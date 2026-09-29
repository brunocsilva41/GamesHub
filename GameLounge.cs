using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Web.Script.Serialization;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace GamesLounge
{
    public class Game
    {
        public string Name = "";
        public string FilePath = "";
        public string Ext = "";
        public string Target = "";
        public string Platform = "PC";
        public string AppId = "";
        public string Cover = "";
        public string Icon = "";
        public string SearchAppId = "";
        public string LastPlayed = "";
        public string LnkIcon = "";
    }

    public class GameDto
    {
        public string name { get; set; }
        public string path { get; set; }
        public string platform { get; set; }
        public string ext { get; set; }
        public string cover { get; set; }
        public string icon { get; set; }
        public string last { get; set; }
        public string appid { get; set; }
    }

    public class ToastDto
    {
        public string text { get; set; }
        public string kind { get; set; }
    }

    public static class Ui
    {
        public static Color Text = Color.FromArgb(238, 240, 248);
        public static Color Dim = Color.FromArgb(150, 158, 186);
    }

    internal class AddGameDialog : Form
    {
        private string _gamesDir;
        private TextBox _nameTxt, _pathTxt, _appTxt;
        public string CreatedMessage = "";

        public AddGameDialog(string gamesDir)
        {
            _gamesDir = gamesDir;
            Text = "Adicionar jogo ao hub";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(490, 256);
            BackColor = Color.FromArgb(16, 18, 30);
            ForeColor = Ui.Text;
            Font = new Font("Segoe UI", 9f);

            int y = 16;
            _nameTxt = MakeField("Nome (opcional)", y); y += 46;
            _pathTxt = MakeField("Arquivo do jogo (.exe, .lnk, .url)", y); y += 46;

            Button browse = MakeButton("Procurar...", new Point(342, y - 21));
            browse.Click += (s, e) =>
            {
                using (OpenFileDialog d = new OpenFileDialog())
                {
                    d.Title = "Escolha o jogo";
                    d.Filter = "Executaveis, atalhos e URL (*.exe;*.lnk;*.url)|*.exe;*.lnk;*.url|Exe (*.exe)|*.exe|Atalho (*.lnk)|*.lnk|URL (*.url)|*.url";
                    if (d.ShowDialog(this) == DialogResult.OK) _pathTxt.Text = d.FileName;
                }
            };

            Label sep = new Label();
            sep.Text = "-- ou adicione da Steam --";
            sep.ForeColor = Ui.Dim;
            sep.Location = new Point(14, y + 2);
            Controls.Add(sep);
            y += 26;

            _appTxt = MakeField("Steam App ID (ex.: 730)", y); y += 46;

            Button ok = MakeButton("Adicionar", new Point(292, y));
            ok.Click += (s, e) => Submit();
            Button cancel = MakeButton("Cancelar", new Point(390, y));
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; };

            AcceptButton = ok;
            CancelButton = cancel;
        }

        private TextBox MakeField(string label, int y)
        {
            Label l = new Label();
            l.Text = label;
            l.ForeColor = Ui.Dim;
            l.Location = new Point(14, y);
            l.AutoSize = true;
            TextBox t = new TextBox();
            t.Location = new Point(14, y + 20);
            t.Size = new Size(304, 26);
            t.BackColor = Color.FromArgb(24, 26, 40);
            t.ForeColor = Ui.Text;
            t.BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(l);
            Controls.Add(t);
            return t;
        }

        private Button MakeButton(string text, Point location)
        {
            Button b = new Button();
            b.Text = text;
            b.Location = location;
            b.Size = new Size(92, 30);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = Color.FromArgb(90, 100, 130);
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(60, 52, 96);
            b.BackColor = Color.FromArgb(24, 26, 40);
            b.ForeColor = Ui.Text;
            Controls.Add(b);
            return b;
        }

        private static string Sanitize(string s)
        {
            s = s.Replace("\"", "").Replace(":", "").Replace("*", "").Replace("?", "").Replace("<", "").Replace(">", "").Replace("|", "").Replace("/", "-").Replace("\\", "-");
            s = s.Trim().TrimEnd('.', ' ');
            if (s.Length == 0) s = "Novo jogo";
            return s;
        }

        private string EnsureUnique(string baseName, string ext)
        {
            string f = Path.Combine(_gamesDir, baseName + ext);
            int i = 2;
            while (File.Exists(f))
            {
                f = Path.Combine(_gamesDir, baseName + " (" + i + ")" + ext);
                i++;
            }
            return Path.GetFileName(f);
        }

        private string FetchSteamName(string appId)
        {
            try
            {
                using (WebClient wc = new WebClient())
                {
                    wc.Headers["User-Agent"] = "Mozilla/5.0";
                    string json = wc.DownloadString("https://store.steampowered.com/api/appdetails?appids=" + appId + "&l=portuguese&cc=br");
                    Match m = Regex.Match(json, "\"name\"\\s*:\\s*\"([^\"]+)\"");
                    if (m.Success) return m.Groups[1].Value;
                }
            }
            catch { }
            return "";
        }

        private void Submit()
        {
            string name = _nameTxt.Text.Trim();
            string path = _pathTxt.Text.Trim();
            string app = Regex.Match(_appTxt.Text.Trim(), "\\d+").Value;

            if (app.Length > 0)
            {
                if (name.Length == 0) name = FetchSteamName(app);
                if (name.Length == 0) name = "Steam " + app;
                name = Sanitize(name);
                string urlFile = Path.Combine(_gamesDir, EnsureUnique(name, ".url"));
                File.WriteAllText(urlFile, "[InternetShortcut]\r\nURL=steam://rungameid/" + app + "\r\n");
                CreatedMessage = name + " adicionado ao hub.";
                DialogResult = DialogResult.OK;
                return;
            }

            if (path.Length == 0 || !File.Exists(path))
            {
                MessageBox.Show(this, "Escolha um arquivo de jogo valido (ou informe um App ID da Steam).", "Adicionar jogo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string baseName = name.Length > 0 ? Sanitize(name) : Sanitize(Path.GetFileNameWithoutExtension(path));

            string full = Path.GetFullPath(path);
            string gamesFull = Path.GetFullPath(_gamesDir);
            if (full.StartsWith(gamesFull, StringComparison.OrdinalIgnoreCase))
            {
                CreatedMessage = "Este jogo ja esta na pasta do hub.";
                DialogResult = DialogResult.OK;
                return;
            }

            string ext = Path.GetExtension(full).ToLowerInvariant();
            string targetFile = Path.Combine(_gamesDir, EnsureUnique(baseName, ext));

            if (ext == ".url")
            {
                File.WriteAllText(targetFile, File.ReadAllText(full));
            }
            else
            {
                CreateLink(targetFile, full);
            }
            CreatedMessage = baseName + " adicionado ao hub.";
            DialogResult = DialogResult.OK;
        }

        private void CreateLink(string targetFile, string sourceFull)
        {
            Type wsh = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(wsh);
            object sc = wsh.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { targetFile });
            Type scType = sc.GetType();

            if (Path.GetExtension(sourceFull).ToLowerInvariant() == ".lnk")
            {
                object src = wsh.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { sourceFull });
                Type st = src.GetType();
                string tgt = (string)st.InvokeMember("TargetPath", BindingFlags.GetProperty, null, src, null);
                string arg = (string)st.InvokeMember("Arguments", BindingFlags.GetProperty, null, src, null);
                string ico = (string)st.InvokeMember("IconLocation", BindingFlags.GetProperty, null, src, null);
                string wd = (string)st.InvokeMember("WorkingDirectory", BindingFlags.GetProperty, null, src, null);
                scType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { tgt });
                scType.InvokeMember("Arguments", BindingFlags.SetProperty, null, sc, new object[] { arg });
                scType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { wd });
                if (!string.IsNullOrEmpty(ico)) scType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc, new object[] { ico });
            }
            else
            {
                scType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { sourceFull });
                scType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { Path.GetDirectoryName(sourceFull) });
            }
            scType.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
        }
    }

    public class LobbyForm : Form
    {
        private string _gamesDir;
        private string _hubDir;
        private string _appDir;
        private string _coverDir;
        private string _trashDir;
        private WebView2 _web;
        private List<Game> _games = new List<Game>();
        private readonly object _gamesLock = new object();
        private FileSystemWatcher _watcher;
        private Timer _debounce;
        private JavaScriptSerializer _json = new JavaScriptSerializer();
        private HttpListener _listener;
        private int _port = -1;
        private bool _topMost = false;
        private List<ToastDto> _toasts = new List<ToastDto>();
        private NotifyIcon _tray;
        private bool _trayHintShown = false;
        private enum TrayState { Normal, Active, Notification, Error, Disabled }
        private Dictionary<int, Icon> _trayIcons = new Dictionary<int, Icon>();
        private Timer _trayFlash;
        private TrayState _trayBase = TrayState.Normal;
        private bool _reallyExit = false;
        private string _iconDir;
        private HashSet<string> _pendingCover = new HashSet<string>();
        private HashSet<string> _pendingIcon = new HashSet<string>();
        private HashSet<string> _pendingSearch = new HashSet<string>();
        private Dictionary<string, int> _searchCache = new Dictionary<string, int>();
        private Dictionary<string, string> _playLog = new Dictionary<string, string>();

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int WCA_ROUNDED = 2;

        public LobbyForm()
        {
            _gamesDir = "";
            try
            {
                string cf = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "config.json");
                if (File.Exists(cf))
                {
                    Dictionary<string, object> o = _json.Deserialize<Dictionary<string, object>>(File.ReadAllText(cf));
                    if (o != null && o.ContainsKey("gamesDir"))
                    {
                        string v = Convert.ToString(o["gamesDir"]);
                        if (v.Length > 0 && Directory.Exists(v)) _gamesDir = v;
                    }
                }
            }
            catch { }
            if (_gamesDir.Length == 0) _gamesDir = "C:\\Users\\Bruno Silva\\Desktop\\jogos";
            try { _appDir = Path.GetDirectoryName(Application.ExecutablePath); } catch { _appDir = ""; }
            if (_appDir.Length == 0) _appDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            _hubDir = Path.Combine(_gamesDir, "_hub");
            _coverDir = Path.Combine(_hubDir, "covers");
            _trashDir = Path.Combine(_hubDir, "_removidos");

            _iconDir = Path.Combine(_hubDir, "icons");
            try { Directory.CreateDirectory(_coverDir); } catch { }
            try { Directory.CreateDirectory(_trashDir); } catch { }
            try { Directory.CreateDirectory(_iconDir); } catch { }

            try
            {
                string scFile = Path.Combine(_hubDir, "searchcache.json");
                if (File.Exists(scFile))
                {
                    Dictionary<string, int> c = _json.Deserialize<Dictionary<string, int>>(File.ReadAllText(scFile));
                    if (c != null) _searchCache = c;
                }
            }
            catch { }

            try
            {
                string plFile = Path.Combine(_hubDir, "playlog.json");
                if (File.Exists(plFile))
                {
                    Dictionary<string, string> c = _json.Deserialize<Dictionary<string, string>>(File.ReadAllText(plFile));
                    if (c != null) _playLog = c;
                }
            }
            catch { }

            Text = "GamesHub";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1180, 760);
            BackColor = Color.FromArgb(11, 13, 22);
            try { Icon = new System.Drawing.Icon(AppFile("gamehub-app.ico")); } catch { }
            DoubleBuffered = true;

            _web = new WebView2();
            _web.Dock = DockStyle.Fill;
            _web.DefaultBackgroundColor = Color.FromArgb(11, 13, 22);
            Controls.Add(_web);

            Load += OnLoad;
            SetupTray();
            FormClosing += (s, e) =>
            {
                if (!_reallyExit)
                {
                    e.Cancel = true;
                    MinimizeToTray(true);
                    return;
                }
                try { if (_tray != null) { _tray.Visible = false; _tray.Dispose(); } } catch { }
                try { if (_listener != null) _listener.Stop(); } catch { }
                try { if (_watcher != null) _watcher.Dispose(); } catch { }
                try { if (_debounce != null) _debounce.Dispose(); } catch { }
                try { _web.Dispose(); } catch { }
            };
        }

        private void SetupTray()
        {
            try
            {
                _tray = new NotifyIcon();
                _tray.Text = "GamesHub";
                _tray.Visible = true;
                LoadTrayIcons();
                SetTray(TrayState.Normal);
                _trayFlash = new Timer();
                _trayFlash.Interval = 2600;
                _trayFlash.Tick += (s, e) =>
                {
                    try { _trayFlash.Stop(); SetTray(_trayBase); } catch { }
                };
                ContextMenuStrip menu = new ContextMenuStrip();
                ToolStripMenuItem header = new ToolStripMenuItem("GameHub");
                header.Enabled = false;
                menu.Items.Add(header);
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add("Abrir GamesHub", null, (s, e) => RestoreFromTray());
                menu.Items.Add("Pasta de jogos", IconImage("gamehub-folder.ico"), (s, e) => OpenFolder(_gamesDir));
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add("Sair", null, (s, e) =>
                {
                    _reallyExit = true;
                    Close();
                });
                _tray.ContextMenuStrip = menu;
                _tray.DoubleClick += (s, e) => RestoreFromTray();
            }
            catch { }
        }

        private string AppFile(string name)
        {
            try
            {
                if (_appDir.Length > 0 && File.Exists(Path.Combine(_appDir, name)))
                    return Path.Combine(_appDir, name);
            }
            catch { }
            return Path.Combine(_hubDir, name);
        }

        private Image IconImage(string name)
        {
            try
            {
                using (Icon ic = new Icon(AppFile(name), 16, 16))
                    return ic.ToBitmap();
            }
            catch { return null; }
        }

        private void LoadTrayIcons()
        {
            AddTrayIcon(TrayState.Normal, "gamehub-app.ico");
            AddTrayIcon(TrayState.Active, "gamehub-tray-active.ico");
            AddTrayIcon(TrayState.Notification, "gamehub-tray-notification.ico");
            AddTrayIcon(TrayState.Error, "gamehub-tray-error.ico");
            AddTrayIcon(TrayState.Disabled, "gamehub-tray-disabled.ico");
        }

        private void AddTrayIcon(TrayState st, string file)
        {
            try { _trayIcons[(int)st] = new Icon(AppFile(file)); } catch { }
        }

        private void SetTray(TrayState st)
        {
            if (_tray == null) return;
            try
            {
                Icon ic;
                if (_trayIcons.TryGetValue((int)st, out ic) && ic != null)
                    _tray.Icon = ic;
            }
            catch { }
        }

        private void FlashTray(TrayState transient, TrayState baseState)
        {
            if (_tray == null) return;
            try
            {
                SetTray(transient);
                _trayBase = baseState;
                _trayFlash.Stop();
                _trayFlash.Start();
            }
            catch { }
        }

        private void MinimizeToTray(bool hint)
        {
            if (_tray == null) { WindowState = FormWindowState.Minimized; return; }
            if (hint && !_trayHintShown)
            {
                _trayHintShown = true;
                try
                {
                    _tray.ShowBalloonTip(4000, "GamesHub", "Rodando em segundo plano.\nClique no icone da bandeja para abrir.", ToolTipIcon.Info);
                }
                catch { }
            }
            Hide();
        }

        private void RestoreFromTray()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
            try { _trayBase = TrayState.Normal; _trayFlash.Stop(); SetTray(TrayState.Normal); } catch { }
            try
            {
                int corner = WCA_ROUNDED;
                DwmSetWindowAttribute(this.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
            }
            catch { }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            try
            {
                int corner = WCA_ROUNDED;
                DwmSetWindowAttribute(this.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
            }
            catch { }
        }

        private void WriteDebug(string s)
        {
            try
            {
                File.AppendAllText(Path.Combine(_hubDir, "debug.log"),
                    DateTime.Now.ToString("HH:mm:ss.fff") + " " + s + "\r\n");
            }
            catch { }
        }

        private void AddToast(string text, bool err)
        {
            lock (_gamesLock)
            {
                _toasts.Add(new ToastDto { text = text, kind = err ? "err" : "ok" });
                if (_toasts.Count > 5) _toasts.RemoveAt(0);
            }
            FlashTray(err ? TrayState.Error : TrayState.Notification, _trayBase);
        }

        private async void OnLoad(object sender, EventArgs e)
        {
            WriteDebug("OnLoad iniciado");
            SetupWatcher();
            int port = StartServer();
            if (port < 0)
            {
                MessageBox.Show("Nao foi possivel iniciar o servidor local.", "GamesHub", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            WriteDebug("Servidor na porta " + port);
            try
            {
                string dataFolder = Path.Combine(_hubDir, ".wvdata");
                CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, dataFolder, null);
                await _web.EnsureCoreWebView2Async(env);
                WriteDebug("CoreWebView2 pronto");
                _web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                _web.CoreWebView2.Settings.IsStatusBarEnabled = false;
                _web.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = true;
                _web.CoreWebView2.Settings.IsZoomControlEnabled = false;
                _web.CoreWebView2.Settings.IsPinchZoomEnabled = false;
                _web.CoreWebView2.NavigationCompleted += (s2, e2) =>
                    WriteDebug("NavCompleted isSuccess=" + e2.IsSuccess + " code=" + e2.WebErrorStatus);
                _web.Source = new Uri("http://127.0.0.1:" + port + "/");
                ScanGames();
            }
            catch (Exception ex)
            {
                WriteDebug("FALHA OnLoad: " + ex);
                MessageBox.Show("Falha ao iniciar o WebView2 (Edge Runtime).\n\n" + ex.Message, "GamesHub",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /* ================= local HTTP server ================= */
        private int StartServer()
        {
            for (int p = 8456; p <= 8462; p++)
            {
                try
                {
                    HttpListener l = new HttpListener();
                    l.Prefixes.Add("http://127.0.0.1:" + p + "/");
                    l.Start();
                    _listener = l;
                    _port = p;
                    System.Threading.Thread t = new System.Threading.Thread(ListenLoop);
                    t.IsBackground = true;
                    t.Start();
                    return p;
                }
                catch { }
            }
            _listener = null;
            return -1;
        }

        private void ListenLoop()
        {
            while (_listener != null && _listener.IsListening)
            {
                try
                {
                    HttpListenerContext ctx = _listener.GetContext();
                    HandleRequest(ctx);
                }
                catch
                {
                    System.Threading.Thread.Sleep(200);
                }
            }
        }

        private bool IsSameOrigin(HttpListenerRequest req)
        {
            string o = req.Headers["Origin"];
            if (string.IsNullOrEmpty(o)) return true;
            return o.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase)
                || o.StartsWith("https://127.0.0.1:", StringComparison.OrdinalIgnoreCase);
        }

        private void HandleRequest(HttpListenerContext ctx)
        {
            try
            {
                HttpListenerResponse r = ctx.Response;
                bool sameOrigin = IsSameOrigin(ctx.Request);
                r.Headers["Access-Control-Allow-Origin"] = sameOrigin && _port > 0 ? "http://127.0.0.1:" + _port : "";

                if (ctx.Request.HttpMethod == "OPTIONS")
                {
                    if (!sameOrigin)
                    {
                        r.StatusCode = 403;
                        r.Close();
                        return;
                    }
                    r.StatusCode = 200;
                    r.Headers["Access-Control-Allow-Methods"] = "POST, GET, OPTIONS";
                    r.Headers["Access-Control-Allow-Headers"] = "Content-Type";
                    r.Close();
                    return;
                }

                string path = ctx.Request.Url.AbsolutePath;

                if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
                {
                    if (!sameOrigin)
                    {
                        r.StatusCode = 403;
                        r.Close();
                        return;
                    }
                    if (path.Equals("/api/state", StringComparison.OrdinalIgnoreCase) || path.Equals("/api/state/", StringComparison.OrdinalIgnoreCase))
                    {
                        Write(r, GetStateJson(), "application/json; charset=utf-8");
                    }
                    else if (path.Equals("/api/cmd", StringComparison.OrdinalIgnoreCase))
                    {
                        string body = new StreamReader(ctx.Request.InputStream, Encoding.UTF8).ReadToEnd();
                        DispatchCommand(body);
                        Write(r, "{\"ok\":true}", "application/json; charset=utf-8");
                    }
                    else
                    {
                        r.StatusCode = 404;
                        r.Close();
                    }
                    return;
                }

                string rel = path.TrimStart('/').Replace('/', '\\');
                if (rel.Length == 0) rel = "index.html";
                string full = Path.GetFullPath(Path.Combine(Path.Combine(_appDir, "web"), rel));
                if (!File.Exists(full)) full = Path.GetFullPath(Path.Combine(_hubDir, rel));
                string webRoot = Path.GetFullPath(Path.Combine(_appDir, "web"));
                string coverRoot = Path.GetFullPath(Path.Combine(_hubDir, "covers"));
                string iconRoot = Path.GetFullPath(Path.Combine(_hubDir, "icons"));
                bool okRoot =
                    full.StartsWith(webRoot, StringComparison.OrdinalIgnoreCase) ||
                    full.StartsWith(coverRoot, StringComparison.OrdinalIgnoreCase) ||
                    full.StartsWith(iconRoot, StringComparison.OrdinalIgnoreCase);
                if (!okRoot || !File.Exists(full))
                {
                    r.StatusCode = 404;
                    r.Close();
                    return;
                }
                r.Headers["Cache-Control"] = "no-store";
                Write(r, File.ReadAllBytes(full), Mime(full));
            }
            catch (Exception ex)
            {
                WriteDebug("Http: " + ex.Message);
                try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { }
            }
        }

        private void Write(HttpListenerResponse r, string body, string contentType)
        {
            Write(r, Encoding.UTF8.GetBytes(body), contentType);
        }

        private void Write(HttpListenerResponse r, byte[] bytes, string contentType)
        {
            try
            {
                r.ContentType = contentType;
                r.ContentLength64 = bytes.Length;
                r.OutputStream.Write(bytes, 0, bytes.Length);
                r.Close();
            }
            catch { }
        }

        private string Mime(string file)
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            switch (ext)
            {
                case ".html": return "text/html; charset=utf-8";
                case ".css": return "text/css; charset=utf-8";
                case ".js": return "application/javascript; charset=utf-8";
                case ".jpg": case ".jpeg": return "image/jpeg";
                case ".png": return "image/png";
                case ".svg": return "image/svg+xml";
                case ".ico": return "image/x-icon";
                case ".json": return "application/json; charset=utf-8";
                default: return "application/octet-stream";
            }
        }

        private void DispatchCommand(string body)
        {
            try
            {
                Dictionary<string, object> m = _json.Deserialize<Dictionary<string, object>>(body);
                if (m == null || !m.ContainsKey("cmd")) return;
                string cmd = Convert.ToString(m["cmd"]);
                string path = m.ContainsKey("path") ? Convert.ToString(m["path"]) : "";
                bool on = m.ContainsKey("on") ? Convert.ToBoolean(m["on"]) : false;
                BeginInvoke((Action)(() => DoCommand(cmd, path, on)));
            }
            catch (Exception ex)
            {
                WriteDebug("Dispatch: " + ex.Message);
            }
        }

        private void DoCommand(string cmd, string path, bool on)
        {
            WriteDebug("CMD " + cmd + " " + path);
            switch (cmd)
            {
                case "launch": Launch(GetGame(path)); break;
                case "openFolder": OpenFolder(path); break;
                case "remove": RemoveGame(path); break;
                case "add": ShowAddDialog(); break;
                case "pin": _topMost = on; TopMost = on; break;
                case "min": MinimizeToTray(true); break;
                case "close": MinimizeToTray(true); break;
                case "log": WriteDebug("JS:" + path); break;
            }
        }

        private string GetStateJson()
        {
            lock (_gamesLock)
            {
                List<GameDto> list = new List<GameDto>();
                foreach (Game g in _games)
                {
                    GameDto d = new GameDto();
                    d.name = g.Name;
                    d.path = g.FilePath;
                    d.platform = g.Platform;
                    d.ext = g.Ext;
                    d.cover = g.Cover;
                    d.icon = g.Icon;
                    d.last = g.LastPlayed;
                    d.appid = g.AppId;
                    list.Add(d);
                }
                List<ToastDto> toasts = new List<ToastDto>(_toasts);
                _toasts.Clear();
                var payload = new { games = list, toasts = toasts, pin = _topMost };
                return _json.Serialize(payload);
            }
        }

        /* ---------------- scanning ---------------- */
        private void ScanGames()
        {
            SetTray(TrayState.Active);
            List<Game> tmp = new List<Game>();
            try
            {
                foreach (string f in Directory.GetFiles(_gamesDir))
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext != ".lnk" && ext != ".url" && ext != ".exe") continue;
                    Game g = new Game();
                    g.FilePath = f;
                    g.Ext = ext;
                    g.Name = Path.GetFileNameWithoutExtension(f);
                    g.Name = Regex.Replace(g.Name, " - Atalho$", "");
                    g.Name = Regex.Replace(g.Name, "\\.exe$", "");
                    ReadTarget(g);
                    if (_playLog.ContainsKey(f)) g.LastPlayed = _playLog[f];
                    tmp.Add(g);
                }
                tmp.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
                lock (_gamesLock)
                {
                    _games = tmp;
                    foreach (Game g in _games) EnsureArt(g);
                }
            }
            catch { }
            SetTray(_trayBase);
        }

        private void ReadTarget(Game g)
        {
            try
            {
                if (g.Ext == ".lnk")
                {
                    Type wsh = Type.GetTypeFromProgID("WScript.Shell");
                    object sh = Activator.CreateInstance(wsh);
                    object sc = wsh.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { g.FilePath });
                    string target = (string)sc.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, sc, null);
                    string args = (string)sc.GetType().InvokeMember("Arguments", BindingFlags.GetProperty, null, sc, null);
                    string iconLoc = (string)sc.GetType().InvokeMember("IconLocation", BindingFlags.GetProperty, null, sc, null);
                    g.Target = (target + " " + args).Trim();
                    g.LnkIcon = ResolveIconLocation(iconLoc);
                    if (g.LnkIcon.Length == 0 && File.Exists(target)) g.LnkIcon = target;
                }
                else if (g.Ext == ".url")
                {
                    string content = File.ReadAllText(g.FilePath);
                    Match m = Regex.Match(content, @"URL=(.+)");
                    if (m.Success) g.Target = m.Groups[1].Value.Trim();
                }
                else g.Target = "";

                if (Regex.IsMatch(g.Target, "steam:|hydralauncher://run")) g.Platform = "Steam";
                else if (Regex.IsMatch(g.Target, "RiotClientServices")) g.Platform = "Riot";
                else if (Regex.IsMatch(g.Target, "Roblox")) g.Platform = "Roblox";
                else if (Regex.IsMatch(g.Target, "sklauncher|javaw")) g.Platform = "Minecraft";
                else if (Regex.IsMatch(g.Target, "EpicGamesLauncher")) g.Platform = "Epic";
                else if (Regex.IsMatch(g.Target, "Steam\\.exe", RegexOptions.IgnoreCase)) g.Platform = "Steam";

                Match aid = Regex.Match(g.Target, @"rungameid/(\d+)");
                if (!aid.Success) aid = Regex.Match(g.Target, @"objectId=(\d+)");
                if (aid.Success) g.AppId = aid.Groups[1].Value;
            }
            catch { }
        }

        private void EnsureArt(Game g)
        {
            string cid = g.AppId;
            if (cid.Length == 0 && _searchCache.ContainsKey(Norm(g.Name))) cid = _searchCache[Norm(g.Name)].ToString();

            if (cid.Length > 0)
            {
                string file = Path.Combine(_coverDir, cid + ".jpg");
                if (File.Exists(file))
                {
                    g.Cover = cid + ".jpg";
                    return;
                }
                int appIdNum = 0;
                if (!int.TryParse(cid, out appIdNum) || appIdNum <= 0) return;
                lock (_gamesLock)
                {
                    if (_pendingCover.Contains(cid)) return;
                    _pendingCover.Add(cid);
                }
                string targetFile = file;
                Task.Run(() => DownloadCover(cid, targetFile));
                return;
            }

            string key = IconKey(g.Name, g.FilePath);
            string ic = Path.Combine(_iconDir, key + ".png");
            if (File.Exists(ic))
            {
                g.Icon = key + ".png";
                return;
            }
            lock (_gamesLock)
            {
                if (_pendingIcon.Contains(key)) return;
                _pendingIcon.Add(key);
            }
            Task.Run(() =>
            {
                bool ok = ExtractIcon(g, ic);
                if (!ok)
                {
                    int appid = SearchSteam(g.Name);
                    if (appid > 0)
                    {
                        string cid2 = appid.ToString();
                        string file2 = Path.Combine(_coverDir, cid2 + ".jpg");
                        if (!File.Exists(file2)) DownloadCover(cid2, file2);
                        else WriteDebug("Search cover exists " + cid2);
                    }
                }
                else
                {
                    WriteDebug("Icon ok " + g.Name);
                }
                try { BeginInvoke((Action)(() => { ScanGames(); })); } catch { }
            });
        }

        private string Norm(string s)
        {
            return (s == null ? "" : s.Trim().ToLowerInvariant());
        }

        private string IconKey(string name, string filePath)
        {
            string s = name + "@" + filePath;
            unchecked
            {
                int h = 17;
                for (int i = 0; i < s.Length; i++) h = h * 31 + s[i];
                return ((uint)h).ToString();
            }
        }

        private string ResolveIconLocation(string iconLoc)
        {
            try
            {
                string p = (iconLoc ?? "").Split(',')[0].Trim();
                if (p.Length == 0) return "";
                p = Environment.ExpandEnvironmentVariables(p);
                if (File.Exists(p)) return p;
            }
            catch { }
            return "";
        }

        private string IconSourcePath(Game g)
        {
            if (g.Ext == ".exe") return g.FilePath;
            if (g.Ext == ".lnk")
            {
                if (g.LnkIcon.Length > 0) return g.LnkIcon;
                return "";
            }
            return "";
        }

        private bool ExtractIcon(Game g, string outPath)
        {
            try
            {
                string src = IconSourcePath(g);
                if (src.Length == 0 || !File.Exists(src)) return false;
                using (Icon ico = LoadAssociatedIcon(src))
                using (Bitmap bmp = ico.ToBitmap())
                {
                    if (bmp.Width < 12 || bmp.Height < 12) return false;
                    using (Bitmap big = new Bitmap(bmp, new Size(128, 128)))
                    {
                        big.Save(outPath, System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
                return File.Exists(outPath);
            }
            catch
            {
                return false;
            }
        }

        private static Icon LoadAssociatedIcon(string src)
        {
            string ext = Path.GetExtension(src).ToLowerInvariant();
            if (ext == ".ico") return new Icon(src);
            return System.Drawing.Icon.ExtractAssociatedIcon(src);
        }

        private int SearchSteam(string name)
        {
            string key = Norm(name);
            if (key.Length == 0) return -1;
            lock (_gamesLock)
            {
                if (_searchCache.ContainsKey(key)) return _searchCache[key];
                if (_pendingSearch.Contains(key)) return -1;
                _pendingSearch.Add(key);
            }
            int appid = 0;
            try
            {
                using (WebClient wc = new WebClient())
                {
                    wc.Headers["User-Agent"] = "Mozilla/5.0";
                    string url = "https://steamcommunity.com/actions/SearchApps/" + Uri.EscapeDataString(name);
                    string json = wc.DownloadString(url);
                    List<Dictionary<string, object>> arr = _json.Deserialize<List<Dictionary<string, object>>>(json);
                    if (arr != null)
                    {
                        foreach (Dictionary<string, object> item in arr)
                        {
                            string n = item.ContainsKey("name") ? Convert.ToString(item["name"]) : "";
                            if (Norm(n) == key)
                            {
                                appid = Convert.ToInt32(item.ContainsKey("appid") ? item["appid"] : 0);
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                WriteDebug("Search fail " + name + " -> " + ex.Message);
            }
            lock (_gamesLock)
            {
                if (appid > 0) _searchCache[key] = appid;
            }
            if (appid > 0) SaveSearchCache();
            return appid;
        }

        private void SaveSearchCache()
        {
            try
            {
                lock (_gamesLock)
                {
                    File.WriteAllText(Path.Combine(_hubDir, "searchcache.json"), _json.Serialize(_searchCache));
                }
            }
            catch { }
        }

        private void DownloadCover(string cid, string file)
        {
            string tmp = file + ".tmp";
            try
            {
                using (WebClient wc = new WebClient())
                {
                    wc.Headers["User-Agent"] = "Mozilla/5.0";
                    wc.DownloadFile("https://cdn.cloudflare.steamstatic.com/steam/apps/" + cid + "/header.jpg", tmp);
                }
                if (File.Exists(tmp))
                {
                    File.Delete(file);
                    File.Move(tmp, file);
                    WriteDebug("Cover ok " + cid);
                }
            }
            catch (Exception ex)
            {
                WriteDebug("Cover fail " + cid + " -> " + ex.Message);
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
            try { BeginInvoke((Action)(() => { ScanGames(); })); } catch { }
        }

        /* ---------------- watcher ---------------- */
        private void SetupWatcher()
        {
            if (_watcher != null) return;
            try
            {
                _watcher = new FileSystemWatcher(_gamesDir);
                _watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite;
                _watcher.Filter = "*.*";
                _watcher.Created += OnFsChanged;
                _watcher.Deleted += OnFsChanged;
                _watcher.Renamed += OnFsChanged;
                _watcher.Changed += OnFsChanged;
                _watcher.EnableRaisingEvents = true;

                _debounce = new Timer();
                _debounce.Interval = 700;
                _debounce.Tick += (s, e) =>
                {
                    _debounce.Stop();
                    WriteDebug("Debounce scan");
                    ScanGames();
                };
            }
            catch { }
        }

        private void OnFsChanged(object sender, FileSystemEventArgs e)
        {
            try
            {
                string ext = Path.GetExtension(e.Name).ToLowerInvariant();
                if (ext == ".lnk" || ext == ".url" || ext == ".exe")
                {
                    BeginInvoke((Action)(() =>
                    {
                        try
                        {
                            _debounce.Stop();
                            _debounce.Start();
                        }
                        catch { }
                    }));
                }
            }
            catch { }
        }

        /* ---------------- actions ---------------- */
        private Game GetGame(string path)
        {
            lock (_gamesLock)
            {
                foreach (Game g in _games)
                    if (string.Equals(g.FilePath, path, StringComparison.OrdinalIgnoreCase)) return g;
            }
            return null;
        }

        private void ShowAddDialog()
        {
            try
            {
                using (AddGameDialog dlg = new AddGameDialog(_gamesDir))
                {
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                    {
                        ScanGames();
                        AddToast(dlg.CreatedMessage, false);
                    }
                }
            }
            catch { }
        }

        private void Launch(Game g)
        {
            if (g == null) return;
            try
            {
                if (g.Ext == ".url")
                    Process.Start(g.Target);
                else
                    Process.Start(new ProcessStartInfo(g.FilePath) { UseShellExecute = true });
                lock (_gamesLock)
                {
                    _playLog[g.FilePath] = DateTime.Now.ToString("o");
                }
                SavePlayLog();
                ScanGames();
                AddToast(g.Name + " iniciando...", false);
                _trayBase = TrayState.Disabled;
                SetTray(TrayState.Disabled);
                MinimizeToTray(true);
            }
            catch
            {
                AddToast("Falha ao iniciar " + g.Name, true);
            }
        }

        private void SavePlayLog()
        {
            try
            {
                lock (_gamesLock)
                {
                    File.WriteAllText(Path.Combine(_hubDir, "playlog.json"), _json.Serialize(_playLog));
                }
            }
            catch { }
        }

        private void OpenFolder(string path)
        {
            try
            {
                Process.Start("explorer.exe", "/select,\"" + path + "\"");
            }
            catch { }
        }

        private void RemoveGame(string path)
        {
            try
            {
                string name = Path.GetFileName(path);
                string dest = Path.Combine(_trashDir, name);
                if (!File.Exists(dest)) File.Move(path, dest);
                else if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase)) { }
                else File.Delete(path);
                ScanGames();
                AddToast("Removido do hub.", false);
            }
            catch
            {
                AddToast("Nao foi possivel remover.", true);
            }
        }
    }

    public static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SetCurrentProcessExplicitAppUserModelID(string appID);
        private const int SW_RESTORE = 9;

        [STAThread]
        public static void Main()
        {
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                ServicePointManager.Expect100Continue = false;
            }
            catch { }
            try
            {
                System.Diagnostics.Process[] procs = System.Diagnostics.Process.GetProcessesByName(System.Diagnostics.Process.GetCurrentProcess().ProcessName);
                if (procs.Length > 1)
                {
                    IntPtr h = FindWindow(null, "GamesHub");
                    if (h != IntPtr.Zero)
                    {
                        ShowWindow(h, SW_RESTORE);
                        SetForegroundWindow(h);
                    }
                    return;
                }
            }
            catch { }
            try { SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { }
            try { SetCurrentProcessExplicitAppUserModelID("GamesHub.GamesHub.1"); } catch { }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new LobbyForm());
        }
    }
}