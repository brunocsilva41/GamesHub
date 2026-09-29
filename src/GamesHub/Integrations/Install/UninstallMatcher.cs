using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace GamesHub
{
    public sealed class UninstallEntry
    {
        public string KeyPath = "";          // e.g. HKLM64\...\Uninstall\{GUID}
        public string DisplayName = "";
        public string InstallLocation = "";
        public string DisplayIcon = "";
        public string UninstallString = "";
        public bool SystemComponent;
    }

    public static class UninstallMatcher
    {
        public const int MinScore = 50;

        /// <summary>Score of one entry for a game with (normalized) install dir and name. 0 = no match.
        /// Location match beats icon match beats a strong name match.</summary>
        public static int Score(UninstallEntry e, string gameDir, string gameName)
            => Score(e, gameDir, gameName, InstallPaths.IsForbiddenRoot);

        public static int Score(UninstallEntry e, string gameDir, string gameName, Func<string, bool> isForbidden)
        {
            if (e == null || e.SystemComponent || string.IsNullOrWhiteSpace(e.UninstallString)) return 0;
            int score = 0;
            bool dirUsable = !string.IsNullOrEmpty(gameDir) && !isForbidden(gameDir);
            if (dirUsable)
            {
                string loc = InstallPaths.Normalize(e.InstallLocation);
                if (loc.Length > 0 && !isForbidden(loc))
                {
                    if (InstallPaths.Key(loc) == InstallPaths.Key(gameDir)) score = Math.Max(score, 100);
                    else if (InstallPaths.IsSameOrInside(gameDir, loc)) score = Math.Max(score, 90);   // game exe in a subfolder
                    else if (InstallPaths.IsSameOrInside(loc, gameDir)) score = Math.Max(score, 75);   // entry in a subfolder of the game
                }
                string icon = IconPath(e.DisplayIcon);
                if (icon.Length > 0 && InstallPaths.IsSameOrInside(icon, gameDir)) score = Math.Max(score, 80);
            }
            int nameScore = NameScore(e.DisplayName, gameName);
            if (nameScore > 0) score = score > 0 ? Math.Min(100, score + 5) : Math.Max(score, nameScore);
            return score;
        }

        /// <summary>Best matching entry (score ≥ MinScore) or null.</summary>
        public static UninstallEntry Best(IEnumerable<UninstallEntry> entries, string gameDir, string gameName)
            => Best(entries, gameDir, gameName, InstallPaths.IsForbiddenRoot);

        public static UninstallEntry Best(IEnumerable<UninstallEntry> entries, string gameDir, string gameName, Func<string, bool> isForbidden)
        {
            UninstallEntry best = null;
            int bestScore = MinScore - 1;
            foreach (UninstallEntry e in entries ?? Enumerable.Empty<UninstallEntry>())
            {
                int s = Score(e, gameDir, gameName, isForbidden);
                if (s > bestScore) { bestScore = s; best = e; }
            }
            return best;
        }

        /// <summary>"C:\x\game.exe,0" / "\"C:\x\game.exe\"" → "C:\x\game.exe".</summary>
        public static string IconPath(string displayIcon)
        {
            if (string.IsNullOrWhiteSpace(displayIcon)) return "";
            string s = displayIcon.Trim();
            if (s.StartsWith("\""))
            {
                int end = s.IndexOf('"', 1);
                s = end > 0 ? s.Substring(1, end - 1) : s.Trim('"');
            }
            else
            {
                int comma = s.LastIndexOf(',');
                if (comma > 2 && int.TryParse(s.Substring(comma + 1).Trim(), out _)) s = s.Substring(0, comma);
            }
            return InstallPaths.Normalize(s);
        }

        /// <summary>70 = same normalized name; 60 = entry name is the game name + version/edition noise. 0 otherwise.</summary>
        public static int NameScore(string displayName, string gameName)
        {
            string a = NormalizeName(displayName), b = NormalizeName(gameName);
            if (a.Length < 3 || b.Length < 3) return 0;
            if (a == b) return 70;
            if (a.StartsWith(b + " "))
            {
                string rest = a.Substring(b.Length + 1);
                string[] noise = { "version", "v", "edition", "remastered", "definitive", "goty", "game", "of", "the", "year", "complete", "x64", "x86", "64", "bit", "pc" };
                if (rest.Split(' ').All(w => w.All(char.IsDigit) || noise.Contains(w) || IsVersion(w))) return 60;
            }
            return 0;
        }

        private static bool IsVersion(string w) => w.Length > 1 && w[0] == 'v' && w.Substring(1).All(char.IsDigit);

        /// <summary>Lowercase, accents/™®© removed, punctuation → spaces, single spaces.</summary>
        public static string NormalizeName(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            string d = s.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(d.Length);
            foreach (char c in d)
            {
                var cat = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
                if (cat == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
                sb.Append(char.IsLetterOrDigit(c) && c < 128 ? char.ToLowerInvariant(c) : ' ');
            }
            return string.Join(" ", sb.ToString().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
        }
    }
}
