// OWNER: integration (lead). Bridge commands for the wave-2 services (see docs/ARCHITECTURE.md → Wave 2).
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace GamesHub
{
    internal sealed partial class Bridge
    {
        /// <summary>Folders the UI may open via openPath: only ones previously returned by getPcgw.</summary>
        private readonly ConcurrentDictionary<string, byte> _openablePaths =
            new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

        private GameCatalog Cat => _app.Catalog;

        /// <summary>Returns null when the command is not a wave-2 command.</summary>
        private Task<BridgeResult> ExecuteWave2(string name, IDictionary<string, object> args)
        {
            switch (name)
            {
                case "getGameInfo": return GetGameInfo(args);
                case "getPcgw": return GetPcgw(args);
                case "openPath": return Done(OpenPath(args));
                case "validateGame": return Done(ValidateGame(args));
                case "uninstallGame": return Lib(() => UninstallGame(RequireString(args, "id")));
                case "getDrives": return Work(() => Wrap("drives", Camel(Cat.Install.GetDrives())));
                case "cleanupBroken": return Work(() => Wrap("results", Cat.CleanupBroken().Select(BridgeDto.OpResultEntry).ToList()));
                case "getGenres": return Work(() => Wrap("genres", Cat.GetGames().SelectMany(g => g.Genres ?? new List<string>())
                    .Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(s => s, StringComparer.CurrentCulture).ToList()));

                case "getAutomation": return Work(() => Camel(AutomationFor(Json.Str(args, "id"))));
                case "saveAutomation": return Lib(() => SaveAutomation(args));
                case "automationOptions": return Work(() => new Dictionary<string, object>
                {
                    ["powerPlans"] = Camel(Cat.Automation.ListPowerPlans()),
                    ["audioDevices"] = Camel(Cat.Automation.ListAudioDevices()),
                    ["resolutions"] = Camel(Cat.Automation.ListResolutions()),
                });

                case "variantGroups": return Work(() => Wrap("groups", Camel(Cat.Variants.GetGroups())));
                case "variantSuggestions": return Work(() => Wrap("groups", Camel(Cat.Variants.Suggest(Cat.Library.GetGames()))));
                case "groupVariants": return Lib(() => Cat.Variants.Group(StringList(args, "ids"), RequireString(args, "primaryId")));
                case "ungroupVariants": return Lib(() => Cat.Variants.Ungroup(RequireString(args, "groupId")));
                case "setVariantLabel": return Lib(() => Cat.Variants.SetLabel(RequireString(args, "id"), Json.Str(args, "label")));
                case "setVariantPrimary": return Lib(() => Cat.Variants.SetPrimary(RequireString(args, "groupId"), RequireString(args, "primaryId")));
                case "dismissVariants": return Lib(() => Cat.Variants.DismissSuggestion(StringList(args, "ids")));
                default: return null;
            }
        }

        private static async Task<BridgeResult> Work(Func<object> fn) => BridgeResult.Success(await Task.Run(fn));

        private static Dictionary<string, object> Wrap(string key, object value) => new Dictionary<string, object> { [key] = value };

        private Game RequireCatalogGame(IDictionary<string, object> args)
        {
            Game g = Cat.Get(RequireString(args, "id"));
            if (g == null) throw new BridgeException("Jogo não encontrado.");
            return g;
        }

        private async Task<BridgeResult> GetGameInfo(IDictionary<string, object> args)
        {
            Game g = RequireCatalogGame(args);
            return BridgeResult.Success(await Task.Run(async () =>
            {
                string appId = Cat.MatchedAppId(g);
                GameInfo info = null;
                if (appId.Length > 0)
                    info = Cat.Meta.GetCached(appId) ?? (Cat.Settings.FetchMetadata ? await Cat.Meta.FetchAsync(appId).ConfigureAwait(false) : null);
                SteamLocalStats steam = null;
                if (!string.IsNullOrEmpty(g.SteamAppId)) Cat.Steam.Load()?.TryGetValue(g.SteamAppId, out steam);
                UninstallInfo un = Cat.Install.FindUninstaller(g) ?? new UninstallInfo();
                long size = g.SizeBytes >= 0 ? g.SizeBytes : await Cat.Install.GetSizeBytesAsync(g).ConfigureAwait(false);
                return new Dictionary<string, object>
                {
                    ["appId"] = appId,
                    ["info"] = info == null ? null : Camel(info),
                    ["steam"] = steam == null ? null : Camel(steam),
                    ["health"] = new Dictionary<string, object> { ["broken"] = g.Broken, ["reason"] = g.BrokenReason ?? "" },
                    ["uninstall"] = new Dictionary<string, object> { ["method"] = un.Method, ["displayName"] = un.DisplayName ?? "" },
                    ["canValidate"] = !string.IsNullOrEmpty(g.SteamAppId) && g.Platform == "Steam",
                    ["sizeBytes"] = size,
                    ["pcgwUrl"] = Cat.Pcgw.PageUrl(g, appId),
                };
            }));
        }

        private async Task<BridgeResult> GetPcgw(IDictionary<string, object> args)
        {
            Game g = RequireCatalogGame(args);
            PcgwInfo info = await Task.Run(() => Cat.Pcgw.GetInfoAsync(g, Cat.MatchedAppId(g)));
            info = info ?? new PcgwInfo();
            foreach (ResolvedPath p in info.SaveLocations.Concat(info.ConfigLocations))
                if (p.Exists && !string.IsNullOrEmpty(p.Path)) _openablePaths[Path.GetFullPath(p.Path)] = 0;
            return BridgeResult.Success(Camel(info));
        }

        private BridgeResult OpenPath(IDictionary<string, object> args)
        {
            string path = RequireString(args, "path");
            string full;
            try { full = Path.GetFullPath(path); }
            catch (Exception) { return BridgeResult.Fail("Caminho inválido."); }
            if (!_openablePaths.ContainsKey(full)) return BridgeResult.Fail("Este caminho não pode ser aberto.");
            if (Directory.Exists(full) ? ShellActions.OpenFolder(full) : File.Exists(full) && ShellActions.Reveal(full))
                return BridgeResult.From(OpResult.Success("Abrindo no Explorador de Arquivos."));
            return BridgeResult.Fail("A pasta não existe mais.");
        }

        private BridgeResult ValidateGame(IDictionary<string, object> args)
        {
            Game g = RequireCatalogGame(args);
            string uri = string.IsNullOrEmpty(g.SteamAppId) ? "" : Cat.Steam.ValidateUri(g.SteamAppId);
            if (uri.Length == 0) return BridgeResult.Fail("A verificação de arquivos só está disponível para jogos da Steam.");
            return ShellOpen(uri)
                ? BridgeResult.From(OpResult.Success("A Steam vai verificar os arquivos de " + g.Name + ".", g.Id))
                : BridgeResult.Fail("Não foi possível abrir a Steam.");
        }

        private OpResult UninstallGame(string id)
        {
            Game g = Cat.Get(id);
            if (g == null) return OpResult.Fail("Jogo não encontrado.");
            UninstallInfo info = Cat.Install.FindUninstaller(g);
            if (info == null || info.Method == "none")
                return OpResult.Fail("Não encontrei um desinstalador para " + g.Name + ". Use Configurações do Windows › Aplicativos.");
            return Cat.Install.RunUninstaller(info);
        }

        private AutomationProfile AutomationFor(string id)
            => string.IsNullOrEmpty(id) ? Cat.Automation.GetDefaultProfile() : Cat.Automation.GetProfile(id);

        private OpResult SaveAutomation(IDictionary<string, object> args)
        {
            string id = Json.Str(args, "id");
            AutomationProfile p = ParseProfile(Json.Obj(args, "profile"));
            return string.IsNullOrEmpty(id) ? Cat.Automation.SaveDefaultProfile(p) : Cat.Automation.SaveProfile(id, p);
        }

        private static AutomationProfile ParseProfile(IDictionary<string, object> d)
        {
            if (d == null) throw new BridgeException("Perfil de automação inválido.");
            return new AutomationProfile
            {
                Enabled = Json.Bool(d, "enabled", true),
                UseDefault = Json.Bool(d, "useDefault", true),
                Before = ParseActions(d, "before"),
                After = ParseActions(d, "after"),
            };
        }

        private static List<AutomationAction> ParseActions(IDictionary<string, object> d, string key)
        {
            var list = new List<AutomationAction>();
            if (!(d.TryGetValue(key, out object v) && v is IEnumerable items) || v is string) return list;
            foreach (object o in items)
            {
                if (!(o is IDictionary<string, object> a)) continue;
                list.Add(new AutomationAction
                {
                    Type = Json.Str(a, "type"),
                    Target = Json.Str(a, "target"),
                    Args = Json.Str(a, "args"),
                    Seconds = (int)Math.Max(0, Math.Min(30, Json.Long(a, "seconds"))),
                    Enabled = Json.Bool(a, "enabled", true),
                });
            }
            return list;
        }

        private static List<string> StringList(IDictionary<string, object> args, string key)
        {
            if (!(args.TryGetValue(key, out object v) && v is IEnumerable items) || v is string)
                throw new BridgeException("Lista de jogos inválida.");
            return items.Cast<object>().Select(Convert.ToString).Where(s => !string.IsNullOrEmpty(s)).ToList();
        }

        private static bool ShellOpen(string uri)
        {
            try
            {
                Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true })?.Dispose();
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("Shell open failed: " + uri, ex);
                return false;
            }
        }

        /// <summary>Contract objects (public fields/properties) → camelCase JSON-ready structures.</summary>
        internal static object Camel(object o)
        {
            switch (o)
            {
                case null: return null;
                case string s: return s;
                case bool _: case int _: case long _: case double _: case float _: case decimal _: return o;
                case DateTime dt: return dt.ToString("o");
                case IDictionary dict:
                    var d = new Dictionary<string, object>();
                    foreach (DictionaryEntry e in dict) d[Convert.ToString(e.Key)] = Camel(e.Value);
                    return d;
                case IEnumerable seq:
                    return seq.Cast<object>().Select(Camel).ToList();
            }
            Type t = o.GetType();
            if (t.IsEnum) return o.ToString();
            var result = new Dictionary<string, object>();
            foreach (FieldInfo f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
                result[CamelName(f.Name)] = Camel(f.GetValue(o));
            foreach (PropertyInfo p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanRead && p.GetIndexParameters().Length == 0))
                result[CamelName(p.Name)] = Camel(p.GetValue(o));
            return result;
        }

        private static string CamelName(string n) => n.Length == 0 ? n : char.ToLowerInvariant(n[0]) + n.Substring(1);
    }
}
