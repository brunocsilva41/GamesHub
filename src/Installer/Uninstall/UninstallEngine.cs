// optionally %LOCALAPPDATA%\GamesHub. Never touches the games folder.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace GamesHub.Installer
{
    internal sealed class UninstallEngine
    {
        public Action<int, string> Progress = (p, s) => { };
        public Func<bool> ConfirmCloseApp = () => true;
        public Func<bool> ConfirmKill = () => true;

        public readonly string InstallDir = Path.GetDirectoryName(Application.ExecutablePath);
        private readonly string self = Application.ExecutablePath;

        /// <summary>Files/folders of v1 and v2 that may be removed even when the manifest is missing.</summary>
        private static readonly string[] KnownFiles =
        {
            Product.ExeName, Product.ExeName + ".config", Product.LegacyExeName, Product.LegacyExeName + ".config",
            "Microsoft.Web.WebView2.Core.dll", "Microsoft.Web.WebView2.WinForms.dll", "WebView2Loader.dll", "WebView2-LICENSE.txt",
            "config.json", "icon.ico", Product.ManifestFile,
        };
        private static readonly string[] KnownDirs = { "web", Product.ExeName + ".WebView2", Product.LegacyExeName + ".WebView2", ".wvdata" };

        private void Report(int p, string s) { InstallerLog.Info("[" + p + "%] " + s); Progress(p, s); }

        public void Run(bool deleteUserData)
        {
            InstallerLog.Info("Uninstalling from " + InstallDir + " (deleteUserData=" + deleteUserData + ")");
            if (PathSafety.IsDangerousDir(InstallDir))
                throw new InvalidOperationException("O desinstalador está numa pasta inesperada (" + InstallDir + ") e não vai apagar nada.");

            Report(5, "Fechando o GamesHub…");
            if (AppProcesses.AnyRunning(InstallDir))
            {
                if (!ConfirmCloseApp()) throw new OperationCanceledException();
                if (!AppProcesses.CloseAll(InstallDir, 6000, ConfirmKill))
                    throw new InvalidOperationException("O GamesHub ainda está em execução. Feche-o (inclusive na bandeja do sistema) e tente novamente.");
            }

            Report(20, "Removendo atalhos…");
            Shortcuts.DeleteOurs(InstallDir);

            Report(35, "Limpando o registro…");
            CleanRegistry();

            Report(50, "Removendo arquivos…");
            DeleteAppFiles();

            if (deleteUserData)
            {
                Report(80, "Apagando seus dados do GamesHub…");
                DeleteUserData();
            }

            Shortcuts.NotifyShell();
            Report(100, "Concluído.");
        }

        private void CleanRegistry()
        {
            try
            {
                using (RegistryKey run = Registry.CurrentUser.OpenSubKey(Product.RunKey, true))
                {
                    string v = run?.GetValue(Product.RunValue) as string;
                    if (v != null && (v.IndexOf(InstallDir, StringComparison.OrdinalIgnoreCase) >= 0 || v.IndexOf(Product.ExeName, StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        run.DeleteValue(Product.RunValue, false);
                        InstallerLog.Info("Run value removed");
                    }
                }
            }
            catch (Exception ex) { InstallerLog.Warn("Could not clean Run key", ex); }

            try
            {
                string loc = null;
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(Product.UninstallKey))
                    loc = k?.GetValue("InstallLocation") as string;
                if (loc == null || string.Equals(loc.TrimEnd('\\'), InstallDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                {
                    Registry.CurrentUser.DeleteSubKeyTree(Product.UninstallKey, false);
                    InstallerLog.Info("Uninstall key removed");
                }
                else InstallerLog.Warn("Uninstall key points to another location (" + loc + "); kept");
            }
            catch (Exception ex) { InstallerLog.Warn("Could not remove uninstall key", ex); }
        }

        private void DeleteAppFiles()
        {
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string manifest = Path.Combine(InstallDir, Product.ManifestFile);
            try
            {
                if (File.Exists(manifest))
                    foreach (string rel in File.ReadAllLines(manifest, Encoding.UTF8))
                        if (rel.Trim().Length > 0) files.Add(rel.Trim());
            }
            catch (Exception ex) { InstallerLog.Warn("Cannot read manifest", ex); }
            foreach (string f in KnownFiles) files.Add(f);
            try
            {
                foreach (string ico in Directory.GetFiles(InstallDir, "gamehub*.ico")) files.Add(Path.GetFileName(ico));
            }
            catch (Exception ex) { InstallerLog.Warn("Cannot list icons", ex); }

            var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string rel in files)
            {
                string full;
                try { full = Path.GetFullPath(Path.Combine(InstallDir, rel)); }
                catch (Exception ex) { InstallerLog.Warn("Bad manifest entry " + rel, ex); continue; }
                if (!PathSafety.IsInside(full, InstallDir) || string.Equals(full, self, StringComparison.OrdinalIgnoreCase)) continue;
                for (string d = Path.GetDirectoryName(full); d != null && PathSafety.IsInside(d, InstallDir); d = Path.GetDirectoryName(d)) dirs.Add(d);
                TryDelete(() => { if (File.Exists(full)) File.Delete(full); });
            }
            foreach (string d in KnownDirs)
            {
                string full = Path.Combine(InstallDir, d);
                TryDelete(() => { if (Directory.Exists(full)) Directory.Delete(full, true); });
            }
            // now-empty subfolders, deepest first
            foreach (string d in dirs.OrderByDescending(x => x.Length))
                TryDelete(() => { if (Directory.Exists(d) && !Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d); });
        }

        private void DeleteUserData()
        {
            string data = Product.DataDir;
            if (!data.EndsWith("\\" + Product.Name, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(data)) return;
            for (int attempt = 1; attempt <= 10 && Directory.Exists(data); attempt++)
            {
                try { Directory.Delete(data, true); InstallerLog.Info("User data deleted"); }
                catch (Exception ex)
                {
                    // WebView2 helper processes may hold files for a moment after the app exits
                    InstallerLog.Warn("Retry " + attempt + " deleting user data", ex);
                    Thread.Sleep(500);
                }
            }
        }

        private static void TryDelete(Action a)
        {
            for (int attempt = 1; attempt <= 5; attempt++)
            {
                try { a(); return; }
                catch (Exception ex)
                {
                    InstallerLog.Warn("Delete attempt " + attempt + " failed", ex);
                    Thread.Sleep(200);
                }
            }
        }

        /// <summary>After this process exits: deletes Uninstall.exe and removes the install folder if it is empty
        /// (non-recursive, so anything the user put there survives).</summary>
        public void ScheduleSelfDelete()
        {
            try
            {
                string cmd = "/d /c ping 127.0.0.1 -n 3 >nul & del /f /q " + Product.Quote(self) + " & rmdir " + Product.Quote(InstallDir);
                using (Process.Start(new ProcessStartInfo("cmd.exe", cmd)
                {
                    UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = Path.GetTempPath(),
                })) { }
                InstallerLog.Info("Self-delete scheduled");
            }
            catch (Exception ex) { InstallerLog.Warn("Could not schedule self-delete", ex); }
        }
    }
}
