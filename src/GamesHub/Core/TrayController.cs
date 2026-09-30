using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace GamesHub
{
    internal enum TrayState { Normal, Active, Notification, Error, Disabled }

    /// <summary>Notification-area icon: state icons, transient flashes, recent-games menu. UI thread only.</summary>
    internal sealed class TrayController : IDisposable
    {
        private const int MaxTooltip = 63; // NotifyIcon.Text limit on .NET Framework
        private const int FlashMs = 2600;

        private readonly NotifyIcon _icon;
        private readonly ContextMenuStrip _menu;
        private readonly ToolStripMenuItem _recentMenu;
        private readonly Dictionary<TrayState, Icon> _icons = new Dictionary<TrayState, Icon>();
        private readonly Timer _flashTimer = new Timer { Interval = FlashMs };
        private readonly Func<IList<Game>> _recentGames;
        private TrayState _base = TrayState.Disabled;
        private string _runningName;

        public event Action OpenRequested;
        public event Action SettingsRequested;
        public event Action GamesFolderRequested;
        public event Action ExitRequested;
        public event Action<string> LaunchRequested;

        public TrayController(Func<IList<Game>> recentGames)
        {
            _recentGames = recentGames;
            LoadIcons();

            _menu = new ContextMenuStrip();
            var open = new ToolStripMenuItem("Abrir GamesHub", null, (s, e) => OpenRequested?.Invoke());
            open.Font = new Font(open.Font, FontStyle.Bold);
            _recentMenu = new ToolStripMenuItem("Jogados recentemente");
            _recentMenu.DropDownItems.Add(new ToolStripMenuItem("…") { Enabled = false });
            _menu.Items.Add(open);
            _menu.Items.Add(_recentMenu);
            _menu.Items.Add(new ToolStripMenuItem("Pasta de jogos", MenuImage("gamehub-folder.ico"), (s, e) => GamesFolderRequested?.Invoke()));
            _menu.Items.Add(new ToolStripMenuItem("Configurações", null, (s, e) => SettingsRequested?.Invoke()));
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(new ToolStripMenuItem("Sair", null, (s, e) => ExitRequested?.Invoke()));
            _menu.Opening += (s, e) => RebuildRecent();

            _icon = new NotifyIcon { ContextMenuStrip = _menu };
            _icon.DoubleClick += (s, e) => OpenRequested?.Invoke();
            _flashTimer.Tick += (s, e) =>
            {
                _flashTimer.Stop();
                ApplyIcon(_base);
            };
            ApplyIcon(_base);
            UpdateTooltip();
            _icon.Visible = true;
        }

        /// <summary>Resting state (Disabled while the library loads, Normal after, Error on failure).</summary>
        public void SetBaseState(TrayState state)
        {
            _base = _runningName != null && state == TrayState.Normal ? TrayState.Active : state;
            if (!_flashTimer.Enabled) ApplyIcon(_base);
        }

        /// <summary>Shows the "playing" state with the game's name; null returns to normal.</summary>
        public void SetRunning(string gameName)
        {
            _runningName = string.IsNullOrWhiteSpace(gameName) ? null : gameName;
            if (_base == TrayState.Normal || _base == TrayState.Active)
                SetBaseState(TrayState.Normal);
            UpdateTooltip();
        }

        /// <summary>Temporarily shows a state icon, then returns to the resting state.</summary>
        public void Flash(TrayState transient)
        {
            ApplyIcon(transient);
            _flashTimer.Stop();
            _flashTimer.Start();
        }

        public void ShowBalloon(string title, string text, ToolTipIcon kind = ToolTipIcon.Info)
            => _icon.ShowBalloonTip(4000, title, text, kind);

        private void RebuildRecent()
        {
            IList<Game> recent;
            try
            {
                recent = _recentGames();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException
                                       || ex is InvalidOperationException || ex is ArgumentException)
            {
                Log.Warn("Could not list recent games for the tray", ex);
                recent = new List<Game>();
            }

            foreach (ToolStripItem old in _recentMenu.DropDownItems) old.Dispose();
            _recentMenu.DropDownItems.Clear();
            if (recent.Count == 0)
            {
                _recentMenu.DropDownItems.Add(new ToolStripMenuItem("Nenhum jogo recente") { Enabled = false });
                return;
            }
            foreach (Game g in recent)
            {
                string id = g.Id;
                string label = g.Name.Replace("&", "&&");
                _recentMenu.DropDownItems.Add(new ToolStripMenuItem(label, null, (s, e) => LaunchRequested?.Invoke(id)));
            }
        }

        private void UpdateTooltip()
        {
            string text = _runningName == null ? AppInfo.Name : AppInfo.Name + " — jogando " + _runningName;
            _icon.Text = text.Length <= MaxTooltip ? text : text.Substring(0, MaxTooltip - 1) + "…";
        }

        private void ApplyIcon(TrayState state)
        {
            if (_icons.TryGetValue(state, out Icon ic) || _icons.TryGetValue(TrayState.Normal, out ic))
                _icon.Icon = ic;
        }

        private void LoadIcons()
        {
            var files = new Dictionary<TrayState, string>
            {
                [TrayState.Normal] = "gamehub-app.ico",
                [TrayState.Active] = "gamehub-tray-active.ico",
                [TrayState.Notification] = "gamehub-tray-notification.ico",
                [TrayState.Error] = "gamehub-tray-error.ico",
                [TrayState.Disabled] = "gamehub-tray-disabled.ico",
            };
            foreach (KeyValuePair<TrayState, string> kv in files)
            {
                string path = AppPaths.AppFile(kv.Value);
                if (!File.Exists(path)) continue;
                try
                {
                    _icons[kv.Key] = new Icon(path, SystemInformation.SmallIconSize);
                }
                catch (Exception ex) when (IsIconLoadError(ex))
                {
                    Log.Warn("Could not load tray icon " + kv.Value, ex);
                }
            }
            if (!_icons.ContainsKey(TrayState.Normal))
            {
                try
                {
                    _icons[TrayState.Normal] = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                }
                catch (Exception ex) when (IsIconLoadError(ex))
                {
                    Log.Warn("Falling back to the default application icon for the tray", ex);
                    _icons[TrayState.Normal] = SystemIcons.Application;
                }
            }
        }

        private static Image MenuImage(string file)
        {
            string path = AppPaths.AppFile(file);
            if (!File.Exists(path)) return null;
            try
            {
                using (var ic = new Icon(path, SystemInformation.SmallIconSize)) return ic.ToBitmap();
            }
            catch (Exception ex) when (IsIconLoadError(ex))
            {
                Log.Warn("Could not load menu icon " + file, ex);
                return null;
            }
        }

        /// <summary>Failures of System.Drawing.Icon loading: bad/missing file, access denied or a GDI+ error.</summary>
        private static bool IsIconLoadError(Exception ex)
            => ex is ArgumentException || ex is IOException || ex is UnauthorizedAccessException
               || ex is System.Runtime.InteropServices.ExternalException;

        public void Dispose()
        {
            _flashTimer.Stop();
            _flashTimer.Dispose();
            _icon.Visible = false; // removes the icon from the notification area immediately
            _icon.Dispose();
            _menu.Dispose();
            foreach (Icon ic in _icons.Values.Where(i => !ReferenceEquals(i, SystemIcons.Application)))
                ic.Dispose();
        }
    }
}
