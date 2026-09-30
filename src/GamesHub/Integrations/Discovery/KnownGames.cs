using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GamesHub
{
    /// <summary>What the library already has, for excluding discovery candidates: executables, install folders
    /// and normalized names. Install folders that are containers (drive roots, scan roots, system folders) are ignored
    /// so that one odd entry cannot hide every game in "D:\Jogos".</summary>
    public sealed class KnownGames
    {
        private readonly HashSet<string> _exes = new HashSet<string>();
        private readonly List<string> _exeKeys = new List<string>();
        private readonly List<string> _dirs = new List<string>();
        private readonly HashSet<string> _names = new HashSet<string>();
        private readonly HashSet<string> _containers = new HashSet<string>();

        public KnownGames(IEnumerable<Game> games)
        {
            foreach (Game g in (games ?? Enumerable.Empty<Game>()).Where(x => x != null))
            {
                foreach (string p in new[] { g.Exe, g.LaunchTarget, g.Ext == ".exe" ? g.FilePath : "" }
                             .Where(x => !string.IsNullOrWhiteSpace(x) && x.Trim().Trim('"').EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
                {
                    string k = DiscoveryRoots.Key(p);
                    if (k.Length > 0 && _exes.Add(k)) _exeKeys.Add(k);
                }
                string dir = DiscoveryRoots.Key(g.InstallDir);
                if (dir.Length > 0) _dirs.Add(dir);
                string name = DiscoveryRules.Compact(g.Name);
                if (name.Length > 0) _names.Add(name);
            }
        }

        public int Count => _names.Count;

        /// <summary>Scan roots (e.g. "D:\Jogos") are never treated as a known game's install folder.</summary>
        public void AddContainers(IEnumerable<string> keys)
        {
            foreach (string k in (keys ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrEmpty(x))) _containers.Add(k);
        }

        public bool Contains(DiscoveredGame c)
        {
            if (c == null) return false;
            string exe = DiscoveryRoots.Key(c.Exe);
            if (exe.Length > 0 && _exes.Contains(exe)) return true;
            string dir = DiscoveryRoots.Key(c.InstallDir);
            if (dir.Length > 0)
            {
                if (_dirs.Any(k => !IsContainer(k) && DiscoveryRoots.IsSameOrInside(dir, k))) return true;
                if (_exeKeys.Any(k => DiscoveryRoots.IsSameOrInside(k, dir))) return true;
            }
            string name = DiscoveryRules.Compact(c.Name);
            if (name.Length > 0 && _names.Contains(name)) return true;
            string folder = DiscoveryRules.Compact(Path.GetFileName(c.InstallDir ?? ""));
            return folder.Length >= 4 && _names.Contains(folder);
        }

        private bool IsContainer(string key) => _containers.Contains(key) || DiscoveryRoots.IsForbidden(key);
    }
}
