using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace GamesHub
{
    internal static class SteamSource
    {
        private static readonly HashSet<string> SkipAppIds = new HashSet<string> { "228980", "1070560", "1391110", "1628350", "250820", "1826330" };
        private static readonly Regex SkipNames = new Regex(
            @"Steamworks Common Redistributables|^Proton\b|Steam Linux Runtime|SteamVR|Steam Audio|Source SDK|Dedicated Server\b|Steam Controller Configs",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>Steam install folder from HKCU\Software\Valve\Steam\SteamPath (read-only), or "".</summary>
        public static string FindSteamPath()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam", false))
                {
                    string p = k?.GetValue("SteamPath") as string;
                    if (!string.IsNullOrEmpty(p)) return p.Replace('/', '\\');
                }
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam", false))
                    return (k?.GetValue("InstallPath") as string) ?? "";
            }
            catch (Exception ex) when (ExpectedErrors.IsRegistry(ex)) { Log.Warn("Cannot read Steam path from registry", ex); return ""; }
        }

        /// <summary>Library folders (including the Steam folder itself), deduplicated, existing only.</summary>
        public static List<string> LibraryFolders(string steamPath)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(steamPath)) return result;
            result.Add(steamPath);
            string vdf = new[] { Path.Combine(steamPath, "steamapps", "libraryfolders.vdf"), Path.Combine(steamPath, "config", "libraryfolders.vdf") }
                .FirstOrDefault(File.Exists);
            if (vdf != null)
            {
                try { result.AddRange(ParseLibraryFolders(File.ReadAllText(vdf))); }
                catch (Exception ex) when (ExpectedErrors.IsFileSystem(ex)) { Log.Warn("Cannot read " + vdf, ex); }
            }
            return result.Select(GameRules.NormalizeDir).Where(d => d.Length > 0 && Directory.Exists(Path.Combine(d, "steamapps")))
                         .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>Pure: library paths from libraryfolders.vdf (new format "N" { "path" ... } and old "N" "path").</summary>
        public static List<string> ParseLibraryFolders(string vdfText)
        {
            var list = new List<string>();
            VdfNode root = VdfParser.Parse(vdfText);
            VdfNode lf = root.Node("libraryfolders") ?? root.Node("LibraryFolders");
            if (lf == null) return list;
            // Library entries are numbered keys; newer files nest {"path": …}, older ones store the path directly.
            list.AddRange(lf.Keys.Where(k => Regex.IsMatch(k, @"^\d+$"))
                .Select(k => lf.Items[k] is VdfNode n ? n.Str("path") : lf.Items[k] as string)
                .Where(p => !string.IsNullOrEmpty(p)));
            return list;
        }

        /// <summary>All installed games across the given libraries.</summary>
        public static List<Game> Scan(IEnumerable<string> libraries)
        {
            var games = new List<Game>();
            var seen = new HashSet<string>();
            foreach (string lib in libraries)
            {
                string apps = Path.Combine(lib, "steamapps");
                IEnumerable<string> files;
                try { files = Directory.EnumerateFiles(apps, "appmanifest_*.acf").ToList(); }
                catch (Exception ex) when (ExpectedErrors.IsFileSystem(ex)) { Log.Warn("Cannot list " + apps, ex); continue; }
                foreach (string acf in files)
                {
                    try
                    {
                        Game g = ParseAppManifest(File.ReadAllText(acf), lib);
                        if (g != null && seen.Add(g.SteamAppId)) games.Add(g);
                    }
                    catch (Exception ex) when (ExpectedErrors.IsFileSystem(ex)) { Log.Warn("Cannot read " + acf, ex); }
                }
            }
            return games;
        }

        /// <summary>Pure: a Game for an installed, real game manifest; null for tools/redistributables/uninstalled.</summary>
        public static Game ParseAppManifest(string acfText, string libraryPath)
        {
            VdfNode st = VdfParser.Parse(acfText).Node("AppState");
            if (st == null) return null;
            string appId = st.Str("appid"), name = st.Str("name").Trim(), installDir = st.Str("installdir");
            if (!Regex.IsMatch(appId, @"^\d+$") || SkipAppIds.Contains(appId)) return null;
            if (!long.TryParse(st.Str("StateFlags"), out long flags) || (flags & 4) == 0) return null;
            if (name.Length == 0) name = "Steam " + appId;
            if (SkipNames.IsMatch(name)) return null;
            return new Game
            {
                Id = GameRules.SteamId(appId),
                Name = name,
                Source = GameRules.SourceSteam,
                Platform = "Steam",
                LaunchTarget = "steam://rungameid/" + appId,
                SteamAppId = appId,
                // installdir comes from the manifest: it must name a folder inside steamapps\common.
                InstallDir = installDir.Length > 0
                    ? SafePath.Combine(Path.Combine(libraryPath, "steamapps", "common"), installDir) ?? "" : "",
            };
        }

        /// <summary>Folders to watch for manifest changes (…\steamapps).</summary>
        public static IEnumerable<string> WatchDirs(IEnumerable<string> libraries) => libraries.Select(l => Path.Combine(l, "steamapps"));
    }
}
