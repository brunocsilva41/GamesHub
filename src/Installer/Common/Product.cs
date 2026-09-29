using System;
using System.IO;
using System.Reflection;
using System.Text;
using Microsoft.Win32;

namespace GamesHub.Installer
{
    /// <summary>Product constants and well-known locations.</summary>
    internal static class Product
    {
        public const string Name = "GamesHub";
        public const string ExeName = "GamesHub.exe";
        public const string LegacyExeName = "GamesLounge.exe";
        public const string UninstallerName = "Uninstall.exe";
        public const string AppUserModelId = "GamesHub.App";
        public const string Publisher = "Bruno Silva";
        public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\GamesHub";
        public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const string RunValue = "GamesHub";
        /// <summary>List of files written by Setup (relative paths), used by Uninstall to delete only what we own.</summary>
        public const string ManifestFile = "install-files.txt";
        /// <summary>Command-line flag the app receives when started by Windows at logon.</summary>
        public const string AutostartArgs = "--minimized";

        public static string Version
        {
            get
            {
                Version v = Assembly.GetExecutingAssembly().GetName().Version;
                return v.Major + "." + v.Minor + "." + v.Build;
            }
        }

        public static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        public static string DefaultInstallDir => Path.Combine(LocalAppData, "Programs", Name);
        public static string DataDir => Path.Combine(LocalAppData, Name);
        public static string SettingsFile => Path.Combine(DataDir, "settings.json");
        public static string TempDir => Path.Combine(Path.GetTempPath(), Name);
        public static string DefaultGamesDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "jogos");

        /// <summary>InstallLocation from a previous install (HKCU uninstall key), or null.</summary>
        public static string RegisteredInstallDir()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(UninstallKey))
                {
                    string v = k?.GetValue("InstallLocation") as string;
                    return string.IsNullOrWhiteSpace(v) ? null : v;
                }
            }
            catch (Exception ex)
            {
                InstallerLog.Warn("Could not read uninstall key", ex);
                return null;
            }
        }

        public static string Quote(string s) => "\"" + s + "\"";
    }

    /// <summary>Plain-text log in %TEMP%\GamesHub\&lt;name&gt;.log (installers can't use the app's Log).</summary>
    internal static class InstallerLog
    {
        private static readonly object Gate = new object();
        public static string FilePath { get; private set; }

        public static void Init(string fileName)
        {
            try
            {
                Directory.CreateDirectory(Product.TempDir);
                FilePath = Path.Combine(Product.TempDir, fileName);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 512 * 1024) File.Delete(FilePath);
            }
            catch (Exception ex)
            {
                FilePath = null;
                System.Diagnostics.Debug.WriteLine("InstallerLog.Init failed: " + ex.Message);
            }
            Info("---- " + Path.GetFileName(System.Windows.Forms.Application.ExecutablePath) + " " + Product.Version
                 + " | args: " + string.Join(" ", Environment.GetCommandLineArgs(), 1, Environment.GetCommandLineArgs().Length - 1));
        }

        public static void Info(string msg) => Write("INFO ", msg, null);
        public static void Warn(string msg, Exception ex = null) => Write("WARN ", msg, ex);
        public static void Error(string msg, Exception ex = null) => Write("ERROR", msg, ex);

        private static void Write(string level, string msg, Exception ex)
        {
            if (FilePath == null) return;
            try
            {
                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + level + " " + msg;
                if (ex != null) line += " | " + ex.GetType().Name + ": " + ex.Message;
                lock (Gate) File.AppendAllText(FilePath, line + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine("InstallerLog write failed: " + e.Message); // logging must never throw
            }
        }

        /// <summary>Writes "OK:..." / "ERRO:..." to %TEMP%\GamesHub\&lt;fileName&gt; for scripted callers.</summary>
        public static void WriteResult(string fileName, string text)
        {
            try
            {
                Directory.CreateDirectory(Product.TempDir);
                File.WriteAllText(Path.Combine(Product.TempDir, fileName), text, new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                Warn("Could not write result file " + fileName, ex);
            }
        }
    }

    internal static class PathSafety
    {
        /// <summary>True for folders we must never install into / wipe (drive roots, system and profile roots).</summary>
        public static bool IsDangerousDir(string dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) return true;
            string full;
            try { full = Path.GetFullPath(dir).TrimEnd('\\'); }
            catch (Exception ex) { InstallerLog.Warn("Invalid path " + dir, ex); return true; }
            if (full.Length <= 3) return true; // "C:" / "C:\"
            Environment.SpecialFolder[] roots =
            {
                Environment.SpecialFolder.Windows, Environment.SpecialFolder.System, Environment.SpecialFolder.ProgramFiles,
                Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.UserProfile, Environment.SpecialFolder.DesktopDirectory,
                Environment.SpecialFolder.MyDocuments, Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolder.ApplicationData,
                Environment.SpecialFolder.Programs, Environment.SpecialFolder.StartMenu, Environment.SpecialFolder.CommonApplicationData,
            };
            foreach (Environment.SpecialFolder f in roots)
            {
                string p = Environment.GetFolderPath(f);
                if (!string.IsNullOrEmpty(p) && string.Equals(p.TrimEnd('\\'), full, StringComparison.OrdinalIgnoreCase)) return true;
            }
            string programs = Path.Combine(Product.LocalAppData, "Programs");
            return string.Equals(programs, full, StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetTempPath().TrimEnd('\\'), full, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsInside(string path, string dir)
        {
            string p = Path.GetFullPath(path);
            string d = Path.GetFullPath(dir).TrimEnd('\\') + "\\";
            return p.StartsWith(d, StringComparison.OrdinalIgnoreCase);
        }
    }
}
