// Wiring: construct after the library; call Start() on the UI thread once the main form's handle
// exists; call ApplyHotkey(settings.QuickLaunchHotkey) after settings change; Dispose() on exit.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GamesHub
{
    public sealed class QuickLaunchController : IQuickLaunch
    {
        private readonly ILibraryService _library;
        private readonly AppSettings _settings;
        private readonly object _indexGate = new object();
        private List<QuickEntry> _index;
        private int _dirty = 1;
        private QuickHotkeyWindow _hotkey;
        private QuickLaunchForm _form;
        private SynchronizationContext _ui;
        private int _uiThreadId = -1;
        private bool _pendingShow;
        private bool _disposed;

        /// <summary>Raised on the UI thread after a launch attempt from the palette (gameId, result) so the
        /// host can apply settings.OnLaunch / show a toast. Optional.</summary>
        public event Action<string, OpResult> Launched;

        public QuickLaunchController(ILibraryService library, AppSettings settings)
        {
            _library = library ?? throw new ArgumentNullException(nameof(library));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _library.Changed += OnLibraryChanged;
            _library.RunningChanged += OnRunningChanged;
        }

        // ------------------------------------------------------------ IQuickLaunch

        /// <summary>Must be called on the UI thread. Registers the hotkey and pre-creates the palette (hidden).</summary>
        public OpResult Start()
        {
            if (_disposed) return OpResult.Fail("Paleta rápida encerrada.");
            if (_ui == null)
            {
                _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
                _uiThreadId = Thread.CurrentThread.ManagedThreadId;
                _hotkey = new QuickHotkeyWindow();
                _hotkey.Pressed += Toggle;
            }
            return ApplyHotkey(_settings.QuickLaunchHotkey);
        }

        public OpResult ApplyHotkey(string hotkey)
        {
            if (_disposed) return OpResult.Fail("Paleta rápida encerrada.");
            if (_ui == null) return OpResult.Fail("A paleta rápida ainda não foi iniciada.");
            if (!OnUiThread())
            {
                OpResult r = null;
                _ui.Send(_ => r = ApplyHotkey(hotkey), null);
                return r;
            }
            if (!_settings.QuickLaunchEnabled)
            {
                _hotkey.Unregister();
                _form?.HidePalette();
                return OpResult.Success("Paleta rápida desativada.");
            }
            if (!QuickHotkey.TryParse(hotkey, out QuickHotkey hk))
                return OpResult.Fail("Atalho inválido: “" + (hotkey ?? "") + "”. Use Ctrl, Alt, Shift ou Win com uma tecla (ex.: Ctrl+Shift+Space).");
            if (_settings.HotkeyEnabled && QuickHotkey.Normalize(_settings.Hotkey) == hk.Display)
                return OpResult.Fail("O atalho " + hk.Display + " já é usado para abrir o GamesHub. Escolha outro.");
            EnsureForm();
            if (_hotkey.Current != null && _hotkey.Current.Display == hk.Display)
                return OpResult.Success("Atalho da paleta rápida: " + hk.Display + ".");
            if (!_hotkey.Register(hk))
                return OpResult.Fail("O atalho " + hk.Display + " já está em uso por outro programa.");
            return OpResult.Success("Atalho da paleta rápida: " + hk.Display + ".");
        }

        /// <summary>Shows the palette (thread-safe). Works even when the hotkey is disabled.</summary>
        public void Show()
        {
            if (_disposed || _ui == null) return;
            if (!OnUiThread()) { _ui.Post(_ => Show(), null); return; }
            EnsureForm();
            if (_form.PageReady) _form.Post(ShowMessage());
            else _pendingShow = true;   // the page posts "ready" soon; we send "show" then
            _form.ShowPalette();
        }

        public void Dispose()
        {
            if (_disposed) return;
            if (_ui != null && !OnUiThread()) { _ui.Send(_ => Dispose(), null); return; }
            _disposed = true;
            _library.Changed -= OnLibraryChanged;
            _library.RunningChanged -= OnRunningChanged;
            try
            {
                _hotkey?.Dispose();
                _form?.CloseForGood();
            }
            catch (Exception ex) { Log.Warn("Quick-launch dispose failed", ex); }
            _hotkey = null;
            _form = null;
        }

        // ------------------------------------------------------------ internals

        private bool OnUiThread() => Thread.CurrentThread.ManagedThreadId == _uiThreadId;

        private void Toggle()
        {
            if (_form != null && _form.Visible && Form.ActiveForm == _form) _form.HidePalette();
            else Show();
        }

        private void EnsureForm()
        {
            if (_form != null) return;
            _form = new QuickLaunchForm();
            _form.PageMessage += OnPageMessage;
            _form.Hidden += () => _form?.Post(new { type = "reset", items = Items("") });
            _form.Warmup();
        }

        private object ShowMessage() => new
        {
            type = "show",
            items = Items(""),
            hotkey = _hotkey?.Current?.Display ?? "",
            reduceMotion = _settings.ReduceMotion,
        };

        private void OnLibraryChanged()
        {
            Interlocked.Exchange(ref _dirty, 1);
            if (_ui != null && !_disposed) _ui.Post(_ => { if (_form != null && _form.Visible) _form.Post(new { type = "changed" }); }, null);
        }

        private void OnRunningChanged(string id, bool running) => OnLibraryChanged();

        private List<QuickEntry> Index()
        {
            lock (_indexGate)
            {
                if (_index == null || Interlocked.Exchange(ref _dirty, 0) == 1)
                {
                    try { _index = QuickSearch.BuildIndex(_library.GetGames()); }
                    catch (Exception ex)
                    {
                        Log.Error("Quick-launch: failed to build index", ex);
                        if (_index == null) _index = new List<QuickEntry>();
                    }
                }
                return _index;
            }
        }

        private List<Dictionary<string, object>> Items(string query)
            => QuickDto.Items(QuickSearch.Search(Index(), query, DateTime.Now));

        private void OnPageMessage(IDictionary<string, object> msg)
        {
            string id = Json.Str(msg, "id");
            switch (Json.Str(msg, "type"))
            {
                case "ready":
                    if (_pendingShow || _form.Visible) { _pendingShow = false; _form.Post(ShowMessage()); }
                    else
                    {
                        _form.Post(new { type = "reset", items = Items("") });
                        _form.Prime();
                    }
                    break;
                case "query":
                    string q = Json.Str(msg, "q");
                    _form.Post(new { type = "results", seq = Json.Long(msg, "seq"), q, items = Items(q) });
                    break;
                case "launch":
                    Launch(id);
                    break;
                case "reveal":
                    Reveal(id);
                    break;
                case "hide":
                    _form.HidePalette();
                    break;
                case "log":
                    string text = "Quick-launch page: " + Json.Str(msg, "msg");
                    if (Json.Str(msg, "level") == "error") Log.Error(text); else Log.Warn(text);
                    break;
                default:
                    Log.Warn("Quick-launch: unknown page message " + Json.Str(msg, "type"));
                    break;
            }
        }

        private async void Launch(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            _form.Post(new { type = "launching", id });
            OpResult r;
            try { r = await Task.Run(() => _library.Launch(id)); }
            catch (Exception ex)
            {
                Log.Error("Quick-launch: launch failed for " + id, ex);
                r = OpResult.Fail("Não foi possível iniciar o jogo.");
            }
            if (_disposed) return;
            if (r == null) r = OpResult.Fail("Não foi possível iniciar o jogo.");
            if (r.Ok) _form.HidePalette();
            else _form.Post(new { type = "status", kind = "err", text = r.Message });
            try { Launched?.Invoke(id, r); }
            catch (Exception ex) { Log.Error("Quick-launch: Launched handler failed", ex); }
        }

        private void Reveal(string id)
        {
            string path = "";
            try { path = _library.RevealPath(id) ?? ""; }
            catch (Exception ex) { Log.Warn("Quick-launch: RevealPath failed for " + id, ex); }
            if (path.Length == 0 || (!Directory.Exists(path) && !File.Exists(path)))
            {
                _form.Post(new { type = "status", kind = "err", text = "Este jogo não tem pasta para abrir." });
                return;
            }
            try
            {
                string args = Directory.Exists(path) ? "\"" + path + "\"" : "/select,\"" + path + "\"";
                Process.Start(new ProcessStartInfo("explorer.exe", args) { UseShellExecute = true });
                _form.HidePalette();
            }
            catch (Exception ex)
            {
                Log.Warn("Quick-launch: could not open Explorer for " + path, ex);
                _form.Post(new { type = "status", kind = "err", text = "Não foi possível abrir a pasta." });
            }
        }
    }
}
