using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace GamesHub
{
    /// <summary>On-disk shape of variants.json.</summary>
    public sealed class VariantData
    {
        public int Version = 1;
        public List<VariantGroup> Groups = new List<VariantGroup>();
        /// <summary>Suggestion member sets the user dismissed (each sorted, ordinal-ignore-case).</summary>
        public List<List<string>> Dismissed = new List<List<string>>();
    }

    /// <summary>Not thread-safe by itself; VariantService serializes access.</summary>
    internal static class VariantStore
    {
        public static VariantData Load(string file)
        {
            try
            {
                if (!File.Exists(file)) return new VariantData();
                var data = Json.Deserialize<VariantData>(File.ReadAllText(file, Encoding.UTF8)) ?? new VariantData();
                return Sanitize(data);
            }
            catch (Exception ex) when (ExpectedErrors.IsFileOrJson(ex))
            {
                Log.Warn("Variants: could not read " + file + "; starting empty (backup kept as .bad)", ex);
                try { File.Copy(file, file + ".bad", true); }
                catch (Exception ex2) when (ExpectedErrors.IsFileSystem(ex2)) { Log.Warn("Variants: backup of corrupt file failed", ex2); }
                return new VariantData();
            }
        }

        public static void Save(string file, VariantData data)
        {
            Json.Save(file, data);
        }

        /// <summary>Drops invalid/duplicate entries so the rest of the service can trust the data.</summary>
        public static VariantData Sanitize(VariantData d)
        {
            var clean = new VariantData { Version = 1 };
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var groupIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (VariantGroup g in (d.Groups ?? new List<VariantGroup>()).Where(x => x != null))
            {
                var members = (g.MemberIds ?? new List<string>())
                    .Where(id => !string.IsNullOrWhiteSpace(id) && !used.Contains(id))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (members.Count < 2) continue;
                string gid = string.IsNullOrWhiteSpace(g.Id) || groupIds.Contains(g.Id) ? NewId() : g.Id;
                string primary = members.FirstOrDefault(m => string.Equals(m, g.PrimaryId, StringComparison.OrdinalIgnoreCase)) ?? members[0];
                var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (g.Labels != null)
                {
                    foreach (var kv in g.Labels.Where(l => members.Contains(l.Key, StringComparer.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(l.Value)))
                        labels[kv.Key] = kv.Value.Trim();
                }
                foreach (string m in members) used.Add(m);
                groupIds.Add(gid);
                clean.Groups.Add(new VariantGroup { Id = gid, PrimaryId = primary, MemberIds = members, Labels = labels });
            }
            // Where is lazy: each candidate is checked against the sets already added in this loop.
            foreach (List<string> key in (d.Dismissed ?? new List<List<string>>()).Select(SetKey)
                         .Where(k => k.Count >= 2 && !clean.Dismissed.Any(x => SameSet(x, k))))
                clean.Dismissed.Add(key);
            return clean;
        }

        public static string NewId() => "g" + Guid.NewGuid().ToString("N").Substring(0, 12);

        public static List<string> SetKey(IEnumerable<string> ids)
            => (ids ?? Enumerable.Empty<string>()).Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();

        public static bool SameSet(List<string> a, List<string> b)
            => a.Count == b.Count && !a.Except(b, StringComparer.OrdinalIgnoreCase).Any();

        public static VariantGroup Clone(VariantGroup g) => new VariantGroup
        {
            Id = g.Id,
            PrimaryId = g.PrimaryId,
            MemberIds = new List<string>(g.MemberIds),
            Labels = new Dictionary<string, string>(g.Labels, StringComparer.OrdinalIgnoreCase),
        };
    }
}
