using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GamesHub
{
    /// <summary>A folder whose sub-folders are candidate game installs.</summary>
    public sealed class ScanRoot
    {
        public string Path = "";
        /// <summary>"games" (a folder meant for games: location prior), "xbox", "programs" or "user".</summary>
        public string Kind = "games";
        /// <summary>A sub-folder without executables may be a publisher/store container: look one level deeper.</summary>
        public bool Containers = true;

        public ScanRoot() { }
        public ScanRoot(string path, string kind, bool containers = true) { Path = path; Kind = kind; Containers = containers; }
    }

    /// <summary>Where discovery looks, and path helpers shared by the scan.</summary>
    public static class DiscoveryRoots
    {
        /// <summary>Per fixed drive (relative to its root): folders meant for games.</summary>
        private static readonly string[] GameFolders =
        {
            "Games", "Jogos", "Juegos", "GOG Games", "EA Games", "Origin Games", "Ubisoft Games", "Epic Games",
            @"Program Files (x86)\GOG Galaxy\Games", @"Program Files\GOG Galaxy\Games",
            @"Program Files\EA Games", @"Program Files (x86)\EA Games", @"Program Files (x86)\Origin Games", @"Program Files\Origin Games",
            @"Program Files (x86)\Ubisoft\Ubisoft Game Launcher\games", @"Program Files\Ubisoft\Ubisoft Game Launcher\games",
            @"Program Files\Epic Games", @"Program Files (x86)\Battle.net Games", "Battle.net Games",
        };

        /// <summary>Roots of every ready fixed drive plus the user's Documents and Desktop (depth 1 only).</summary>
        public static List<ScanRoot> Default()
        {
            var roots = new List<ScanRoot>();
            foreach (string drive in FixedDrives())
            {
                foreach (string f in GameFolders) roots.Add(new ScanRoot(System.IO.Path.Combine(drive, f), "games"));
                roots.Add(new ScanRoot(System.IO.Path.Combine(drive, "XboxGames"), "xbox", false));
                roots.Add(new ScanRoot(System.IO.Path.Combine(drive, "Program Files"), "programs"));
                roots.Add(new ScanRoot(System.IO.Path.Combine(drive, "Program Files (x86)"), "programs"));
                // Secondary drives often hold games (or publisher folders) right at the root: "D:\Zepetto\PointBlank".
                if (!string.Equals(drive, SystemDrive(), StringComparison.OrdinalIgnoreCase)) roots.Add(new ScanRoot(drive, "programs"));
            }
            foreach (string p in new[] { Environment.SpecialFolder.MyDocuments, Environment.SpecialFolder.DesktopDirectory }.Select(Environment.GetFolderPath))
                if (p.Length > 0) roots.Add(new ScanRoot(p, "user", false));
            var seen = new HashSet<string>();
            return roots.Where(r => seen.Add(Key(r.Path)) && SafeExists(r.Path)).ToList();
        }

        private static string SystemDrive()
        {
            try { return System.IO.Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? ""; }
            catch (Exception ex) when (DiscoveryErrors.IsIo(ex)) { return ""; }
        }

        public static List<string> FixedDrives()
        {
            var list = new List<string>();
            try
            {
                foreach (DriveInfo d in DriveInfo.GetDrives())
                {
                    try { if (d.DriveType == DriveType.Fixed && d.IsReady) list.Add(d.RootDirectory.FullName); }
                    catch (Exception ex) when (DiscoveryErrors.IsIo(ex)) { Log.Warn("Discovery: drive " + d.Name, ex); }
                }
            }
            catch (Exception ex) when (DiscoveryErrors.IsIo(ex)) { Log.Warn("Discovery: cannot list drives", ex); }
            return list;
        }

        /// <summary>Full path, lowercase, no trailing separator (except "c:\"). "" when invalid.</summary>
        public static string Key(string p)
        {
            if (string.IsNullOrWhiteSpace(p)) return "";
            try
            {
                string s = Environment.ExpandEnvironmentVariables(p.Trim().Trim('"')).Replace('/', '\\');
                if (!System.IO.Path.IsPathRooted(s) || s.StartsWith("\\\\", StringComparison.Ordinal)) return "";
                s = System.IO.Path.GetFullPath(s);
                if (s.Length > 3) s = s.TrimEnd('\\');
                return s.ToLowerInvariant();
            }
            catch (Exception ex) when (DiscoveryErrors.IsIo(ex)) { return ""; }
        }

        /// <summary>True when <paramref name="child"/> equals <paramref name="parent"/> or is inside it (keys).</summary>
        public static bool IsSameOrInside(string child, string parent)
        {
            if (child.Length == 0 || parent.Length == 0) return false;
            if (child == parent) return true;
            return child.StartsWith(parent.EndsWith("\\") ? parent : parent + "\\", StringComparison.Ordinal);
        }

        private static HashSet<string> _forbidden;

        /// <summary>Folders that are never a game's install folder: drive roots, Windows, Program Files, ProgramData,
        /// the user profile and its shell folders.</summary>
        public static bool IsForbidden(string key)
        {
            if (key.Length == 0 || key.Length <= 3) return true;
            if (_forbidden == null)
            {
                var set = new HashSet<string>();
                foreach (string k in new[] {
                    Environment.SpecialFolder.Windows, Environment.SpecialFolder.System, Environment.SpecialFolder.ProgramFiles,
                    Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.CommonApplicationData,
                    Environment.SpecialFolder.UserProfile, Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.MyDocuments,
                    Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.LocalApplicationData,
                    Environment.SpecialFolder.CommonProgramFiles, Environment.SpecialFolder.CommonProgramFilesX86 }
                    .Select(f => Key(Environment.GetFolderPath(f))))
                {
                    if (k.Length > 0) set.Add(k);
                }
                string w6432 = Key(Environment.GetEnvironmentVariable("ProgramW6432"));
                if (w6432.Length > 0) set.Add(w6432);
                _forbidden = set;
            }
            if (_forbidden.Contains(key)) return true;
            string win = Key(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            return win.Length > 0 && IsSameOrInside(key, win)
                   || key.EndsWith("\\program files", StringComparison.Ordinal) || key.EndsWith("\\program files (x86)", StringComparison.Ordinal)
                   || key.IndexOf("\\steamapps\\", StringComparison.Ordinal) >= 0 || key.EndsWith("\\steamapps", StringComparison.Ordinal)
                   || key.IndexOf("\\windowsapps", StringComparison.Ordinal) >= 0;
        }

        public static bool SafeExists(string dir)
        {
            try { return Directory.Exists(dir); }
            catch (Exception ex) when (DiscoveryErrors.IsIo(ex)) { return false; }
        }

        /// <summary>Immediate sub-folders (no reparse points, hidden or system folders). Budgeted; never throws.</summary>
        public static List<DirectoryInfo> SubDirs(string dir, ScanBudget budget, int cap = 500)
        {
            var list = new List<DirectoryInfo>();
            try
            {
                foreach (DirectoryInfo d in new DirectoryInfo(dir).EnumerateDirectories())
                {
                    if (list.Count >= cap || !budget.Spend()) break;
                    if ((d.Attributes & (FileAttributes.ReparsePoint | FileAttributes.System | FileAttributes.Hidden)) != 0) continue;
                    list.Add(d);
                }
            }
            catch (Exception ex) when (DiscoveryErrors.IsIo(ex)) { Log.Warn("Discovery: cannot list " + dir, ex); }
            return list;
        }
    }
}
