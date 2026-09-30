using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace GamesHub
{
    internal static class VariantSuggester
    {
        private const int MaxGroupSize = 8;

        // Shared launcher executables: many different games point at them, so they prove nothing.
        private static readonly HashSet<string> LauncherExes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "steam.exe", "epicgameslauncher.exe", "riotclientservices.exe", "riotclientux.exe", "riot client.exe",
            "battle.net.exe", "battle.net launcher.exe", "eadesktop.exe", "origin.exe", "ubisoftconnect.exe",
            "upc.exe", "uplay.exe", "galaxyclient.exe", "goggalaxy.exe", "explorer.exe", "cmd.exe",
            "powershell.exe", "rundll32.exe", "hydra.exe", "robloxplayerlauncher.exe", "minecraftlauncher.exe",
            "xboxpcapp.exe", "amazon games ui.exe", "itch.exe", "playnite.desktopapp.exe",
        };

        // Launcher install folders (matched at the end of the path) — shared by unrelated games.
        private static readonly string[] LauncherDirEnds =
        {
            "\\riot client", "\\steam", "\\epic games\\launcher", "\\battle.net", "\\ubisoft game launcher",
            "\\ea desktop", "\\gog galaxy", "\\origin", "\\hydra", "\\minecraft launcher",
        };
        // Folders anywhere in the path that host several different apps (Roblox Player + Studio share one).
        private static readonly string[] LauncherDirParts = { "\\roblox\\", "\\windowsapps\\" };

        public static List<VariantGroup> Suggest(List<Game> games, ISet<string> grouped, List<List<string>> dismissed)
        {
            var cands = (games ?? new List<Game>())
                .Where(g => g != null && !string.IsNullOrEmpty(g.Id) && !grouped.Contains(g.Id))
                .GroupBy(g => g.Id, StringComparer.OrdinalIgnoreCase).Select(x => x.First()).ToList();
            int n = cands.Count;
            var parent = Enumerable.Range(0, n).ToArray();
            Func<int, int> find = null;
            find = i => parent[i] == i ? i : (parent[i] = find(parent[i]));
            Action<int, int> union = (a, b) => { int ra = find(a), rb = find(b); if (ra != rb) parent[Math.Max(ra, rb)] = Math.Min(ra, rb); };

            LinkBy(cands, g => Platform(g) + "|" + VariantNames.BaseKey(g.Name), g => VariantNames.BaseKey(g.Name).Length >= 2, union);
            LinkBy(cands, g => (g.SteamAppId ?? "").Trim(), g => IsAppId(g.SteamAppId) && g.SteamAppId.Trim() != "0", union); // "0" = marked not-on-Steam
            LinkBy(cands, g => Platform(g) + "|" + PathKey(g.Exe), g => UsableExe(g.Exe), union);
            LinkBy(cands, g => Platform(g) + "|" + PathKey(g.InstallDir), g => UsableDir(g.InstallDir), union);

            var result = new List<VariantGroup>();
            foreach (var members in Enumerable.Range(0, n).GroupBy(find).Select(cluster => cluster.Select(i => cands[i]).ToList())
                                              .Where(m => m.Count >= 2 && m.Count <= MaxGroupSize))
            {
                List<string> key = VariantStore.SetKey(members.Select(m => m.Id));
                if (dismissed.Any(d => !key.Except(d, StringComparer.OrdinalIgnoreCase).Any())) continue;
                result.Add(Build(members, key));
            }
            return result.OrderBy(g => g.PrimaryId, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static void LinkBy(List<Game> c, Func<Game, string> key, Func<Game, bool> usable, Action<int, int> union)
        {
            var first = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < c.Count; i++)
            {
                if (!usable(c[i])) continue;
                string k = key(c[i]);
                if (first.TryGetValue(k, out int j)) union(j, i); else first[k] = i;
            }
        }

        private static VariantGroup Build(List<Game> members, List<string> key)
        {
            Game primary = ChoosePrimary(members);
            var ordered = new List<Game> { primary };
            ordered.AddRange(members.Where(m => m != primary)
                .OrderBy(m => VariantNames.Qualifier(m.Name), StringComparer.OrdinalIgnoreCase)
                .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase));
            var g = new VariantGroup
            {
                Id = "s" + Hash(string.Join("\n", key)),
                PrimaryId = primary.Id,
                MemberIds = ordered.Select(m => m.Id).ToList(),
            };
            foreach (Game m in ordered)
                g.Labels[m.Id] = VariantLabels.Default(m, m == primary, g.Labels.Values);
            return g;
        }

        /// <summary>Entry without qualifier, else most played, else shortest name.</summary>
        public static Game ChoosePrimary(List<Game> members)
            => members.OrderBy(m => VariantNames.Qualifier(m.Name).Length == 0 ? 0 : 1)
                      .ThenByDescending(m => m.PlaySeconds)
                      .ThenBy(m => (m.Name ?? "").Length)
                      .ThenBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
                      .First();

        private static string Platform(Game g) => (g.Platform ?? "").Trim().ToLowerInvariant();

        private static bool IsAppId(string s)
        {
            s = (s ?? "").Trim();
            return s.Length > 0 && s != "0" && s.All(char.IsDigit);
        }

        internal static string PathKey(string p)
            => (p ?? "").Trim().Trim('"').Replace('/', '\\').TrimEnd('\\').ToLowerInvariant();

        private static bool UsableExe(string exe)
        {
            string k = PathKey(exe);
            if (k.Length == 0 || k.Contains("://")) return false;
            string file = k.Substring(k.LastIndexOf('\\') + 1);
            return file.Length > 0 && !LauncherExes.Contains(file);
        }

        private static bool UsableDir(string dir)
        {
            string k = PathKey(dir);
            if (k.Length == 0 || k.Contains("://")) return false;
            // "C:\Games" or "D:\Program Files" are too generic; want at least drive\a\b.
            if (k.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries).Length < 3) return false;
            if (k.EndsWith("\\common") || k.EndsWith("\\steamapps")) return false;
            return !LauncherDirEnds.Any(h => k.EndsWith(h)) && !LauncherDirParts.Any(h => (k + "\\").Contains(h));
        }

        private static string Hash(string s)
        {
            using (var md5 = MD5.Create())
            {
                byte[] b = md5.ComputeHash(Encoding.UTF8.GetBytes(s.ToLowerInvariant()));
                return BitConverter.ToString(b, 0, 6).Replace("-", "").ToLowerInvariant();
            }
        }
    }

    internal static class VariantLabels
    {
        public const string DefaultPrimary = "Padrão";

        /// <summary>Qualifier ("DirectX 11"), "Padrão" for an unqualified primary, else the game name;
        /// made unique against labels already taken.</summary>
        public static string Default(Game g, bool isPrimary, IEnumerable<string> taken)
        {
            string q = VariantNames.Qualifier(g.Name);
            string label = q.Length > 0 ? q : isPrimary ? DefaultPrimary : (g.Name ?? "").Trim();
            if (label.Length == 0) label = g.Id;
            if (taken.Contains(label, StringComparer.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(g.Name)
                && !string.Equals(label, g.Name.Trim(), StringComparison.OrdinalIgnoreCase))
                label = g.Name.Trim();
            return label;
        }
    }
}
