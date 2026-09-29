// OWNER: LIB agent. Installed Epic Games Store games from the launcher's .item manifests (read-only).
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GamesHub
{
    internal static class EpicSource
    {
        public static string ManifestsDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");

        public static List<Game> Scan(string manifestsDir)
        {
            var games = new List<Game>();
            if (!Directory.Exists(manifestsDir)) return games;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(manifestsDir, "*.item").ToList(); }
            catch (Exception ex) { Log.Warn("Cannot list " + manifestsDir, ex); return games; }
            foreach (string file in files)
            {
                try
                {
                    Game g = ParseManifest(File.ReadAllText(file));
                    if (g != null && seen.Add(g.Id)) games.Add(g);
                }
                catch (Exception ex) { Log.Warn("Cannot read Epic manifest " + file, ex); }
            }
            return games;
        }

        /// <summary>Pure: a Game for a complete, base-game manifest; null for DLC/add-ons/incomplete installs.</summary>
        public static Game ParseManifest(string json)
        {
            var d = Json.DeserializeObject(json) as IDictionary<string, object>;
            if (d == null) return null;
            string appName = Json.Str(d, "AppName").Trim();
            if (appName.Length == 0 || Json.Bool(d, "bIsIncompleteInstall")) return null;
            string main = Json.Str(d, "MainGameAppName").Trim();
            if (main.Length > 0 && !main.Equals(appName, StringComparison.OrdinalIgnoreCase)) return null;
            var cats = new List<string>();
            if (d.TryGetValue("AppCategories", out object c) && c is IEnumerable e && !(c is string) && !(c is IDictionary))
                cats = e.Cast<object>().Select(o => Convert.ToString(o).ToLowerInvariant()).ToList();
            if (cats.Any(x => x.StartsWith("addons") || x == "digitalextras" || x == "plugins" || x == "engines")) return null;
            if (cats.Count > 0 && !cats.Contains("games")) return null;

            string ns = Json.Str(d, "CatalogNamespace"), item = Json.Str(d, "CatalogItemId");
            string install = Json.Str(d, "InstallLocation").Replace('/', '\\').TrimEnd('\\');
            string exe = Json.Str(d, "LaunchExecutable").Replace('/', '\\').TrimStart('\\');
            string display = Json.Str(d, "DisplayName").Trim();
            return new Game
            {
                Id = GameRules.EpicId(appName),
                Name = display.Length > 0 ? display : appName,
                Source = GameRules.SourceEpic,
                Platform = "Epic",
                LaunchTarget = "com.epicgames.launcher://apps/" + Uri.EscapeDataString(ns) + "%3A" + Uri.EscapeDataString(item)
                               + "%3A" + Uri.EscapeDataString(appName) + "?action=launch&silent=true",
                InstallDir = install,
                Exe = install.Length > 0 && exe.Length > 0 && exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                      ? Path.Combine(install, exe) : "",
            };
        }
    }
}
