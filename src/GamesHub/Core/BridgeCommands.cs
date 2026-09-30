using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace GamesHub
{
    /// <summary>Bridge command handlers. They start on the UI thread; I/O-bound work goes through Task.Run.</summary>
    internal sealed partial class Bridge
    {
        private static readonly string[] GameFileExts = { ".exe", ".lnk", ".url" };
        private int _lastProgress = -1;

        private Task<BridgeResult> Execute(string name, IDictionary<string, object> args, List<string> files)
        {
            switch (name)
            {
                case "getState": return GetState();
                case "launch": return Launch(args);
                case "reveal": return Done(Reveal(args));
                case "remove": return Lib(() => _app.Library.Remove(RequireString(args, "id")));
                case "undo": return Lib(() => _app.Library.Undo(RequireString(args, "token")));
                case "updateGame": return UpdateGame(args);
                case "addFiles": return AddFiles(files);
                case "pickFile": return PickFile();
                case "addSteam": return AddSteam(args);
                case "searchSteam": return SearchSteam(args);
                case "setArt": return SetArt(args, files);
                case "clearArt": return ClearArt(args);
                case "refreshArt": return RefreshArt(args);
                case "rescan": return Rescan();
                case "setSettings": return Done(SetSettings(args));
                case "pickGamesDir": return Done(PickGamesDir());
                case "window": return Done(WindowCommand(args));
                case "openExternal": return Done(OpenExternal(args));
                case "openDataFolder":
                    ShellActions.OpenFolder(Json.Str(args, "sub") == "logs" ? AppPaths.LogDir : AppPaths.DataDir);
                    return Done(BridgeResult.Success(Empty()));
                case "checkUpdate": return CheckUpdate();
                case "installUpdate": return InstallUpdate();
                case "log": return Done(WebLog(args));
                case "quit":
                    _app.RunOnUi(_app.Exit); // queued: the reply is posted first
                    return Done(BridgeResult.Success(Empty()));
                default:
                    Task<BridgeResult> extra = ExecuteIntegration(name, args);
                    if (extra != null) return extra;
                    Log.Warn("Bridge: unknown command '" + name + "'");
                    return Done(BridgeResult.Fail("Comando desconhecido: " + name));
            }
        }

        /// <summary>Runs a library operation off the UI thread and maps its OpResult.</summary>
        private static async Task<BridgeResult> Lib(Func<OpResult> op) => BridgeResult.From(await Task.Run(op));

        private async Task<BridgeResult> GetState()
        {
            Dictionary<string, object> games = await Task.Run(() => GamesPayload());
            games["settings"] = SettingsStore.ToDto(_app.Settings);
            games["version"] = AppInfo.Version;
            games["windowState"] = _app.Form.StateDto();
            games["demo"] = false;
            return BridgeResult.Success(games);
        }

        private async Task<BridgeResult> Launch(IDictionary<string, object> args)
        {
            string id = RequireString(args, "id");
            string variantId = Json.Str(args, "variantId");
            OpResult r = await Task.Run(() => _app.Catalog.LaunchAsync(id, variantId));
            if (r != null && r.Ok) _app.ApplyOnLaunch();
            return BridgeResult.From(r);
        }

        private BridgeResult Reveal(IDictionary<string, object> args)
        {
            string id = RequireString(args, "id");
            string path = _app.Library.RevealPath(id);
            if (string.IsNullOrEmpty(path) || !(File.Exists(path) || Directory.Exists(path)))
                return BridgeResult.Fail("Não foi possível encontrar a pasta deste jogo.");
            return ShellActions.Reveal(path)
                ? BridgeResult.From(OpResult.Success("Abrindo no Explorador de Arquivos.", id))
                : BridgeResult.Fail("Não foi possível abrir o Explorador de Arquivos.");
        }

        private Task<BridgeResult> UpdateGame(IDictionary<string, object> args)
        {
            string id = RequireString(args, "id");
            GameEdit edit = BridgeDto.ParseEdit(Json.Obj(args, "edit"));
            return Lib(() => _app.Library.Update(id, edit));
        }

        private async Task<BridgeResult> AddFiles(List<string> files)
        {
            if (files.Count == 0) return BridgeResult.Fail("Nenhum arquivo recebido.");
            List<OpResult> results = await Task.Run(() => files.Select(AddOne).ToList());
            return BridgeResult.Success(new Dictionary<string, object>
            {
                ["results"] = results.Select(BridgeDto.OpResultEntry).ToList(),
            });
        }

        private OpResult AddOne(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (!GameFileExts.Contains(ext))
                return OpResult.Fail("Tipo de arquivo não suportado: " + Path.GetFileName(path));
            try
            {
                return _app.Library.AddFromFile(path, Path.GetFileNameWithoutExtension(path));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException
                                       || ex is ArgumentException || ex is NotSupportedException || ex is InvalidOperationException)
            {
                Log.Error("AddFromFile failed: " + path, ex);
                return OpResult.Fail("Não foi possível adicionar " + Path.GetFileName(path) + ".");
            }
        }

        private async Task<BridgeResult> PickFile()
        {
            string path = NativeDialogs.PickGameFile(_app.Form);
            if (path == null) return BridgeResult.Success(Cancelled());
            return BridgeResult.From(await Task.Run(() => AddOne(path)));
        }

        private Task<BridgeResult> AddSteam(IDictionary<string, object> args)
        {
            string appId = RequireString(args, "appId");
            if (!appId.All(char.IsDigit)) throw new BridgeException("O ID da Steam deve conter apenas números.");
            string name = Json.Str(args, "name").Trim();
            return Lib(() => _app.Library.AddSteamApp(appId, name));
        }

        private async Task<BridgeResult> SearchSteam(IDictionary<string, object> args)
        {
            string query = Json.Str(args, "query").Trim();
            var results = new List<SteamSearchResult>();
            if (query.Length > 0)
                results = await Task.Run(() => _app.Art.SearchSteamAsync(query)) ?? results;
            return BridgeResult.Success(new Dictionary<string, object>
            {
                ["results"] = results.Select(r => new Dictionary<string, object>
                {
                    ["appId"] = r.AppId ?? "", ["name"] = r.Name ?? "", ["iconUrl"] = r.IconUrl ?? "",
                }).ToList(),
            });
        }

        private async Task<BridgeResult> SetArt(IDictionary<string, object> args, List<string> files)
        {
            Game game = RequireGame(args);
            string kind = RequireArtKind(args);
            string source = files.FirstOrDefault() ?? NativeDialogs.PickImage(_app.Form);
            if (source == null) return BridgeResult.Success(Cancelled());
            return BridgeResult.From(await Task.Run(() => _app.Art.SetCustomImage(game, kind, source)));
        }

        private async Task<BridgeResult> ClearArt(IDictionary<string, object> args)
        {
            Game game = RequireGame(args);
            string kind = RequireArtKind(args);
            return BridgeResult.From(await Task.Run(() => _app.Art.ClearCustomImage(game, kind)));
        }

        private async Task<BridgeResult> RefreshArt(IDictionary<string, object> args)
        {
            Game game = RequireGame(args);
            await Task.Run(() => _app.Art.Refresh(game));
            return BridgeResult.From(OpResult.Success("Buscando imagens novamente…", game.Id));
        }

        private async Task<BridgeResult> Rescan()
        {
            await Task.Run(() => _app.Library.Rescan());
            return BridgeResult.From(OpResult.Success("Biblioteca atualizada."));
        }

        private BridgeResult SetSettings(IDictionary<string, object> args)
        {
            IDictionary<string, object> patch = Json.Obj(args, "patch");
            if (patch == null) throw new BridgeException("Configurações inválidas.");
            SettingsPatchResult r = SettingsStore.ApplyPatch(patch);
            _app.ApplySettingsSideEffects(r);
            if (r.Rejected.Count > 0 && r.Changed.Count == 0)
                return BridgeResult.Fail(RejectionMessage(r.Rejected));
            if (r.Rejected.Count > 0) Toast(RejectionMessage(r.Rejected), "err");
            return BridgeResult.Success(SettingsStore.ToDto(_app.Settings));
        }

        private static string RejectionMessage(List<string> rejected)
        {
            if (rejected.Contains("gamesDir")) return "A pasta de jogos escolhida não existe.";
            if (rejected.Contains("hotkey")) return "Atalho inválido. Use algo como Ctrl+Alt+G.";
            if (rejected.Contains("updateRepo")) return "Repositório inválido. Use o formato dono/repositorio.";
            if (rejected.Contains("steamGridDbKey")) return "Chave do SteamGridDB inválida.";
            return "Alguns valores inválidos foram ignorados.";
        }

        private BridgeResult PickGamesDir()
        {
            string dir = NativeDialogs.PickFolder(_app.Form, _app.Settings.GamesDir, "Escolha a pasta de jogos");
            if (dir == null) return BridgeResult.Success(Cancelled());
            return SetSettings(new Dictionary<string, object>
            {
                ["patch"] = new Dictionary<string, object> { ["gamesDir"] = dir },
            });
        }

        private BridgeResult WindowCommand(IDictionary<string, object> args)
        {
            string action = RequireString(args, "action").ToLowerInvariant();
            bool? on = args.TryGetValue("on", out object v) && v is bool b ? b : (bool?)null;
            return BridgeResult.Success(_app.WindowAction(action, on));
        }

        private static BridgeResult OpenExternal(IDictionary<string, object> args)
        {
            string url = Json.Str(args, "url").Trim();
            if (!ShellActions.IsSafeExternalUrl(url)) return BridgeResult.Fail("Só é possível abrir endereços https.");
            return ShellActions.OpenUrl(url)
                ? BridgeResult.Success(Empty())
                : BridgeResult.Fail("Não foi possível abrir o navegador.");
        }

        private async Task<BridgeResult> CheckUpdate()
        {
            if (string.IsNullOrWhiteSpace(_app.Settings.UpdateRepo))
                return BridgeResult.Success(BridgeDto.Update(new UpdateInfo { Version = AppInfo.Version }));
            UpdateInfo info = await Task.Run(() => _app.Updater.CheckAsync());
            _app.LastUpdate = info;
            return BridgeResult.Success(BridgeDto.Update(info));
        }

        private async Task<BridgeResult> InstallUpdate()
        {
            if (string.IsNullOrWhiteSpace(_app.Settings.UpdateRepo))
                return BridgeResult.Success(new Dictionary<string, object> { ["started"] = false });
            UpdateInfo info = _app.LastUpdate;
            if (info == null || !info.Available)
            {
                info = await Task.Run(() => _app.Updater.CheckAsync());
                _app.LastUpdate = info;
            }
            if (info == null || !info.Available)
                return BridgeResult.Fail((_app.Updater as UpdateChecker)?.LastError ?? "Nenhuma atualização disponível.");

            _lastProgress = -1;
            bool started = await Task.Run(() => _app.Updater.DownloadAndInstallAsync(info, ReportProgress));
            if (started)
            {
                Log.Info("Update " + info.Version + " installer started; exiting");
                _app.ExitSoon(1500); // let the installer replace our files
            }
            if (!started) return BridgeResult.Fail((_app.Updater as UpdateChecker)?.LastError ?? "Não foi possível baixar a atualização.");
            return BridgeResult.Success(new Dictionary<string, object> { ["started"] = started });
        }

        private void ReportProgress(int percent)
        {
            percent = Math.Max(0, Math.Min(100, percent));
            if (percent == System.Threading.Interlocked.Exchange(ref _lastProgress, percent)) return;
            Emit("updateProgress", new Dictionary<string, object> { ["percent"] = percent });
        }

        private static BridgeResult WebLog(IDictionary<string, object> args)
        {
            string msg = "[web] " + ShellActions.Truncate(Json.Str(args, "msg"), 2000);
            switch (Json.Str(args, "level"))
            {
                case "error": Log.Error(msg); break;
                case "warn": Log.Warn(msg); break;
                default: Log.Info(msg); break;
            }
            return BridgeResult.Success(Empty());
        }
    }
}
