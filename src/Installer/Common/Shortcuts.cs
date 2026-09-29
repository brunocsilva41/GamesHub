using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace GamesHub.Installer
{
    internal static class Shortcuts
    {
        public static string DesktopLink =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Product.Name + ".lnk");
        public static string StartMenuLink =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Product.Name + ".lnk");

        /// <summary>Folders (non-recursive, plus the Start menu "GamesHub" subfolder) where our shortcuts may live.</summary>
        public static IEnumerable<string> SearchFolders()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            return new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                programs,
                Path.Combine(programs, Product.Name),
                Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                Path.Combine(appData, @"Microsoft\Internet Explorer\Quick Launch"),
                Path.Combine(appData, @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar"),
                Path.Combine(appData, @"Microsoft\Internet Explorer\Quick Launch\User Pinned\StartMenu"),
            };
        }

        /// <summary>All .lnk files in SearchFolders() whose target is GamesHub.exe or GamesLounge.exe inside installDir.</summary>
        public static List<string> FindOurs(string installDir)
        {
            var found = new List<string>();
            string exe = Path.Combine(installDir, Product.ExeName);
            string legacy = Path.Combine(installDir, Product.LegacyExeName);
            foreach (string folder in SearchFolders())
            {
                if (!Directory.Exists(folder)) continue;
                string[] links;
                try { links = Directory.GetFiles(folder, "*.lnk"); }
                catch (Exception ex) { InstallerLog.Warn("Cannot list " + folder, ex); continue; }
                foreach (string lnk in links)
                {
                    string t = ShellLink.GetTarget(lnk);
                    if (string.IsNullOrEmpty(t)) continue;
                    if (string.Equals(t, exe, StringComparison.OrdinalIgnoreCase) || string.Equals(t, legacy, StringComparison.OrdinalIgnoreCase))
                        found.Add(lnk);
                }
            }
            return found;
        }

        public static ShellLink.Spec AppSpec(string installDir)
        {
            string exe = Path.Combine(installDir, Product.ExeName);
            return new ShellLink.Spec
            {
                Target = exe,
                WorkingDir = installDir,
                IconPath = exe,
                IconIndex = 0,
                Description = "Sua biblioteca de jogos",
                AppUserModelId = Product.AppUserModelId,
            };
        }

        public static void Create(string lnkPath, string installDir)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(lnkPath));
            ShellLink.Save(lnkPath, AppSpec(installDir));
            InstallerLog.Info("Shortcut created: " + lnkPath);
        }

        /// <summary>Points every existing GamesHub/GamesLounge shortcut at the new GamesHub.exe. Returns how many.</summary>
        public static int RepointExisting(string installDir)
        {
            int n = 0;
            ShellLink.Spec spec = AppSpec(installDir);
            spec.Arguments = null; // keep whatever arguments the shortcut had
            foreach (string lnk in FindOurs(installDir))
            {
                try
                {
                    ShellLink.Retarget(lnk, spec);
                    n++;
                    InstallerLog.Info("Shortcut re-pointed: " + lnk);
                }
                catch (Exception ex) { InstallerLog.Warn("Could not re-point " + lnk, ex); }
            }
            return n;
        }

        public static int DeleteOurs(string installDir)
        {
            int n = 0;
            foreach (string lnk in FindOurs(installDir))
            {
                try { File.Delete(lnk); n++; InstallerLog.Info("Shortcut deleted: " + lnk); }
                catch (Exception ex) { InstallerLog.Warn("Could not delete " + lnk, ex); }
            }
            string sub = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Product.Name);
            try
            {
                if (Directory.Exists(sub) && Directory.GetFileSystemEntries(sub).Length == 0) Directory.Delete(sub);
            }
            catch (Exception ex) { InstallerLog.Warn("Could not remove " + sub, ex); }
            return n;
        }

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(int wEventId, int uFlags, IntPtr dwItem1, IntPtr dwItem2);

        /// <summary>Tells Explorer to refresh icons/associations.</summary>
        public static void NotifyShell()
        {
            try { SHChangeNotify(0x08000000 /* SHCNE_ASSOCCHANGED */, 0x1000 /* SHCNF_FLUSH */, IntPtr.Zero, IntPtr.Zero); }
            catch (Exception ex) { InstallerLog.Warn("SHChangeNotify failed", ex); }
        }
    }
}
