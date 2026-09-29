using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace GamesHubUninstall
{
    internal static class Program
    {
        public const string AppName = "GamesHub";
        public const string RegPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\GamesHub";

        [STAThread]
        private static void Main(string[] args)
        {
            bool silent = false;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i] == null ? "" : args[i].ToLowerInvariant();
                if (a == "-silent" || a == "/silent") silent = true;
            }

            if (silent)
            {
                string result;
                try
                {
                    Uninstall.Run();
                    result = "OK";
                }
                catch (Exception ex)
                {
                    result = "ERRO:" + ex.Message;
                }
                WriteResultFile(ResultPath("gh_uninstall_result.txt"), result);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
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

    internal static class Uninstall
    {
        public static void Run()
        {
            string myExe = Application.ExecutablePath;
            string installDir = Path.GetDirectoryName(myExe);

            string desktopLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Program.AppName + ".lnk");
            string startLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Program.AppName + ".lnk");
            DeleteShortcutIfTargets(desktopLnk, installDir);
            DeleteShortcutIfTargets(startLnk, installDir);

            DeleteRegistry();

            if (Directory.Exists(installDir))
                DeleteTree(installDir, myExe);

            if (Directory.Exists(installDir) || File.Exists(myExe))
                LaunchCleanup(installDir, myExe);
        }

        private static void DeleteRegistry()
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(Program.RegPath, false);
            }
            catch (Exception) { }
        }

        private static void DeleteShortcutIfTargets(string lnkPath, string installDir)
        {
            if (!File.Exists(lnkPath)) return;
            try
            {
                string target = Shortcut.GetTarget(lnkPath);
                string expected = Path.Combine(installDir, "GamesLounge.exe");
                if (!String.IsNullOrEmpty(target) && String.Equals(target, expected, StringComparison.OrdinalIgnoreCase))
                    File.Delete(lnkPath);
            }
            catch (Exception) { }
        }

        private static void DeleteTree(string root, string myExe)
        {
            string[] dirs = Directory.GetDirectories(root);
            foreach (string d in dirs) DeleteTree(d, myExe);

            string[] files = Directory.GetFiles(root);
            foreach (string f in files)
            {
                if (String.Equals(f, myExe, StringComparison.OrdinalIgnoreCase)) continue;
                try { File.Delete(f); }
                catch (Exception) { }
            }

            try { Directory.Delete(root, false); }
            catch (Exception) { }
        }

        private static void LaunchCleanup(string installDir, string myExe)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "cmd.exe";
                psi.Arguments = "/c timeout /t 2 /nobreak >nul & rmdir /s /q \"" + installDir + "\" & del /q \"" + myExe + "\"";
                psi.UseShellExecute = true;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                psi.CreateNoWindow = true;
                Process.Start(psi);
            }
            catch (Exception) { }
        }
    }

    internal static class Shortcut
    {
        public static string GetTarget(string lnkPath)
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
            catch
            {
                return null;
            }
            finally
            {
                Marshal.FinalReleaseComObject(shell);
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

        private Button btnUninstall;
        private Button btnCancel;

        public MainForm()
        {
            Text = Program.AppName + " — Desinstalacao";
            try { Icon = new Icon(Path.Combine(Application.StartupPath, "gamehub-uninstaller.ico")); } catch { }
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = COL_BACK;
            ForeColor = COL_TEXT;
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(520, 320);

            Label lblTitle = new Label();
            lblTitle.Text = Program.AppName + " — Desinstalacao";
            lblTitle.Font = new Font("Segoe UI", 15f, FontStyle.Bold);
            lblTitle.ForeColor = COL_ACCENT2;
            lblTitle.BackColor = COL_BACK;
            lblTitle.AutoSize = true;
            lblTitle.Location = new Point(24, 22);
            Controls.Add(lblTitle);

            Panel separator = new Panel();
            separator.BackColor = COL_BORDER;
            separator.Size = new Size(472, 1);
            separator.Location = new Point(24, 60);
            Controls.Add(separator);

            Label lblBody = new Label();
            lblBody.Text = "Esta acao remove o " + Program.AppName + " deste computador.\n\n"
                + "Os JOGOS e DADOS (capas, icones, historico) na sua pasta de jogos\n"
                + "serao PRESERVADOS e nao serao apagados.\n\n"
                + "Deseja continuar?";
            lblBody.ForeColor = COL_TEXT;
            lblBody.BackColor = COL_BACK;
            lblBody.AutoSize = true;
            lblBody.Location = new Point(24, 84);
            Controls.Add(lblBody);

            btnCancel = MakeActionButton("Cancelar", 312, 260, false);
            btnCancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(btnCancel);

            btnUninstall = MakeActionButton("Desinstalar", 412, 260, true);
            btnUninstall.Click += delegate { StartUninstall(); };
            Controls.Add(btnUninstall);

            AcceptButton = btnUninstall;
            CancelButton = btnCancel;
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

        private void StartUninstall()
        {
            btnUninstall.Enabled = false;
            btnCancel.Enabled = false;

            try
            {
                Uninstall.Run();
                MessageBox.Show(this, Program.AppName + " desinstalado. Seus jogos foram preservados.",
                    Program.AppName + " — Desinstalacao", MessageBoxButtons.OK, MessageBoxIcon.Information);
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                btnUninstall.Enabled = true;
                btnCancel.Enabled = true;
                MessageBox.Show(this, ex.Message, Program.AppName + " — Desinstalacao", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}