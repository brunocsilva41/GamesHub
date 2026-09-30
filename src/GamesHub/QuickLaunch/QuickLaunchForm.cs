// Created once (hidden) and kept alive so showing it is just position + Show + a web message.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace GamesHub
{
    internal sealed class QuickLaunchForm : Form
    {
        public const string StartUrl = "https://" + WebViewEnv.AppHost + "/quick/index.html";
        private const int LogicalWidth = 640, LogicalHeight = 420, LogicalRadius = 14;
        private static readonly Color Background = Color.FromArgb(0x10, 0x13, 0x1C);

        private readonly WebView2 _web;
        private readonly bool _debug;
        private Rectangle _target;
        private bool _dwmRounded;
        private bool _allowClose;
        private int _dpi = 96;
        private bool _primed, _priming;

        /// <summary>True once the page has posted {type:"ready"}.</summary>
        public bool PageReady { get; private set; }
        /// <summary>Raised on the UI thread for every message the page posts (already parsed).</summary>
        public event Action<IDictionary<string, object>> PageMessage;
        /// <summary>Raised when the palette hides (Esc, focus loss, launch...).</summary>
        public event Action Hidden;

        public QuickLaunchForm()
        {
            _debug = Environment.GetCommandLineArgs().Any(a => string.Equals(a, "--debug", StringComparison.OrdinalIgnoreCase));
            Text = "GamesHub — Início rápido";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Background;
            Size = new Size(LogicalWidth, LogicalHeight);
            _web = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Background };
            Controls.Add(_web);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= QuickNative.WS_EX_TOOLWINDOW | QuickNative.WS_EX_TOPMOST; // no taskbar/Alt+Tab entry
                cp.ClassStyle |= QuickNative.CS_DROPSHADOW;
                return cp;
            }
        }

        /// <summary>Creates the window handles (hidden) and starts loading the page. Call on the UI thread.</summary>
        public async void Warmup()
        {
            try
            {
                // Reading Handle creates the form handle without showing it; WebView2 initializes only once
                // its control has a handle too. Both getters throw on failure, so the guard never returns early.
                if (Handle == IntPtr.Zero || _web.Handle == IntPtr.Zero) return;
                ApplyCorners();
                CoreWebView2Environment env = await WebViewEnv.GetAsync();
                await _web.EnsureCoreWebView2Async(env);
                CoreWebView2 core = _web.CoreWebView2;
                CoreWebView2Settings s = core.Settings;
                s.AreDevToolsEnabled = _debug;
                s.AreDefaultContextMenusEnabled = _debug;
                s.AreBrowserAcceleratorKeysEnabled = _debug;
                s.IsZoomControlEnabled = false;
                s.IsPinchZoomEnabled = false;
                s.IsSwipeNavigationEnabled = false;
                s.IsStatusBarEnabled = false;
                s.IsGeneralAutofillEnabled = false;
                s.IsPasswordAutosaveEnabled = false;
                s.AreHostObjectsAllowed = false;
                s.IsWebMessageEnabled = true;
                WebViewEnv.MapHosts(core);
                core.NavigationStarting += (o, e) =>
                {
                    if (IsAppUri(e.Uri)) return;
                    e.Cancel = true;
                    Log.Warn("Quick-launch: blocked navigation to " + Truncate(e.Uri));
                };
                // Same lock-down as the main window: no frames outside the palette, no popups, no external
                // protocol handlers, no downloads, no permissions.
                core.FrameNavigationStarting += (o, e) =>
                {
                    if (IsAppUri(e.Uri) || e.Uri == "about:blank") return;
                    e.Cancel = true;
                    Log.Warn("Quick-launch: blocked frame navigation to " + Truncate(e.Uri));
                };
                core.NewWindowRequested += (o, e) =>
                {
                    e.Handled = true;
                    Log.Warn("Quick-launch: blocked new window " + Truncate(e.Uri));
                };
                core.LaunchingExternalUriScheme += (o, e) =>
                {
                    e.Cancel = true;
                    Log.Warn("Quick-launch: blocked external scheme " + Truncate(e.Uri));
                };
                core.PermissionRequested += (o, e) => e.State = CoreWebView2PermissionState.Deny;
                core.DownloadStarting += (o, e) =>
                {
                    e.Cancel = true;
                    Log.Warn("Quick-launch: blocked download " + Truncate(e.DownloadOperation?.Uri));
                };
                core.WebMessageReceived += OnWebMessage;
                core.ProcessFailed += (o, e) =>
                {
                    Log.Error("Quick-launch WebView2 process failed: " + e.ProcessFailedKind);
                    PageReady = false;
                    if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessExited
                        || e.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
                        core.Navigate(StartUrl);
                };
                core.NavigationCompleted += (o, e) =>
                {
                    if (!e.IsSuccess) Log.Warn("Quick-launch navigation failed: " + e.WebErrorStatus);
                };
                core.Navigate(StartUrl);
            }
            // Resilience boundary: async void entry point; an escaping exception would crash the UI thread.
            catch (Exception ex)
            {
                Log.Error("Quick-launch WebView2 initialization failed", ex);
            }
        }

        public static bool IsAppUri(string uri)
            => uri != null && uri.StartsWith("https://" + WebViewEnv.AppHost + "/quick/", StringComparison.OrdinalIgnoreCase);

        private static string Truncate(string s) => s == null ? "" : s.Length > 120 ? s.Substring(0, 120) + "…" : s;

        private void OnWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            if (!IsAppUri(e.Source)) return;
            IDictionary<string, object> msg;
            try { msg = Json.DeserializeMessage(e.WebMessageAsJson) as IDictionary<string, object>; }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
            { Log.Warn("Quick-launch: invalid page message", ex); return; }
            if (msg == null) return;
            if (Json.Str(msg, "type") == "ready") PageReady = true;
            try { PageMessage?.Invoke(msg); }
            // Resilience boundary: WebView2 event handler dispatching to arbitrary subscribers.
            catch (Exception ex) { Log.Error("Quick-launch: message handler failed (" + Json.Str(msg, "type") + ")", ex); }
        }

        /// <summary>Sends an object to the page as JSON (ignored until the page is ready).</summary>
        public void Post(object message)
        {
            if (!PageReady || _web.CoreWebView2 == null) return;
            try { _web.CoreWebView2.PostWebMessageAsJson(Json.Serialize(message)); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException || ex is System.Runtime.InteropServices.COMException)
            { Log.Warn("Quick-launch: PostWebMessageAsJson failed", ex); }
        }

        // ------------------------------------------------------------ show / hide

        protected override bool ShowWithoutActivation => _priming;

        /// <summary>The first time a WebView becomes visible its compositor is set up (~200-400 ms). Do that
        /// once in advance, off-screen and without activation, so the first real show is instant.</summary>
        public void Prime()
        {
            if (_primed || Visible) return;
            _primed = _priming = true;
            Rectangle vs = SystemInformation.VirtualScreen;
            QuickNative.SetWindowPos(Handle, IntPtr.Zero, vs.Left - Width - 2000, vs.Top - Height - 2000, Width, Height,
                QuickNative.SWP_NOZORDER | QuickNative.SWP_NOACTIVATE);
            Show();
            var timer = new Timer { Interval = 600 };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                timer.Dispose();
                if (!_priming) return;   // a real show happened meanwhile
                _priming = false;
                Hide();
            };
            timer.Start();
        }

        public void ShowPalette()
        {
            _priming = false;
            PlaceOnCursorMonitor();
            if (!Visible) Show();
            Activate();
            QuickNative.SetForegroundWindow(Handle);   // allowed: we are handling our own WM_HOTKEY
            _web.Focus();
        }

        public void HidePalette()
        {
            if (!Visible || _priming) return;
            Hide();
            Hidden?.Invoke();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            HidePalette();   // focus loss hides the palette (ignored while priming, see HidePalette)
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_allowClose && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;   // Alt+F4 just hides
                HidePalette();
                return;
            }
            base.OnFormClosing(e);
        }

        public void CloseForGood()
        {
            _allowClose = true;
            Close();
            Dispose();
        }

        // ------------------------------------------------------------ geometry

        /// <summary>Centers the palette on the monitor under the cursor, sized for that monitor's DPI.</summary>
        private void PlaceOnCursorMonitor()
        {
            Point cursor = Cursor.Position;
            Rectangle wa = Screen.FromPoint(cursor).WorkingArea;
            int dpi = QuickNative.DpiAt(cursor.X, cursor.Y);
            _dpi = dpi;
            int w = Math.Min(Scale(LogicalWidth, dpi), wa.Width - 16);
            int h = Math.Min(Scale(LogicalHeight, dpi), wa.Height - 16);
            int x = wa.Left + (wa.Width - w) / 2;
            int y = wa.Top + (int)((wa.Height - h) * 0.38);   // slightly above center, Spotlight-style
            _target = new Rectangle(x, y, w, h);
            ApplyTarget();
        }

        private static int Scale(int logical, int dpi) => (int)Math.Round((double)logical * dpi / 96.0);

        private void ApplyTarget()
        {
            if (_target.IsEmpty) return;
            QuickNative.SetWindowPos(Handle, IntPtr.Zero, _target.X, _target.Y, _target.Width, _target.Height,
                QuickNative.SWP_NOZORDER | QuickNative.SWP_NOACTIVATE);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == QuickNative.WM_DPICHANGED)
            {
                // We size the window ourselves for the destination monitor; ignore the suggested rect.
                ApplyTarget();
                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (IsHandleCreated && !_dwmRounded) ApplyRegion();
        }

        /// <summary>Windows 11: DWM rounded corners. Windows 10: clip with a rounded region.</summary>
        private void ApplyCorners()
        {
            int pref = QuickNative.DWMWCP_ROUND;
            try { _dwmRounded = QuickNative.DwmSetWindowAttribute(Handle, QuickNative.DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int)) == 0; }
            catch (DllNotFoundException ex) { Log.Warn("dwmapi unavailable", ex); _dwmRounded = false; }
            // On Windows 10 the call fails (unknown attribute) → region fallback.
            if (!_dwmRounded) ApplyRegion();
        }

        private void ApplyRegion()
        {
            int r = Scale(LogicalRadius, _dpi) * 2;
            IntPtr rgn = QuickNative.CreateRoundRectRgn(0, 0, Width + 1, Height + 1, r, r);
            if (rgn == IntPtr.Zero) return;
            Region old = Region;
            Region = Region.FromHrgn(rgn);
            QuickNative.DeleteObject(rgn);
            old?.Dispose();
        }
    }
}
