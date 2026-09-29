// OWNER: CORE agent.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;

namespace GamesHub
{
    /// <summary>
    /// Frameless top-level window hosting the WebView2 UI. The web page draws the title bar; this class
    /// provides resize borders, maximize-to-work-area, fullscreen, pin, and bounds persistence.
    /// </summary>
    internal sealed partial class MainForm : Form
    {
        public static readonly Color Background = Color.FromArgb(11, 13, 22); // matches web --bg0 (#0b0d16)
        private static readonly Size DefaultLogicalSize = new Size(1240, 800);
        private static readonly Size MinLogicalSize = new Size(900, 600);
        private const int ResizeBorderLogical = 5;

        private readonly bool _debug;
        private readonly WebView2 _web;
        private bool _fullscreen;
        private Rectangle _preFullscreenBounds;
        private bool _preFullscreenMaximized;
        private bool _maximizeOnFirstShow;
        private FormWindowState _lastVisibleState = FormWindowState.Normal;
        private (bool max, bool fs, bool pin) _lastReportedState;

        /// <summary>Set by the controller when the app really exits; otherwise closing is delegated.</summary>
        public bool AllowClose;
        /// <summary>Decides what a close request does. Return true to let the form close.</summary>
        public Func<CloseReason, bool> CloseHandler;
        /// <summary>Raised on maximize/restore/fullscreen/pin changes.</summary>
        public event Action StateChanged;

        public MainForm(bool debug)
        {
            _debug = debug;
            Text = AppInfo.Name;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            BackColor = Background;
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.None;
            Icon = LoadAppIcon();

            _web = new WebView2
            {
                Dock = DockStyle.Fill,
                DefaultBackgroundColor = Background,
                AllowExternalDrop = true,
            };
            Controls.Add(_web);
        }

        public bool IsFullscreen => _fullscreen;
        public bool IsPinned => TopMost;
        public bool IsMaximized => WindowState == FormWindowState.Maximized;

        private int Dpi => IsHandleCreated ? CoreNative.DpiFor(Handle) : 96;
        private int Px(int logical) => (int)Math.Round(logical * Dpi / 96.0);

        private static Icon LoadAppIcon()
        {
            try
            {
                string file = AppPaths.AppFile("gamehub-app.ico");
                if (System.IO.File.Exists(file)) return new Icon(file);
                return Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch (Exception ex)
            {
                Log.Warn("Could not load window icon", ex);
                return null;
            }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                // Borderless, but keep min/max boxes + system menu so taskbar clicks, Win+Down,
                // Alt+Space and caption double-clicks (app-region: drag) behave like a normal window.
                cp.Style |= CoreNative.WS_MINIMIZEBOX | CoreNative.WS_MAXIMIZEBOX | CoreNative.WS_SYSMENU;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            UpdateFrame();
        }

        /// <summary>Creates the window handle (without showing it) and applies the saved placement.</summary>
        public void Prepare(WindowPlacementData placement)
        {
            IntPtr _ = Handle; // forces handle creation so the WebView can initialize while hidden
            var workAreas = new List<Rectangle> { Screen.PrimaryScreen.WorkingArea };
            workAreas.AddRange(Screen.AllScreens.Where(s => !s.Primary).Select(s => s.WorkingArea));
            Size min = new Size(Px(MinLogicalSize.Width), Px(MinLogicalSize.Height));
            Size def = new Size(Px(DefaultLogicalSize.Width), Px(DefaultLogicalSize.Height));
            Bounds = WindowPlacement.Clamp(placement.Bounds, workAreas, min, def);
            _maximizeOnFirstShow = placement.Maximized;
        }

        /// <summary>Current placement for window.json (normal bounds even while maximized/fullscreen).</summary>
        public void CapturePlacement(WindowPlacementData into)
        {
            if (_fullscreen)
            {
                into.Bounds = _preFullscreenBounds;
                into.Maximized = _preFullscreenMaximized;
                return;
            }
            into.Bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            into.Maximized = WindowState == FormWindowState.Maximized
                             || (WindowState == FormWindowState.Minimized && _lastVisibleState == FormWindowState.Maximized)
                             || (!Visible && _maximizeOnFirstShow);
        }

        public void ShowAndActivate()
        {
            if (!Visible)
            {
                Show();
                if (_maximizeOnFirstShow)
                {
                    _maximizeOnFirstShow = false;
                    WindowState = FormWindowState.Maximized;
                }
            }
            if (WindowState == FormWindowState.Minimized)
                WindowState = _lastVisibleState == FormWindowState.Maximized ? FormWindowState.Maximized : FormWindowState.Normal;
            Activate();
            CoreNative.SetForegroundWindow(Handle);
            _web.Focus();
        }

        public void MinimizeWindow() => WindowState = FormWindowState.Minimized;

        public void SetMaximized(bool maximize)
        {
            if (_fullscreen) SetFullscreen(false);
            WindowState = maximize ? FormWindowState.Maximized : FormWindowState.Normal;
        }

        /// <summary>Borderless window covering the whole monitor (taskbar included).</summary>
        public void SetFullscreen(bool on)
        {
            if (on == _fullscreen) return;
            if (on)
            {
                _preFullscreenMaximized = WindowState == FormWindowState.Maximized;
                if (WindowState != FormWindowState.Normal) WindowState = FormWindowState.Normal;
                _preFullscreenBounds = Bounds;
                _fullscreen = true;
                UpdateFrame();
                Bounds = Screen.FromHandle(Handle).Bounds;
            }
            else
            {
                _fullscreen = false;
                Bounds = _preFullscreenBounds;
                if (_preFullscreenMaximized) WindowState = FormWindowState.Maximized;
                UpdateFrame();
            }
            ReportState();
        }

        public void SetPinned(bool on)
        {
            if (TopMost == on) return;
            TopMost = on;
            ReportState();
        }

        public Dictionary<string, object> StateDto() => new Dictionary<string, object>
        {
            ["maximized"] = IsMaximized,
            ["fullscreen"] = _fullscreen,
            ["pinned"] = IsPinned,
        };

        /// <summary>True when this window is the foreground window and not minimized.</summary>
        public bool IsForeground => Visible && WindowState != FormWindowState.Minimized
                                    && CoreNative.GetForegroundWindow() == Handle;

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState != FormWindowState.Minimized) _lastVisibleState = WindowState;
            UpdateFrame();
            ReportState();
        }

        private void ReportState()
        {
            var now = (IsMaximized, _fullscreen, IsPinned);
            if (now == _lastReportedState) return;
            _lastReportedState = now;
            StateChanged?.Invoke();
        }

        /// <summary>Resize band + DWM corners depend on the window state.</summary>
        private void UpdateFrame()
        {
            if (!IsHandleCreated) return;
            bool normal = WindowState == FormWindowState.Normal && !_fullscreen;
            int border = normal ? Px(ResizeBorderLogical) : 0;
            if (Padding.All != border) Padding = new Padding(border);
            CoreNative.SetCornerPreference(Handle, normal);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!AllowClose && CloseHandler != null && !CloseHandler(e.CloseReason))
            {
                e.Cancel = true;
                return;
            }
            base.OnFormClosing(e);
        }
    }
}
