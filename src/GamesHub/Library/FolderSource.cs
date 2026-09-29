using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace GamesHub
{
    internal sealed class FolderSource
    {
        private sealed class CacheEntry { public long Size; public DateTime MTime; public ShortcutInfo Info; }

        private readonly object _gate = new object();
        private readonly Dictionary<string, CacheEntry> _cache = new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Scans the top level of gamesDir. Unchanged files (same size + mtime) are not re-parsed.</summary>
        public List<Game> Scan(string gamesDir)
        {
            var games = new List<Game>();
            if (string.IsNullOrWhiteSpace(gamesDir) || !Directory.Exists(gamesDir)) return games;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in Directory.EnumerateFiles(gamesDir))
            {
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (!GameRules.IsGameFileExt(ext)) continue;
                FileInfo fi;
                try
                {
                    fi = new FileInfo(path);
                    if ((fi.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                }
                catch (Exception ex) { Log.Warn("Folder scan: cannot stat " + path, ex); continue; }
                seen.Add(path);
                ShortcutInfo info = GetInfo(fi, ext);
                Game g = BuildGame(path, info, gamesDir);
                g.AddedAt = fi.CreationTime;
                ResolveLauncherInstallDir(g, info);
                games.Add(g);
            }
            lock (_gate)
            {
                var stale = new List<string>();
                foreach (string k in _cache.Keys) if (!seen.Contains(k)) stale.Add(k);
                foreach (string k in stale) _cache.Remove(k);
            }
            return games;
        }

        /// <summary>Cached shortcut data for a file (parses it if needed). Never null.</summary>
        public ShortcutInfo GetShortcut(string path)
        {
            try { return GetInfo(new FileInfo(path), Path.GetExtension(path).ToLowerInvariant()); }
            catch (Exception ex) { Log.Warn("GetShortcut failed: " + path, ex); return new ShortcutInfo(); }
        }

        private ShortcutInfo GetInfo(FileInfo fi, string ext)
        {
            if (ext == ".exe") return new ShortcutInfo { Target = fi.FullName };
            long size = fi.Length;
            DateTime mtime = fi.LastWriteTimeUtc;
            lock (_gate)
            {
                if (_cache.TryGetValue(fi.FullName, out CacheEntry c) && c.Size == size && c.MTime == mtime) return c.Info;
            }
            ShortcutInfo info;
            try
            {
                info = ext == ".lnk" ? LibShellLink.ReadLnk(fi.FullName)
                                     : LibShellLink.ParseUrlFile(File.ReadAllText(fi.FullName, Encoding.Default));
            }
            catch (Exception ex)
            {
                Log.Warn("Cannot parse shortcut " + fi.FullName, ex);
                info = new ShortcutInfo();
            }
            lock (_gate) _cache[fi.FullName] = new CacheEntry { Size = size, MTime = mtime, Info = info };
            return info;
        }

        /// <summary>Pure: builds the folder Game for a file and its parsed shortcut data.</summary>
        public static Game BuildGame(string filePath, ShortcutInfo info, string gamesDir)
        {
            string fileName = Path.GetFileName(filePath);
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            info = info ?? new ShortcutInfo();
            var g = new Game
            {
                Id = GameRules.FolderId(fileName),
                Name = GameRules.CleanName(fileName),
                Source = GameRules.SourceFolder,
                FilePath = filePath,
                Ext = ext,
                LaunchTarget = ext == ".url" && info.Target.Length > 0 ? info.Target : filePath,
            };
            string detect = ext == ".exe" ? filePath : info.DetectionText;
            g.Platform = GameRules.DetectPlatform(detect);
            g.SteamAppId = GameRules.ExtractSteamAppId(detect);

            string target = ext == ".url" ? "" : info.Target;
            if (target.Length > 0 && !GameRules.IsLauncherExe(target) && g.Platform != "Steam" && !GameRules.IsUri(target))
            {
                if (target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) g.Exe = target;
                string dir = SafeDir(target);
                if (!GameRules.IsGenericDir(dir, gamesDir)) g.InstallDir = GameRules.NormalizeDir(dir);
            }
            return g;
        }

        private static string SafeDir(string path)
        {
            try { return Path.GetDirectoryName(path) ?? ""; }
            catch (ArgumentException ex) { Log.Warn("Bad path in shortcut: " + path, ex); return ""; }
        }

        private static readonly Dictionary<string, string> RiotProducts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "league_of_legends", "League of Legends" }, { "valorant", "VALORANT" }, { "bacon", "LoR" }, { "lion", "2XKO" },
        };

        /// <summary>Riot shortcuts target the Riot Client; the game lives next to it (…\Riot Games\&lt;Game&gt;).
        /// Knowing that folder lets the play tracker work for them.</summary>
        private static void ResolveLauncherInstallDir(Game g, ShortcutInfo info)
        {
            if (g.Platform != "Riot" || g.InstallDir.Length > 0) return;
            Match m = Regex.Match(info.Arguments ?? "", @"--launch-product=([\w\-]+)", RegexOptions.IgnoreCase);
            if (!m.Success || !RiotProducts.TryGetValue(m.Groups[1].Value, out string folder)) return;
            string target = info.Target ?? "";
            int i = target.IndexOf(@"\Riot Client\", StringComparison.OrdinalIgnoreCase);
            if (i <= 0) return;
            string dir = Path.Combine(target.Substring(0, i), folder);
            if (Directory.Exists(dir)) g.InstallDir = dir;
        }
    }
}
