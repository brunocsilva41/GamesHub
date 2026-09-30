// "Jogar ▾" menu (e.g. "LEGO Marvel Super Heroes 2" + "... DirectX 11", "Black Ops 3" + "Plutonium").
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GamesHub
{
    public sealed class VariantService : IVariantService
    {
        private const int MaxLabelLength = 40;
        private readonly object _gate = new object();
        private readonly string _file;
        private readonly VariantData _data;

        /// <summary>Raised (outside the lock) after any persisted change; the integration layer should re-publish games.</summary>
        public event Action Changed;

        public VariantService() : this(Path.Combine(AppPaths.DataDir, "variants.json")) { }

        /// <summary>Storage path is injectable for tests.</summary>
        public VariantService(string storeFile)
        {
            _file = storeFile;
            _data = VariantStore.Load(storeFile);
        }

        public static string BaseName(string name) => VariantNames.BaseName(name);
        public static string Qualifier(string name) => VariantNames.Qualifier(name);

        // ------------------------------------------------------------------ queries

        public List<VariantGroup> Suggest(List<Game> games)
        {
            HashSet<string> grouped;
            List<List<string>> dismissed;
            lock (_gate)
            {
                grouped = new HashSet<string>(_data.Groups.SelectMany(g => g.MemberIds), StringComparer.OrdinalIgnoreCase);
                dismissed = _data.Dismissed.Select(d => new List<string>(d)).ToList();
            }
            return VariantSuggester.Suggest(games, grouped, dismissed);
        }

        public List<VariantGroup> GetGroups()
        {
            lock (_gate) return _data.Groups.Select(VariantStore.Clone).ToList();
        }

        public List<Game> Apply(List<Game> games)
        {
            List<VariantGroup> groups = GetGroups();
            return VariantApplier.Apply(games, groups);
        }

        /// <summary>Group id containing this member, or null.</summary>
        public string GroupOf(string memberId)
        {
            lock (_gate) return FindByMember(memberId)?.Id;
        }

        /// <summary>For `launch {id, variantId}`: returns variantId when it belongs to the same group as id
        /// (so the library launches that member game), otherwise id.</summary>
        public string ResolveLaunchId(string id, string variantId)
        {
            if (string.IsNullOrWhiteSpace(variantId) || Same(id, variantId)) return id;
            lock (_gate)
            {
                VariantGroup g = FindByMember(id);
                return g != null && g.MemberIds.Contains(variantId, StringComparer.OrdinalIgnoreCase) ? variantId : id;
            }
        }

        // ------------------------------------------------------------------ commands

        public OpResult Group(List<string> memberIds, string primaryId)
        {
            List<string> members = (memberIds ?? new List<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (members.Count < 2) return OpResult.Fail("Selecione pelo menos dois jogos para agrupar.");
            if (string.IsNullOrWhiteSpace(primaryId)) primaryId = members[0];
            string primary = members.FirstOrDefault(m => Same(m, primaryId));
            if (primary == null) return OpResult.Fail("O jogo principal precisa fazer parte do grupo.");
            members.Remove(primary);
            members.Insert(0, primary);

            lock (_gate)
            {
                string taken = members.FirstOrDefault(m => FindByMember(m) != null);
                if (taken != null) return OpResult.Fail("Um dos jogos já faz parte de outro grupo de variantes. Desagrupe-o primeiro.");
                var group = new VariantGroup { Id = VariantStore.NewId(), PrimaryId = primary, MemberIds = members,
                    Labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) };
                _data.Groups.Add(group);
                // A group supersedes an earlier "não sugerir" for the same set.
                List<string> key = VariantStore.SetKey(members);
                _data.Dismissed.RemoveAll(d => VariantStore.SameSet(d, key));
                if (!Persist()) { _data.Groups.Remove(group); return SaveFailed(); }
            }
            RaiseChanged();
            return OpResult.Success("Variantes agrupadas em um único jogo.", primary);
        }

        public OpResult Ungroup(string groupId)
        {
            VariantGroup g;
            lock (_gate)
            {
                g = _data.Groups.FirstOrDefault(x => Same(x.Id, groupId));
                if (g == null) return OpResult.Fail("Grupo de variantes não encontrado.");
                int index = _data.Groups.IndexOf(g);
                _data.Groups.RemoveAt(index);
                if (!Persist()) { _data.Groups.Insert(index, g); return SaveFailed(); }
            }
            RaiseChanged();
            return OpResult.Success("Variantes desagrupadas.", g.PrimaryId);
        }

        public OpResult DismissSuggestion(List<string> memberIds)
        {
            List<string> key = VariantStore.SetKey(memberIds);
            if (key.Count < 2) return OpResult.Fail("Sugestão inválida.");
            lock (_gate)
            {
                if (!_data.Dismissed.Any(d => VariantStore.SameSet(d, key)))
                {
                    _data.Dismissed.Add(key);
                    if (!Persist()) { _data.Dismissed.RemoveAt(_data.Dismissed.Count - 1); return SaveFailed(); }
                }
            }
            return OpResult.Success("Sugestão ignorada. Ela não será mostrada novamente.");
        }

        public OpResult SetLabel(string memberId, string label)
        {
            string text = (label ?? "").Trim();
            if (text.Length > MaxLabelLength) return OpResult.Fail($"O nome da variante pode ter no máximo {MaxLabelLength} caracteres.");
            string primaryId;
            lock (_gate)
            {
                VariantGroup g = FindByMember(memberId);
                if (g == null) return OpResult.Fail("Este jogo não faz parte de um grupo de variantes.");
                string id = g.MemberIds.First(m => Same(m, memberId));
                if (text.Length > 0 && g.Labels.Any(kv => !Same(kv.Key, id) && string.Equals(kv.Value, text, StringComparison.OrdinalIgnoreCase)))
                    return OpResult.Fail("Já existe uma variante com esse nome neste grupo.");
                g.Labels.TryGetValue(id, out string old);
                if (text.Length == 0) g.Labels.Remove(id); else g.Labels[id] = text;
                if (!Persist())
                {
                    if (old == null) g.Labels.Remove(id); else g.Labels[id] = old;
                    return SaveFailed();
                }
                primaryId = g.PrimaryId;
            }
            RaiseChanged();
            return OpResult.Success(text.Length == 0 ? "Nome da variante restaurado." : "Nome da variante atualizado.", primaryId);
        }

        public OpResult SetPrimary(string groupId, string primaryId)
        {
            string id;
            lock (_gate)
            {
                VariantGroup g = _data.Groups.FirstOrDefault(x => Same(x.Id, groupId));
                if (g == null) return OpResult.Fail("Grupo de variantes não encontrado.");
                id = g.MemberIds.FirstOrDefault(m => Same(m, primaryId));
                if (id == null) return OpResult.Fail("Esse jogo não faz parte do grupo.");
                if (Same(g.PrimaryId, id)) return OpResult.Success("Esta já é a variante principal.", id);
                string oldPrimary = g.PrimaryId;
                List<string> oldOrder = new List<string>(g.MemberIds);
                g.PrimaryId = id;
                g.MemberIds.Remove(id);
                g.MemberIds.Insert(0, id);
                if (!Persist()) { g.PrimaryId = oldPrimary; g.MemberIds = oldOrder; return SaveFailed(); }
            }
            RaiseChanged();
            return OpResult.Success("Variante principal alterada.", id);
        }

        // ------------------------------------------------------------------ helpers

        private VariantGroup FindByMember(string memberId)
            => string.IsNullOrWhiteSpace(memberId) ? null
               : _data.Groups.FirstOrDefault(g => g.MemberIds.Contains(memberId.Trim(), StringComparer.OrdinalIgnoreCase));

        private static bool Same(string a, string b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

        /// <summary>Caller holds the lock.</summary>
        private bool Persist()
        {
            try { VariantStore.Save(_file, _data); return true; }
            catch (Exception ex) when (ExpectedErrors.IsFileOrJson(ex)) { Log.Error("Variants: failed to save " + _file, ex); return false; }
        }

        private static OpResult SaveFailed() => OpResult.Fail("Não foi possível salvar as variantes. Tente novamente.");

        private void RaiseChanged()
        {
            try { Changed?.Invoke(); }
            // Resilience boundary: raises an event to arbitrary subscribers.
            catch (Exception ex) { Log.Warn("Variants: Changed handler failed", ex); }
        }
    }
}
