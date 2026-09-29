using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace GamesHub
{
    /// <summary>
    /// Application lifetime: owns the window, bridge, tray, hotkey and Jump List, reacts to library and
    /// settings events, handles activation from other instances and performs the orderly exit.
    /// </summary>
    internal sealed class AppController : ApplicationContext
    {
        private const int RecentCount = 5;
        private static string LegacyMarker => Path.Combine(AppPaths.DataDir, "legacy-import.done");

        public readonly AppSettings Settings;
        public readonly IArtworkService Art;
        public readonly ILibraryService Library;
        public readonly GameCatalog Catalog;
        public readonly IUpdateChecker Updater;
        public readonly MainForm Form;
        public volatile UpdateInfo LastUpdate;

        private readonly StartupArgs _args;
        private readonly SingleInstance _instance;
        private readonly WindowPlacementData _placement;
        private readonly Bridge _bridge;
        private readonly TrayController _tray;
        private readonly GlobalHotkey _hotkey;
        private readonly QuickLaunchController _quick;
        private readonly EventCoalescer _jumpListRefresh;
        private readonly HashSet<string> _running = new HashSet<string>();
        private string _jumpListSignature;
        private string _pendingLaunch;
        private bool _libraryReady, _webReady, _exiting, _formClosed;

        public AppController(AppSettings settings, IArtworkService art, ILibraryService library, GameCatalog catalog, IUpdateChecker updater,
                             StartupArgs args, SingleInstance instance)
        {
            Settings = settings;
            Art = art;
            Library = library;
            Catalog = catalog;
            Updater = updater;
            _args = args;
            _instance = instance;
            _placement = WindowPlacement.Load();

            Form = new MainForm(args.Debug) { CloseHandler = OnCloseRequested };
            Form.FormClosed += (s, e) =>
            {
                _formClosed = true; // already closing: Exit() must not Close/Dispose it re-entrantly
                Exit();
            };
            Form.Prepare(_placement);

            _bridge = new Bridge(this);
            Form.StateChanged += () => _bridge.Emit("windowState", Form.StateDto());
            Form.WebViewReady += OnWebViewReady;
            Form.WebViewFailed += OnWebViewFailed;

            _tray = new TrayController(() => BridgeDto.RecentGames(Catalog.GetGames(), RecentCount));
            _tray.OpenRequested += ShowWindow;
            _tray.SettingsRequested += () => Navigate("settings");
            _tray.GamesFolderRequested += OpenGamesFolder;
            _tray.ExitRequested += () => RunOnUi(Exit); // not inside the menu click that Exit disposes
            _tray.LaunchRequested += id => LaunchFromShell(id);

            _hotkey = new GlobalHotkey();
            _hotkey.Pressed += OnHotkey;
            RegisterHotkey(notify: false);

            _quick = new QuickLaunchController(new CatalogLibraryView(catalog), settings);
            _quick.Launched += (id, r) => RunOnUi(() => OnQuickLaunched(r));

            _jumpListRefresh = new EventCoalescer(RefreshJumpList, 2000);
            Catalog.Changed += _jumpListRefresh.Signal;
            Library.RunningChanged += (id, running) => RunOnUi(() => OnRunningChanged(id, running));
            SettingsStore.Changed += () => _bridge.Emit("settings", SettingsStore.ToDto(Settings));

            _instance.ArgumentsReceived += raw => RunOnUi(() => HandleActivation(StartupArgs.Parse(raw)));
            _instance.StartListening();

            SyncAutostart();
            Form.InitializeWebView();
            Task.Run(StartLibrary);

            if (args.LaunchId != null) _pendingLaunch = args.LaunchId;
            bool startHidden = (args.Minimized || args.LaunchId != null) && !args.Show;
            if (!startHidden) ShowWindow();
        }

        // ------------------------------------------------------------------ threading

        /// <summary>Queues work on the UI thread (no-op once exiting).</summary>
        public void RunOnUi(Action action)
        {
            if (_exiting || Form.IsDisposed || !Form.IsHandleCreated) return;
            try
            {
                Form.BeginInvoke(action);
            }
            catch (InvalidOperationException ex)
            {
                Log.Warn("UI dispatch failed (window closing?)", ex);
            }
        }

        // ------------------------------------------------------------------ startup

        private void StartLibrary()
        {
            try
            {
                if (!File.Exists(LegacyMarker))
                {
                    Art.ImportLegacy(AppPaths.LegacyHubDir(Settings.GamesDir));
                    File.WriteAllText(LegacyMarker, DateTime.Now.ToString("o"));
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Legacy artwork import failed", ex);
            }

            Catalog.RecoverAutomation();
            bool ok = true;
            try
            {
                Library.Start();
            }
            catch (Exception ex)
            {
                ok = false;
                Log.Error("Library start failed", ex);
            }
            RunOnUi(() => OnLibraryStarted(ok));
        }

        private void OnLibraryStarted(bool ok)
        {
            _libraryReady = true;
            _tray.SetBaseState(ok ? TrayState.Normal : TrayState.Error);
            if (!ok) _bridge.Toast("Não foi possível carregar a biblioteca. Veja o log para detalhes.", "err");
            _bridge.SignalGames();
            _jumpListRefresh.Signal();
            if (_pendingLaunch != null)
            {
                string id = _pendingLaunch;
                _pendingLaunch = null;
                LaunchFromShell(id);
            }
        }

        private void OnWebViewReady(CoreWebView2 core)
        {
            _bridge.Attach(core);
            if (_webReady) return;
            _webReady = true;
            StartQuickLaunch();
            if (Settings.CheckUpdates && !string.IsNullOrWhiteSpace(Settings.UpdateRepo))
                Task.Run(StartupUpdateCheck);
        }

        private async Task StartupUpdateCheck()
        {
            try
            {
                await Task.Delay(5000).ConfigureAwait(false); // don't compete with the first scan
                UpdateInfo info = await Updater.CheckAsync().ConfigureAwait(false);
                if (info == null || !info.Available) return;
                LastUpdate = info;
                Log.Info("Update available: " + info.Version);
                _bridge.Emit("update", new Dictionary<string, object>
                {
                    ["version"] = info.Version, ["notes"] = info.Notes, ["pageUrl"] = info.PageUrl,
                });
                RunOnUi(() => _tray.Flash(TrayState.Notification));
            }
            catch (Exception ex)
            {
                Log.Warn("Startup update check failed", ex);
            }
        }

        private void OnWebViewFailed(Exception ex)
        {
            bool missing = ex is WebView2RuntimeNotFoundException;
            if (missing) Program.ShowRuntimeMissing();
            else
                MessageBox.Show("Não foi possível iniciar a interface do GamesHub.\n\n" + ex.Message +
                                "\n\nDetalhes em: " + AppPaths.LogDir, AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
            Exit();
        }

        // ------------------------------------------------------------------ window

        public void ShowWindow()
        {
            if (_exiting) return;
            Form.ShowAndActivate();
        }

        private void Navigate(string view, string id = null)
        {
            ShowWindow();
            var data = new Dictionary<string, object> { ["view"] = view };
            if (id != null) data["id"] = id;
            _bridge.Emit("navigate", data);
        }

        public void HideToTray(bool hint)
        {
            SavePlacement();
            Form.Hide();
            if (hint && !_placement.TrayHintShown)
            {
                _placement.TrayHintShown = true;
                WindowPlacement.Save(_placement);
                _tray.ShowBalloon(AppInfo.Name, "O GamesHub continua rodando na bandeja. Clique duas vezes no ícone para abrir.");
            }
        }

        /// <summary>Close button / Alt+F4: tray or exit, depending on settings.</summary>
        public void RequestClose()
        {
            if (Settings.CloseToTray) HideToTray(hint: true);
            else Exit();
        }

        private bool OnCloseRequested(CloseReason reason)
        {
            if (_exiting) return true;
            if (reason == CloseReason.WindowsShutDown || reason == CloseReason.TaskManagerClosing)
            {
                SavePlacement();
                return true; // FormClosed → Exit()
            }
            RunOnUi(RequestClose);
            return false;
        }

        public Dictionary<string, object> WindowAction(string action, bool? on)
        {
            switch (action)
            {
                case "minimize": Form.MinimizeWindow(); break;
                case "maximize": Form.SetMaximized(true); break;
                case "restore":
                    if (Form.IsFullscreen) Form.SetFullscreen(false);
                    else Form.SetMaximized(false);
                    break;
                case "close": RunOnUi(RequestClose); break;
                case "pin": Form.SetPinned(on ?? !Form.IsPinned); break;
                case "fullscreen": Form.SetFullscreen(on ?? !Form.IsFullscreen); break;
                default: throw new BridgeException("Ação de janela desconhecida: " + action);
            }
            return Form.StateDto();
        }

        /// <summary>settings.onLaunch after a successful launch.</summary>
        public void ApplyOnLaunch()
        {
            switch (Settings.OnLaunch)
            {
                case "tray":
                    if (Form.Visible) HideToTray(hint: true);
                    break;
                case "minimize":
                    if (Form.Visible) Form.MinimizeWindow();
                    break;
            }
        }

        private void SavePlacement()
        {
            if (Form.IsDisposed) return;
            Form.CapturePlacement(_placement);
            WindowPlacement.Save(_placement);
        }

        // ------------------------------------------------------------------ activation / shell

        private void HandleActivation(StartupArgs a)
        {
            Log.Info("Activation from another instance");
            if (a.Quit)
            {
                Exit();
                return;
            }
            if (a.LaunchId != null)
            {
                if (_libraryReady) LaunchFromShell(a.LaunchId);
                else _pendingLaunch = a.LaunchId;
            }
            if (a.Show || a.IsPlain) ShowWindow();
        }

        /// <summary>Launch requested by the tray, Jump List or command line.</summary>
        private async void LaunchFromShell(string id)
        {
            OpResult r;
            try
            {
                r = await Catalog.LaunchAsync(id, null);
            }
            catch (Exception ex)
            {
                Log.Error("Launch failed: " + id, ex);
                r = OpResult.Fail("Não foi possível iniciar o jogo.");
            }
            if (_exiting) return;
            if (r != null && r.Ok)
            {
                ApplyOnLaunch();
                return;
            }
            string msg = r?.Message ?? "Não foi possível iniciar o jogo.";
            _tray.Flash(TrayState.Error);
            if (Form.Visible) _bridge.Toast(msg, "err");
            else _tray.ShowBalloon(AppInfo.Name, msg, ToolTipIcon.Error);
        }

        private void OpenGamesFolder()
        {
            if (!ShellActions.OpenFolder(Settings.GamesDir))
                _tray.ShowBalloon(AppInfo.Name, "A pasta de jogos não foi encontrada:\n" + Settings.GamesDir, ToolTipIcon.Warning);
        }

        private void OnRunningChanged(string id, bool running)
        {
            if (running) _running.Add(id);
            else _running.Remove(id);
            string name = null;
            string current = _running.LastOrDefault();
            if (current != null) name = Library.Get(current)?.Name ?? "";
            _tray.SetRunning(name);
        }

        private void RefreshJumpList()
        {
            List<Game> recent = BridgeDto.RecentGames(Catalog.GetGames(), RecentCount);
            string signature = string.Join("\n", recent.Select(g => g.Id + "\t" + g.Name + "\t" + g.Exe));
            RunOnUi(() =>
            {
                if (signature == _jumpListSignature) return;
                try
                {
                    TaskbarJumpList.Update(recent, Application.ExecutablePath);
                    _jumpListSignature = signature;
                }
                catch (Exception ex)
                {
                    Log.Warn("Jump List update failed", ex);
                }
            });
        }

        // ------------------------------------------------------------------ quick launch

        private void StartQuickLaunch()
        {
            try
            {
                OpResult r = _quick.Start();
                if (r != null && !r.Ok && Settings.QuickLaunchEnabled) _bridge.Toast(r.Message, "err");
            }
            catch (Exception ex)
            {
                Log.Error("Quick launch failed to start", ex);
            }
        }

        private void OnQuickLaunched(OpResult r)
        {
            if (r == null) return;
            if (r.Ok) ApplyOnLaunch();
            else if (Form.Visible) _bridge.Toast(r.Message, "err");
            else _tray.ShowBalloon(AppInfo.Name, r.Message, ToolTipIcon.Error);
        }

        // ------------------------------------------------------------------ hotkey / settings

        private void OnHotkey()
        {
            if (Form.IsForeground)
            {
                HideToTray(hint: false);
                return;
            }
            ShowWindow();
            _bridge.Emit("focusSearch", new Dictionary<string, object>());
        }

        private void RegisterHotkey(bool notify)
        {
            _hotkey.Unregister();
            if (!Settings.HotkeyEnabled) return;
            if (!HotkeyParser.TryParse(Settings.Hotkey, out Hotkey hk))
            {
                Log.Warn("Invalid hotkey in settings: " + Settings.Hotkey);
                return;
            }
            if (!_hotkey.Register(hk) && notify)
                _bridge.Toast("O atalho " + hk.Display + " já está em uso por outro programa.", "err");
        }

        private void SyncAutostart()
        {
            if (!Settings.StartWithWindows) return; // never touch the registry unless the user opted in
            try
            {
                Autostart.Apply(true, Settings.StartMinimized);
            }
            catch (Exception ex)
            {
                Log.Warn("Could not refresh the autostart entry", ex);
            }
        }

        public void ApplySettingsSideEffects(SettingsPatchResult r)
        {
            if (r.Has("startWithWindows") || (r.Has("startMinimized") && Settings.StartWithWindows))
            {
                try
                {
                    Autostart.Apply(Settings.StartWithWindows, Settings.StartMinimized);
                }
                catch (Exception ex)
                {
                    Log.Warn("Could not update the autostart entry", ex);
                    _bridge.Toast("Não foi possível alterar a inicialização com o Windows.", "err");
                }
            }
            if (r.Has("hotkey") || r.Has("hotkeyEnabled")) RegisterHotkey(notify: true);
            if (r.Has("quickLaunchHotkey") || r.Has("quickLaunchEnabled") || r.Has("hotkey"))
            {
                OpResult q = _quick.ApplyHotkey(Settings.QuickLaunchHotkey);
                if (q != null && !q.Ok && Settings.QuickLaunchEnabled) _bridge.Toast(q.Message, "err");
            }

            bool sourcesChanged = r.Has("gamesDir") || r.Has("importSteam") || r.Has("importEpic")
                                  || r.Has("importRiot") || r.Has("importHydra");
            bool artChanged = (r.Has("autoArtwork") && Settings.AutoArtwork) || r.Has("steamGridDbKey");
            if (sourcesChanged || artChanged)
            {
                Task.Run(() =>
                {
                    try { Library.Rescan(); }
                    catch (Exception ex) { Log.Error("Rescan after settings change failed", ex); }
                });
            }
        }

        // ------------------------------------------------------------------ exit

        public void ExitSoon(int delayMs)
        {
            var timer = new Timer { Interval = Math.Max(1, delayMs) };
            timer.Tick += (s, e) =>
            {
                timer.Dispose();
                Exit();
            };
            timer.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Exit();
            base.Dispose(disposing);
        }

        /// <summary>Really exits (bypasses close-to-tray). Idempotent.</summary>
        public void Exit()
        {
            if (_exiting) return;
            SavePlacement();
            _exiting = true;
            Log.Info("Exiting");

            _instance.StopListening();
            _jumpListRefresh.Dispose();
            Catalog.Changed -= _jumpListRefresh.Signal;
            _hotkey.Dispose();
            _quick.Dispose();
            _tray.Dispose();
            _bridge.Dispose();
            try
            {
                Catalog.Dispose();
                Library.Dispose();
            }
            catch (Exception ex)
            {
                Log.Warn("Library dispose failed", ex);
            }
            try
            {
                (Art as IDisposable)?.Dispose(); // after the library: persists the artwork index
            }
            catch (Exception ex)
            {
                Log.Warn("Artwork service dispose failed", ex);
            }

            Form.AllowClose = true;
            if (!_formClosed && !Form.IsDisposed)
            {
                Form.Close();
                Form.Dispose();
            }
            ExitThread();
        }
    }
}
