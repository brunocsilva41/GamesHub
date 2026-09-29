// OWNER: INSTALL agent. Path normalization + "obviously wrong root" detection (shared by sizes and uninstall matching).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GamesHub
{
    public static class InstallPaths
    {
        /// <summary>Full path, backslashes, no trailing separator (except "C:\"), env vars expanded. "" on failure.</summary>
        public static string Normalize(string p)
        {
            if (string.IsNullOrWhiteSpace(p)) return "";
            try
            {
                string s = Environment.ExpandEnvironmentVariables(p.Trim().Trim('"')).Replace('/', '\\');
                if (!Path.IsPathRooted(s)) return "";
                s = Path.GetFullPath(s);
                if (s.Length > 3) s = s.TrimEnd('\\');
                return s;
            }
            catch (Exception ex)
            {
                Log.Warn("InstallPaths.Normalize: " + p, ex);
                return "";
            }
        }

        public static string Key(string p) => Normalize(p).ToLowerInvariant();

        /// <summary>True when child == parent or child is inside parent (both normalized or raw).</summary>
        public static bool IsSameOrInside(string child, string parent)
        {
            string c = Key(child), p = Key(parent);
            if (c.Length == 0 || p.Length == 0) return false;
            if (c == p) return true;
            string pp = p.EndsWith("\\") ? p : p + "\\";
            return c.StartsWith(pp, StringComparison.Ordinal);
        }

        private static List<string> _sensitive;

        /// <summary>System/user folders that must never be treated as a game's install folder.</summary>
        public static List<string> SensitiveDirs()
        {
            if (_sensitive != null) return _sensitive;
            var list = new List<string>();
            void Add(string d) { string k = Key(d); if (k.Length > 0 && !list.Contains(k)) list.Add(k); }
            foreach (Environment.SpecialFolder f in new[] {
                Environment.SpecialFolder.Windows, Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86,
                Environment.SpecialFolder.CommonApplicationData, Environment.SpecialFolder.UserProfile,
                Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.MyDocuments,
                Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.LocalApplicationData,
                Environment.SpecialFolder.CommonDesktopDirectory, Environment.SpecialFolder.StartMenu,
                Environment.SpecialFolder.CommonStartMenu, Environment.SpecialFolder.Programs })
            {
                try { Add(Environment.GetFolderPath(f)); }
                catch (Exception ex) { Log.Warn("InstallPaths: special folder " + f, ex); }
            }
            Add(Environment.GetEnvironmentVariable("ProgramW6432"));
            Add(Environment.GetEnvironmentVariable("TEMP"));
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (profile.Length > 0) { Add(Path.GetDirectoryName(profile)); Add(Path.Combine(profile, "Downloads")); }
            Add(AppPaths.DefaultGamesDir);
            Add(AppPaths.DataDir);
            _sensitive = list;
            return list;
        }

        // Last path segments that are containers of many games (launcher libraries).
        private static readonly HashSet<string> ContainerNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "steam", "steamapps", "steamlibrary", "epic games", "riot games", "xboxgames", "windowsapps",
            "gog games", "gog galaxy", "ubisoft", "ubisoft game launcher", "ea games", "origin games", "battle.net",
            "games", "jogos", "program files", "program files (x86)", "hydra", "hydra games", "rockstar games",
        };

        /// <summary>True for folders that are obviously not a single game's install dir: drive roots,
        /// Windows (and anything inside it), Program Files, the user profile and its parents, well-known
        /// user folders, and launcher/library containers (steamapps\common, "Epic Games", ...).</summary>
        public static bool IsForbiddenRoot(string dir) => IsForbiddenRoot(dir, SensitiveDirs());

        public static bool IsForbiddenRoot(string dir, IEnumerable<string> sensitive)
        {
            string k = Key(dir);
            if (k.Length == 0) return true;
            if (k.Length <= 3 || k.StartsWith("\\\\") && k.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries).Length <= 2)
                return true; // drive root or UNC share root
            string win = Key(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            if (win.Length > 0 && IsSameOrInside(k, win)) return true;
            foreach (string s in sensitive)
            {
                string sk = Key(s);
                if (sk.Length > 0 && IsSameOrInside(sk, k)) return true; // k is the sensitive dir or one of its parents
            }
            string name = Path.GetFileName(k);
            string parent = Path.GetFileName(Path.GetDirectoryName(k) ?? "");
            if (ContainerNames.Contains(name)) return true;
            if (name == "common" && parent == "steamapps") return true;
            if (name == "games" && (parent == "gog galaxy" || parent == "steam")) return true;
            return false;
        }
    }
}
