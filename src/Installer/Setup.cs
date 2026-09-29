using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace GamesHubSetup
{
    internal static class Program
    {
        public const string AppName = "GamesHub";
        public const string Version = "1.0.0";
        public const string RegPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\GamesHub";

        [STAThread]
        private static void Main(string[] args)
        {
            bool silent = false;
            string dir = DefaultInstallDir();
            string games = DefaultGamesDir();

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i] == null ? "" : args[i].ToLowerInvariant();
                if (a == "-silent" || a == "/silent") { silent = true; }
                else if ((a == "-dir" || a == "/dir") && i + 1 < args.Length) { dir = args[++i]; }
                else if ((a == "-games" || a == "/games") && i + 1 < args.Length) { games = args[++i]; }
            }

            if (silent)
            {
                string result;
                try
                {
                    string installed = Install.Go(dir, games, true, true, null);
                    result = "OK:" + installed;
                }
                catch (Exception ex)
                {
                    result = "ERRO:" + ex.Message;
                }
                WriteResultFile(ResultPath("gh_setup_result.txt"), result);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(dir, games));
        }

        public static string DefaultInstallDir()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppName);
        }

        public static string DefaultGamesDir()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "jogos");
        }

        public static string ResultPath(string name)
        {
            return Path.Combine(Path.GetTempPath(), "opencode", name);
        }

        public static void WriteResultFile(string path, string text)
        {
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!String.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, text, new UTF8Encoding(false));
            }
            catch { }
        }
    }

    internal static class Install
    {
        public static string Go(string installDir, string gamesDir, bool desktopShortcut, bool startMenuShortcut, Action<string> progress)
        {
            installDir = Path.GetFullPath(installDir);
            gamesDir = Path.GetFullPath(gamesDir);

            string setupDir = Path.GetDirectoryName(Application.ExecutablePath);
            string srcExe = Path.Combine(setupDir, "GamesLounge.exe");
            string srcCore = Path.Combine(setupDir, "Microsoft.Web.WebView2.Core.dll");
            string srcWinForms = Path.Combine(setupDir, "Microsoft.Web.WebView2.WinForms.dll");
            string srcLoader = Path.Combine(setupDir, "WebView2Loader.dll");
            string srcUninst = Path.Combine(setupDir, "Uninstall.exe");
            string srcWeb = Path.Combine(setupDir, "web");
            string[] iconFiles = new string[] {
                "gamehub-app.ico",
                "gamehub-folder.ico",
                "gamehub-tray.ico",
                "gamehub-tray-active.ico",
                "gamehub-tray-notification.ico",
                "gamehub-tray-error.ico",
                "gamehub-tray-disabled.ico"
            };

            string[] required = new string[] { srcExe, srcCore, srcWinForms, srcLoader, srcUninst };
            for (int i = 0; i < required.Length; i++)
            {
                if (!File.Exists(required[i]))
                    throw new Exception("Arquivo de origem ausente: " + required[i]);
            }
            for (int i = 0; i < iconFiles.Length; i++)
            {
                if (!File.Exists(Path.Combine(setupDir, iconFiles[i])))
                    throw new Exception("Arquivo de origem ausente (icone): " + iconFiles[i]);
            }
            if (!Directory.Exists(srcWeb))
                throw new Exception("Pasta web de origem ausente: " + srcWeb);

            long totalBytes = 0;

            Notify(progress, "Criando pasta de destino...");
            try
            {
                Directory.CreateDirectory(installDir);
            }
            catch (Exception)
            {
                throw new Exception("Nao foi possivel criar a pasta de destino '" + installDir + "'. Verifique as permissoes de escrita.");
            }

            totalBytes += CopyFile(progress, srcExe, Path.Combine(installDir, "GamesLounge.exe"));
            totalBytes += CopyFile(progress, srcCore, Path.Combine(installDir, "Microsoft.Web.WebView2.Core.dll"));
            totalBytes += CopyFile(progress, srcWinForms, Path.Combine(installDir, "Microsoft.Web.WebView2.WinForms.dll"));
            totalBytes += CopyFile(progress, srcLoader, Path.Combine(installDir, "WebView2Loader.dll"));
            for (int i = 0; i < iconFiles.Length; i++)
                totalBytes += CopyFile(progress, Path.Combine(setupDir, iconFiles[i]), Path.Combine(installDir, iconFiles[i]));
            totalBytes += CopyFile(progress, srcUninst, Path.Combine(installDir, "Uninstall.exe"));
            totalBytes += CopyDirectory(progress, srcWeb, Path.Combine(installDir, "web"));

            Notify(progress, "Escrevendo config.json...");
            Dictionary<string, object> cfg = new Dictionary<string, object>();
            cfg["gamesDir"] = gamesDir;
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            string json = serializer.Serialize(cfg);
            File.WriteAllText(Path.Combine(installDir, "config.json"), json, new UTF8Encoding(false));

            string installedExe = Path.Combine(installDir, "GamesLounge.exe");
            string installedIcon = Path.Combine(installDir, "gamehub-app.ico");

            if (desktopShortcut)
            {
                Notify(progress, "Criando atalho na Area de Trabalho...");
                string desktopLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Program.AppName + ".lnk");
                Shortcut.Create(desktopLnk, installedExe, installDir, installedIcon);
            }

            if (startMenuShortcut)
            {
                Notify(progress, "Criando atalho no Menu Iniciar...");
                string startLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Program.AppName + ".lnk");
                Shortcut.Create(startLnk, installedExe, installDir, installedIcon);
            }
            RefreshShellIcons();

            Notify(progress, "Registrando desinstalacao...");
            string uninstPath = Path.Combine(installDir, "Uninstall.exe");
            long estimatedKb = totalBytes / 1024;
            if (estimatedKb > Int32.MaxValue) estimatedKb = Int32.MaxValue;

            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(Program.RegPath))
            {
                key.SetValue("DisplayName", Program.AppName);
                key.SetValue("DisplayVersion", Program.Version);
                key.SetValue("DisplayIcon", installedExe);
                key.SetValue("UninstallString", "\"" + uninstPath + "\"");
                key.SetValue("InstallLocation", installDir);
                key.SetValue("Publisher", "GamesHub");
                key.SetValue("EstimatedSize", (int)estimatedKb, RegistryValueKind.DWord);
                key.SetValue("NoModify", (int)1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", (int)1, RegistryValueKind.DWord);
            }

Notify(progress, "Concluido.");
            return installDir;
        }

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(int wEventId, int uFlags, IntPtr dwItem1, IntPtr dwItem2);

        private static void RefreshShellIcons()
        {
            try { SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_FLUSH, IntPtr.Zero, IntPtr.Zero); } catch { }
        }
        private const int SHCNE_ASSOCCHANGED = 0x08000000;
        private const int SHCNF_FLUSH = 0x1000;
 
        private static void Notify(Action<string> progress, string message)
        {
            if (progress != null) progress(message);
        }

        private static long CopyFile(Action<string> progress, string src, string dst)
        {
            Notify(progress, "Copiando " + Path.GetFileName(src) + "...");
            File.Copy(src, dst, true);
            return new FileInfo(dst).Length;
        }

        private static long CopyDirectory(Action<string> progress, string srcDir, string dstDir)
        {
            Directory.CreateDirectory(dstDir);
            long total = 0;
            string[] files = Directory.GetFiles(srcDir);
            for (int i = 0; i < files.Length; i++)
            {
                total += CopyFile(progress, files[i], Path.Combine(dstDir, Path.GetFileName(files[i])));
            }
            string[] dirs = Directory.GetDirectories(srcDir);
            for (int i = 0; i < dirs.Length; i++)
            {
                total += CopyDirectory(progress, dirs[i], Path.Combine(dstDir, Path.GetFileName(dirs[i])));
            }
            return total;
        }
    }

    internal static class Shortcut
    {
        public static void Create(string lnkPath, string target, string workingDir, string iconPath)
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) throw new Exception("WScript.Shell (Scripting Host) nao esta disponivel.");
            object shell = Activator.CreateInstance(shellType);
            try
            {
                object lnk = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });
                try
                {
                    Type lnkType = lnk.GetType();
                    lnkType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { target });
                    lnkType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { workingDir });
                    lnkType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, lnk, new object[] { iconPath + ",0" });
                    lnkType.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
                    Marshal.FinalReleaseComObject(lnk);
                }
                finally
                {
                    Marshal.FinalReleaseComObject(shell);
                }
            }
            catch
            {
                Marshal.FinalReleaseComObject(shell);
                throw;
            }
        }

        public static string GetTarget(string lnkPath)
        {
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return null;
                object shell = Activator.CreateInstance(shellType);
                try
                {
                    object lnk = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });
                    string target = (string)lnk.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, lnk, null);
                    Marshal.FinalReleaseComObject(lnk);
                    return target;
                }
                finally
                {
                    Marshal.FinalReleaseComObject(shell);
                }
            }
            catch
            {
                return null;
            }
        }
    }

    internal class MainForm : Form
    {
        private static readonly Color COL_BACK = Color.FromArgb(0x0b, 0x0d, 0x16);
        private static readonly Color COL_CARD = Color.FromArgb(0x12, 0x15, 0x20);
        private static readonly Color COL_BORDER = Color.FromArgb(0x2a, 0x2e, 0x3d);
        private static readonly Color COL_ACCENT = Color.FromArgb(0x7c, 0x5c, 0xff);
        private static readonly Color COL_ACCENT2 = Color.FromArgb(0x22, 0xd3, 0xee);
        private static readonly Color COL_TEXT = Color.FromArgb(0xe5, 0xe7, 0xeb);
        private static readonly Color COL_MUTED = Color.FromArgb(0x9c, 0xa3, 0xaf);

        private TextBox txtInstall;
        private TextBox txtGames;
        private CheckBox chkDesktop;
        private CheckBox chkStartMenu;
        private ProgressBar progressBar;
        private Label lblStatus;
        private Button btnInstall;
        private Button btnCancel;

        public MainForm(string installDir, string gamesDir)
        {
            Text = Program.AppName + " — Instalacao";
            try { Icon = new Icon(Path.Combine(Application.StartupPath, "gamehub-installer.ico")); } catch { }
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = COL_BACK;
            ForeColor = COL_TEXT;
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(560, 480);

            Label lblTitle = new Label();
            lblTitle.Text = Program.AppName + " — Instalacao";
            lblTitle.Font = new Font("Segoe UI", 16f, FontStyle.Bold);
            lblTitle.ForeColor = COL_TEXT;
            lblTitle.BackColor = COL_BACK;
            lblTitle.AutoSize = true;
            lblTitle.Location = new Point(24, 22);
            Controls.Add(lblTitle);

            Label lblVersion = new Label();
            lblVersion.Text = "v" + Program.Version;
            lblVersion.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            lblVersion.ForeColor = COL_ACCENT2;
            lblVersion.BackColor = COL_CARD;
            lblVersion.AutoSize = true;
            lblVersion.Padding = new Padding(6, 2, 6, 2);
            lblVersion.Location = new Point(238, 27);
            Controls.Add(lblVersion);

            Panel separator = new Panel();
            separator.BackColor = COL_BORDER;
            separator.Size = new Size(512, 1);
            separator.Location = new Point(24, 70);
            Controls.Add(separator);

            Label lblInstallCap = new Label();
            lblInstallCap.Text = "Destino da instalacao";
            lblInstallCap.ForeColor = COL_MUTED;
            lblInstallCap.BackColor = COL_BACK;
            lblInstallCap.AutoSize = true;
            lblInstallCap.Location = new Point(24, 96);
            Controls.Add(lblInstallCap);

            txtInstall = MakeTextBox(24, 118, 410);
            txtInstall.Text = installDir;
            Controls.Add(txtInstall);

            Button btnInstallBrowse = MakeBrowseButton(442, 116);
            btnInstallBrowse.Tag = "install";
            btnInstallBrowse.Click += OnBrowse;
            Controls.Add(btnInstallBrowse);

            Label lblGamesCap = new Label();
            lblGamesCap.Text = "Pasta de jogos";
            lblGamesCap.ForeColor = COL_MUTED;
            lblGamesCap.BackColor = COL_BACK;
            lblGamesCap.AutoSize = true;
            lblGamesCap.Location = new Point(24, 154);
            Controls.Add(lblGamesCap);

            txtGames = MakeTextBox(24, 176, 410);
            txtGames.Text = gamesDir;
            Controls.Add(txtGames);

            Button btnGamesBrowse = MakeBrowseButton(442, 174);
            btnGamesBrowse.Tag = "games";
            btnGamesBrowse.Click += OnBrowse;
            Controls.Add(btnGamesBrowse);

            Label lblHint = new Label();
            lblHint.Text = "Seus jogos e dados (capas, historico) ficam na pasta de jogos e nao serao tocados.";
            lblHint.ForeColor = COL_MUTED;
            lblHint.BackColor = COL_BACK;
            lblHint.AutoSize = true;
            lblHint.Font = new Font("Segoe UI", 8.25f, FontStyle.Italic);
            lblHint.Location = new Point(24, 206);
            Controls.Add(lblHint);

            chkDesktop = new CheckBox();
            chkDesktop.Text = "Criar atalho na Area de Trabalho";
            chkDesktop.Checked = true;
            chkDesktop.ForeColor = COL_TEXT;
            chkDesktop.BackColor = COL_BACK;
            chkDesktop.AutoSize = true;
            chkDesktop.Location = new Point(24, 236);
            Controls.Add(chkDesktop);

            chkStartMenu = new CheckBox();
            chkStartMenu.Text = "Criar atalho no Menu Iniciar";
            chkStartMenu.Checked = true;
            chkStartMenu.ForeColor = COL_TEXT;
            chkStartMenu.BackColor = COL_BACK;
            chkStartMenu.AutoSize = true;
            chkStartMenu.Location = new Point(24, 264);
            Controls.Add(chkStartMenu);

            lblStatus = new Label();
            lblStatus.Text = "";
            lblStatus.ForeColor = COL_MUTED;
            lblStatus.BackColor = COL_BACK;
            lblStatus.AutoSize = true;
            lblStatus.Location = new Point(24, 302);
            Controls.Add(lblStatus);

            progressBar = new ProgressBar();
            progressBar.Minimum = 0;
            progressBar.Maximum = 100;
            progressBar.Value = 0;
            progressBar.Style = ProgressBarStyle.Continuous;
            progressBar.Size = new Size(512, 16);
            progressBar.Location = new Point(24, 324);
            progressBar.Visible = false;
            Controls.Add(progressBar);

            btnCancel = MakeActionButton("Cancelar", 352, 420, false);
            btnCancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(btnCancel);

            btnInstall = MakeActionButton("Instalar", 452, 420, true);
            btnInstall.Click += delegate { StartInstall(); };
            Controls.Add(btnInstall);

            AcceptButton = btnInstall;
            CancelButton = btnCancel;
        }

        private TextBox MakeTextBox(int x, int y, int width)
        {
            TextBox tb = new TextBox();
            tb.Location = new Point(x, y);
            tb.Size = new Size(width, 26);
            tb.BackColor = COL_CARD;
            tb.ForeColor = COL_TEXT;
            tb.BorderStyle = BorderStyle.FixedSingle;
            return tb;
        }

        private Button MakeBrowseButton(int x, int y)
        {
            Button b = MakeActionButton("Browse...", x, y, false);
            return b;
        }

        private Button MakeActionButton(string text, int x, int y, bool primary)
        {
            Button b = new Button();
            b.Text = text;
            b.Location = new Point(x, y);
            b.Size = new Size(92, 32);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 1;
            if (primary)
            {
                b.BackColor = COL_ACCENT;
                b.ForeColor = Color.FromArgb(0xff, 0xff, 0xff);
                b.FlatAppearance.BorderColor = COL_ACCENT;
                b.FlatAppearance.MouseOverBackColor = Color.FromArgb(0x8b, 0x72, 0xff);
            }
            else
            {
                b.BackColor = COL_CARD;
                b.ForeColor = COL_TEXT;
                b.FlatAppearance.BorderColor = COL_BORDER;
                b.FlatAppearance.MouseOverBackColor = Color.FromArgb(0x18, 0x1b, 0x27);
            }
            b.Cursor = Cursors.Hand;
            return b;
        }

        private void OnBrowse(object sender, EventArgs e)
        {
            Button b = (Button)sender;
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                dlg.Description = (string)b.Tag == "install"
                    ? "Escolha a pasta de destino da instalacao"
                    : "Escolha a pasta onde ficam seus jogos";
                dlg.SelectedPath = ((string)b.Tag == "install" ? txtInstall.Text : txtGames.Text);
                dlg.ShowNewFolderButton = true;
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    if ((string)b.Tag == "install") txtInstall.Text = dlg.SelectedPath;
                    else txtGames.Text = dlg.SelectedPath;
                }
            }
        }

        private void StartInstall()
        {
            string installDir = txtInstall.Text.Trim();
            string gamesDir = txtGames.Text.Trim();

            if (installDir.Length == 0)
            {
                MessageBox.Show(this, "Informe a pasta de destino.", Program.AppName + " — Instalacao", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!Directory.Exists(gamesDir))
            {
                DialogResult r = MessageBox.Show(this,
                    "A pasta de jogos indicada nao existe:\n" + gamesDir + "\n\nDeseja continuar mesmo assim?",
                    Program.AppName + " — Instalacao", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (r != DialogResult.Yes) return;
            }

            btnInstall.Enabled = false;
            btnCancel.Enabled = false;
            txtInstall.Enabled = false;
            txtGames.Enabled = false;
            progressBar.Visible = true;
            progressBar.Value = 0;
            lblStatus.Text = "";

            try
            {
                string installed = Install.Go(installDir, gamesDir, chkDesktop.Checked, chkStartMenu.Checked, OnProgress);
                progressBar.Value = progressBar.Maximum;
                lblStatus.Text = "";
                DialogResult r = MessageBox.Show(this,
                    Program.AppName + " instalado com sucesso.\n\nDeseja abrir o " + Program.AppName + " agora?",
                    Program.AppName + " — Instalacao", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                DialogResult = DialogResult.OK;
                Close();
                if (r == DialogResult.Yes)
                {
                    try
                    {
                        using (Process p = new Process())
                        {
                            p.StartInfo.FileName = Path.Combine(installed, "GamesLounge.exe");
                            p.StartInfo.WorkingDirectory = installed;
                            p.StartInfo.UseShellExecute = true;
                            p.Start();
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                progressBar.Visible = false;
                btnInstall.Enabled = true;
                btnCancel.Enabled = true;
                txtInstall.Enabled = true;
                txtGames.Enabled = true;
                MessageBox.Show(this, ex.Message, Program.AppName + " — Instalacao", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnProgress(string message)
        {
            lblStatus.Text = message;
            if (progressBar.Value < progressBar.Maximum - 1) progressBar.Value += 1;
            Application.DoEvents();
        }
    }
}