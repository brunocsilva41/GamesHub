using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GamesHub
{
    /// <summary>A shallow, budgeted listing of a candidate install folder: every entry of the folder itself,
    /// its sub-folders, and deeper only along the usual binary/content paths ("Binaries\Win64", "Content\Paks",
    /// "bin\x64"…). Reparse points and redistributable/installer folders are never entered. Never throws.</summary>
    public sealed class FolderSnapshot
    {
        public sealed class Entry
        {
            /// <summary>Path relative to the root, lowercase, backslashes ("binaries\win64\game.exe").</summary>
            public string Rel = "";
            public string Name = "";
            public string FullPath = "";
            public long Size;
            public int Depth;
        }

        private const int RootCap = 400, SubCap = 250, DeepCap = 150, MaxSubDirs = 14;

        /// <summary>Folders whose contents are followed below the first level.</summary>
        private static readonly HashSet<string> DeepNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "binaries", "bin", "bin64", "bin32", "x64", "x86", "win64", "win32", "wingdk", "paks", "content", "retail",
            "game", "system", "program", "exe", "client", "plugins", "x86_64",
        };

        /// <summary>Folders never entered (installers, redistributables, anti-cheat, tooling, caches).</summary>
        private static readonly HashSet<string> SkipNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "_commonredist", "commonredist", "redist", "redistributables", "__installer", "installer", "installers", "directx",
            "vcredist", "dotnet", "prerequisites", "prereqs", "support", "easyanticheat", "battleye", "monobleedingedge",
            "thirdparty", "logs", "cache", "shadercache", "crashes", "screenshots", "saves", "savegames", "docs", "manual",
            "localization", "movies", "videos", "sounds", "audio", "music", "__pycache__", "node_modules", ".git",
        };

        /// <summary>Binary/content layout folders of a single game ("bin", "Binaries", "x64"…).</summary>
        public static bool IsLayoutDir(string name) => DeepNames.Contains(name ?? "");

        public string Root = "";
        public List<Entry> Files = new List<Entry>();
        /// <summary>Relative lowercase paths of the folders seen (entered or not).</summary>
        public HashSet<string> Dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>False when the root could not be read at all.</summary>
        public bool Readable;

        public IEnumerable<Entry> Exes => Files.Where(f => f.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

        public bool HasFile(string rel) => Files.Any(f => string.Equals(f.Rel, rel, StringComparison.OrdinalIgnoreCase));

        public static FolderSnapshot Take(string root, ScanBudget budget)
        {
            var s = new FolderSnapshot { Root = root ?? "" };
            if (string.IsNullOrWhiteSpace(root) || budget == null) return s;
            var level = new List<Tuple<DirectoryInfo, string>>();
            try { s.Readable = List(s, new DirectoryInfo(root), "", 0, RootCap, budget, level); }
            catch (Exception ex) when (DiscoveryErrors.IsIo(ex)) { Log.Warn("Discovery: cannot read " + root, ex); }
            if (!s.Readable) return s;
            // Every directory opened costs a seek on hard disks: layout/engine folders first, then the rest, capped.
            IEnumerable<Tuple<DirectoryInfo, string>> ordered = level
                .OrderBy(d => DeepNames.Contains(d.Item1.Name) || d.Item2.EndsWith("_data", StringComparison.Ordinal) || d.Item2 == "engine" ? 0 : 1)
                .Take(MaxSubDirs);
            foreach (Tuple<DirectoryInfo, string> d1 in ordered.ToList())
            {
                var next = new List<Tuple<DirectoryInfo, string>>();
                List(s, d1.Item1, d1.Item2, 1, SubCap, budget, next);
                foreach (Tuple<DirectoryInfo, string> d2 in next.Where(d => DeepNames.Contains(d.Item1.Name)).Take(4))
                {
                    var deeper = new List<Tuple<DirectoryInfo, string>>();
                    List(s, d2.Item1, d2.Item2, 2, DeepCap, budget, deeper);
                    foreach (Tuple<DirectoryInfo, string> d3 in deeper.Where(d => DeepNames.Contains(d.Item1.Name)).Take(3))
                        List(s, d3.Item1, d3.Item2, 3, DeepCap, budget, null);
                }
            }
            return s;
        }

        /// <summary>Lists one folder. Sub-folders to enter are appended to <paramref name="enter"/>.</summary>
        private static bool List(FolderSnapshot s, DirectoryInfo dir, string rel, int depth, int cap, ScanBudget budget,
                                 List<Tuple<DirectoryInfo, string>> enter)
        {
            int n = 0;
            try
            {
                foreach (FileSystemInfo e in dir.EnumerateFileSystemInfos())
                {
                    if (++n > cap || !budget.Spend()) break;
                    string childRel = rel.Length == 0 ? e.Name.ToLowerInvariant() : rel + "\\" + e.Name.ToLowerInvariant();
                    if (e is DirectoryInfo sub)
                    {
                        s.Dirs.Add(childRel);
                        if (enter != null && IsEnterable(sub)) enter.Add(Tuple.Create(sub, childRel));
                    }
                    else if (e is FileInfo f)
                    {
                        s.Files.Add(new Entry { Rel = childRel, Name = f.Name, FullPath = f.FullName, Size = f.Length, Depth = depth });
                    }
                }
                return true;
            }
            catch (Exception ex) when (DiscoveryErrors.IsIo(ex))
            {
                if (depth == 0) Log.Warn("Discovery: cannot list " + dir.FullName, ex);
                return n > 0;
            }
        }

        private static bool IsEnterable(DirectoryInfo d)
        {
            // Launchers/tools living inside a publisher folder ("Ubisoft\Ubisoft Game Launcher") are not game evidence.
            if (SkipNames.Contains(d.Name) || DiscoveryRules.IsNotAGame(d.Name)) return false;
            try { return (d.Attributes & (FileAttributes.ReparsePoint | FileAttributes.System)) == 0; }
            catch (Exception ex) when (DiscoveryErrors.IsIo(ex)) { return false; }
        }
    }
}
