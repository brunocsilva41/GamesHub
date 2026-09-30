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

        /// <summary>Uninstaller shown to the user per game (getGameInfo): uninstallGame runs exactly that one.</summary>
        private readonly UninstallOffers _uninstallOffers = new UninstallOffers();
        private AutomationRunApprovals _runApprovals;

        private GameCatalog Cat => _app.Catalog;

        private AutomationRunApprovals RunApprovals
            => _runApprovals ?? (_runApprovals = new AutomationRunApprovals(Path.Combine(AppPaths.DataDir, "automation-approvals.json")));

        /// <summary>Returns null when the command is not an integration command.</summary>
        private Task<BridgeResult> ExecuteIntegration(string name, IDictionary<string, object> args)
        {
            switch (name)
            {
                case "getGameInfo": return GetGameInfo(args);
                case "getPcgw": return GetPcgw(args);
                case "openPath": return Done(OpenPath(args));
                case "validateGame": return Done(ValidateGame(args));
                case "uninstallGame": return UninstallGame(RequireString(args, "id"));
                case "getDrives": return Work(DriveUsage);
                case "cleanupBroken": return Work(() => Wrap("results", Cat.CleanupBroken().Select(BridgeDto.OpResultEntry).ToList()));
                case "getGenres": return Work(() => Wrap("genres", Cat.GetGames().SelectMany(g => g.Genres ?? new List<string>())
                    .Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(s => s, StringComparer.CurrentCulture).ToList()));

                case "getAutomation": return Work(() => Camel(AutomationFor(Json.Str(args, "id"))));
                case "saveAutomation": return SaveAutomation(args);
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

                case "discoverGames": return DiscoverGames();
                case "addDiscovered": return AddDiscovered(args);
                default: return null;
            }
        }

        // ------------------------------------------------------------------ discovery (Integrations/Discovery)

        /// <summary>Executables the page may add via addDiscovered: only those returned by the last discoverGames.</summary>
        private readonly DiscoverySession _discovered = new DiscoverySession();
        private int _discovering;

        private async Task<BridgeResult> DiscoverGames()
        {
            if (System.Threading.Interlocked.Exchange(ref _discovering, 1) == 1)
                return BridgeResult.Fail("Uma busca já está em andamento.");
            try
            {
                var progress = new CallbackProgress<string>(text => Emit("discoverProgress", Wrap("text", text)));
                List<DiscoveredGame> found = await new GameDiscovery()
                    .DiscoverAsync(Cat.Library.GetGames(), System.Threading.CancellationToken.None, progress);
                _discovered.Remember(found);
                return BridgeResult.Success(Wrap("candidates", Camel(found)));
            }
            finally
            {
                System.Threading.Interlocked.Exchange(ref _discovering, 0);
            }
        }

        private async Task<BridgeResult> AddDiscovered(IDictionary<string, object> args)
        {
            args.TryGetValue("items", out object items);
            List<DiscoverySession.Item> list = _discovered.Validate(items);
            if (list.Count == 0) return BridgeResult.Fail("Nenhum jogo selecionado.");
            List<OpResult> results = await Task.Run(() => list.Select(AddDiscoveredOne).ToList());
            return BridgeResult.Success(Wrap("results", results.Select(BridgeDto.OpResultEntry).ToList()));
        }

        private OpResult AddDiscoveredOne(DiscoverySession.Item item)
        {
            if (item.Error != null) return OpResult.Fail(item.Error);
            try
            {
                return _app.Library.AddFromFile(item.Exe, item.Name);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException
                                       || ex is ArgumentException || ex is NotSupportedException || ex is InvalidOperationException)
            {
                Log.Error("AddDiscovered failed: " + item.Exe, ex);
                return OpResult.Fail("Não foi possível adicionar " + item.Name + ".");
            }
        }

        private static async Task<BridgeResult> Work(Func<object> fn) => BridgeResult.Success(await Task.Run(fn));

        private static Dictionary<string, object> Wrap(string key, object value) => new Dictionary<string, object> { [key] = value };

        /// <summary>Drives plus every game with the drive it is installed on ("" when the location is unknown,
        /// e.g. launcher shortcuts without an install folder). Sizes of -1 are still being measured.</summary>
        private Dictionary<string, object> DriveUsage()
        {
            List<DriveSpace> drives = Cat.Install.GetDrives();
            var roots = new HashSet<string>(drives.Select(d => d.Name), StringComparer.OrdinalIgnoreCase);
            var games = Cat.GetGames().Select(g =>
            {
                string root = DriveOf(g);
                return new Dictionary<string, object>
                {
                    ["id"] = g.Id, ["name"] = g.Name, ["platform"] = g.Platform, ["sizeBytes"] = g.SizeBytes,
                    ["sizeState"] = Cat.SizeState(g), ["folder"] = GameCatalog.InstallFolderOf(g),
                    ["drive"] = roots.Contains(root) ? root : "",
                };
            }).ToList();
            return new Dictionary<string, object> { ["drives"] = Camel(drives), ["games"] = games };
        }

        /// <summary>Drive root ("D:\") of the game's install folder, else of its executable; "" when unknown.</summary>
        internal static string DriveOf(Game g)
        {
            foreach (string p in new[] { g.InstallDir, g.Exe, g.Ext == ".exe" ? g.FilePath : "" })
            {
                if (string.IsNullOrWhiteSpace(p)) continue;
                try
                {
                    string root = Path.GetPathRoot(p);
                    if (!string.IsNullOrEmpty(root) && root.Length >= 2 && root[1] == ':')
                        return char.ToUpperInvariant(root[0]) + ":\\";
                }
                catch (ArgumentException ex)
                {
                    Log.Warn("Invalid path for " + g.Id, ex);
                }
            }
            return "";
        }

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
                _uninstallOffers.Remember(g.Id, un);
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
            foreach (ResolvedPath p in info.SaveLocations.Concat(info.ConfigLocations).Where(r => r.Exists && !string.IsNullOrEmpty(r.Path)))
                _openablePaths[Path.GetFullPath(p.Path)] = 0;
            return BridgeResult.Success(Camel(info));
        }

        private BridgeResult OpenPath(IDictionary<string, object> args)
        {
            string path = RequireString(args, "path");
            string full;
            try { full = Path.GetFullPath(path); }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException
                                       || ex is System.Security.SecurityException)
            {
                return BridgeResult.Fail("Caminho inválido.");
            }
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

        /// <summary>Runs the uninstaller the details page offered (never a fresh search), after a native
        /// confirmation for registry uninstallers. The prompt runs on the UI thread, the start off it.</summary>
        private async Task<BridgeResult> UninstallGame(string id)
        {
            Game g = Cat.Get(id);
            if (g == null) return BridgeResult.Fail("Jogo não encontrado.");
            UninstallInfo offer = _uninstallOffers.Get(id);
            OpResult refused = UninstallGate.Check(g.Name, offer, ConfirmUninstall);
            if (refused != null) return BridgeResult.From(refused);
            OpResult r = await Task.Run(() => Cat.Install.RunUninstaller(offer));
            if (r != null && r.Ok) _uninstallOffers.Forget(id);
            return BridgeResult.From(r);
        }

        private bool ConfirmUninstall(UninstallInfo offer)
            => _app.Form.ConfirmDangerous("Desinstalar jogo", UninstallGate.Describe(offer, UninstallCommand.Parse(offer.Command)),
                                          "Deseja abrir este desinstalador?");

        private AutomationProfile AutomationFor(string id)
            => string.IsNullOrEmpty(id) ? Cat.Automation.GetDefaultProfile() : Cat.Automation.GetProfile(id);

        /// <summary>New or changed "run" actions need a native confirmation; declined ones are left out and the
        /// rest of the profile is saved (the reply then carries the profile that was actually stored).</summary>
        private async Task<BridgeResult> SaveAutomation(IDictionary<string, object> args)
        {
            string id = Json.Str(args, "id");
            AutomationProfile incoming = ParseProfile(Json.Obj(args, "profile"));
            AutomationProfile p = AutomationRunGate.Decide(incoming, AutomationFor(id), RunApprovals, ConfirmRuns, out int refused);
            OpResult r = await Task.Run(() => string.IsNullOrEmpty(id) ? Cat.Automation.SaveDefaultProfile(p) : Cat.Automation.SaveProfile(id, p));
            if (r == null || !r.Ok || refused == 0) return BridgeResult.From(r);
            Dictionary<string, object> data = BridgeDto.OpResultData(r);
            data["message"] = refused == 1
                ? "Automação salva sem o programa que não foi autorizado."
                : "Automação salva sem os " + refused + " programas que não foram autorizados.";
            data["profile"] = Camel(AutomationFor(id));
            data["refusedRuns"] = refused;
            return BridgeResult.Success(data);
        }

        private bool ConfirmRuns(IList<AutomationAction> runs)
            => _app.Form.ConfirmDangerous("Autorizar programa na automação", AutomationRunGate.Describe(runs),
                                          "Sim: autorizar e salvar. Não: salvar a automação sem esses programas.");

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
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException || ex is IOException)
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
