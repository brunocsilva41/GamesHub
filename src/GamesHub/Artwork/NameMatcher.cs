using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace GamesHub
{
    /// <summary>A search hit from Steam (or SteamGridDB) used by the matcher.</summary>
    public sealed class MatchCandidate
    {
        public string AppId = "";
        public string Name = "";
        public string IconUrl = "";
        public MatchCandidate() { }
        public MatchCandidate(string appId, string name, string iconUrl = "") { AppId = appId; Name = name; IconUrl = iconUrl ?? ""; }
        public override string ToString() => Name + " (" + AppId + ")";
    }

    /// <summary>
    /// Fuzzy game-name matching: normalization (case, symbols, noise such as "DirectX 11" or
    /// "(Install Crack)", edition words, roman numerals) and a token-set similarity with a
    /// conservative acceptance threshold. Pure functions — no I/O.
    /// </summary>
    public static class NameMatcher
    {
        /// <summary>Minimum score for a candidate to be accepted.</summary>
        public const double Threshold = 0.8;

        private static readonly HashSet<string> StopWords = new HashSet<string> { "the", "of", "a", "an", "and" };

        private static readonly HashSet<string> EditionQualifiers = new HashSet<string>
        {
            "definitive", "complete", "deluxe", "ultimate", "gold", "standard", "special", "enhanced",
            "anniversary", "collectors", "premium", "digital", "legendary", "legacy",
        };

        /// <summary>Candidate words that indicate a non-game product (ignored unless the query has them too).</summary>
        private static readonly HashSet<string> NonGameWords = new HashSet<string>
        {
            "soundtrack", "ost", "demo", "playtest", "artbook", "dlc", "sdk", "server", "trailer",
            "wallpaper", "wallpapers", "benchmark", "editor", "tool", "tools", "prologue",
        };

        private static readonly HashSet<string> NonGameNames = new HashSet<string>
        {
            "steam", "epic games", "epic games launcher", "roblox studio",
            "battle net", "battlenet", "ea", "ea app", "origin", "ubisoft connect", "uplay", "gog galaxy",
            "riot client", "discord", "xbox", "xbox app", "hydra", "plutonium", "tlauncher", "lunar client",
            "playnite", "overwolf", "geforce experience", "nvidia app", "msi afterburner", "obs studio",
            "rockstar games launcher", "amazon games", "itch", "heroic", "lutris", "legendary",
        };

        private static readonly HashSet<string> NonGameTokens = new HashSet<string>
        {
            "setup", "install", "installer", "uninstall", "uninstaller", "unins000", "updater", "launcher",
            "crashreporter", "redist", "vcredist", "directx", "dxsetup",
        };

        /// <summary>Platforms whose games are never sold on Steam (skip the search entirely).</summary>
        private static readonly HashSet<string> NonSteamPlatforms = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Riot", "Roblox", "Minecraft",
        };

        // ------------------------------------------------------------ normalization

        /// <summary>Lowercase tokens joined by single spaces, noise removed, roman numerals → digits.</summary>
        public static string Normalize(string name) => string.Join(" ", Clean(name, true));

        /// <summary>A readable query for Steam's search (noise removed, numerals kept as typed).</summary>
        public static string SearchQuery(string name) => string.Join(" ", Clean(name, false));

        /// <summary>Same as <see cref="SearchQuery"/> but with digits 2..20 written as roman numerals
        /// (Steam's search does not treat "3" and "III" as equal).</summary>
        public static string RomanQuery(string name)
        {
            List<string> t = Clean(name, false);
            bool changed = false;
            for (int i = 0; i < t.Count; i++)
                if (int.TryParse(t[i], out int n) && n >= 2 && n <= 20) { t[i] = ToRoman(n).ToLowerInvariant(); changed = true; }
            return changed ? string.Join(" ", t) : "";
        }

        /// <summary>Shorter fallback query (last word dropped) for when Steam finds nothing at all
        /// for the full name, e.g. renamed titles ("... Siege X"). "" when too short.</summary>
        public static string ShortQuery(string name)
        {
            List<string> t = Clean(name, false);
            return t.Count >= 3 ? string.Join(" ", t.Take(t.Count - 1)) : "";
        }

        private static List<string> Clean(string name, bool romanToDigits)
        {
            string s = RemoveDiacritics(name ?? "").ToLowerInvariant().Trim();
            s = Regex.Replace(s, "[™®©℠]", " ");
            s = Regex.Replace(s, @"\s*-\s*(atalho|shortcut|copy|c[oó]pia)\s*$", "");
            s = Regex.Replace(s, @"\.(exe|lnk|url)$", "");
            s = Regex.Replace(s, @"\([^)]*\)|\[[^\]]*\]|\{[^}]*\}", " ");
            s = s.Replace("&", " and ");
            s = Regex.Replace(s, "['’`´]", "");
            s = Regex.Replace(s, @"[^\p{L}\p{Nd}]+", " ");
            List<string> tokens = s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            tokens = RemoveNoise(tokens);
            // "Play Raft" style shortcut names: a leading "play" is not part of the title.
            if (tokens.Count >= 2 && tokens[0] == "play") tokens.RemoveAt(0);
            if (romanToDigits)
                for (int i = 0; i < tokens.Count; i++)
                {
                    // A lone "x" is usually a letter ("Mega Man X", "Siege X"), not the number 10.
                    int n = tokens[i] == "x" ? 0 : RomanToInt(tokens[i]);
                    if (n >= 2) tokens[i] = n.ToString(CultureInfo.InvariantCulture);
                }
            return tokens;
        }

        private static List<string> RemoveNoise(List<string> t)
        {
            var o = new List<string>(t.Count);
            for (int i = 0; i < t.Count; i++)
            {
                string w = t[i];
                string next = i + 1 < t.Count ? t[i + 1] : "";
                // DirectX / DX / architecture markers: "directx 11", "dx12", "directx11", "x64", "64 bit"
                if ((w == "directx" || w == "dx") && Regex.IsMatch(next, @"^\d+$")) { i++; continue; }
                if (Regex.IsMatch(w, @"^(directx|dx)\d+$") || w == "x64" || w == "x86" || w == "64bit" || w == "32bit") continue;
                if ((w == "64" || w == "32") && next == "bit") { i++; continue; }
                // Edition noise: "game of the year edition", "goty", "<qualifier> edition", "edition"
                if (w == "game" && next == "of" && i + 3 < t.Count && t[i + 2] == "the" && t[i + 3] == "year")
                {
                    i += 3;
                    if (i + 1 < t.Count && t[i + 1] == "edition") i++;
                    continue;
                }
                if (w == "goty") continue;
                if (EditionQualifiers.Contains(w) && next == "edition") { i++; continue; }
                if (w == "edition") continue;
                o.Add(w);
            }
            return o;
        }

        private static string RemoveDiacritics(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char c in s.Normalize(NormalizationForm.FormD))
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        // ------------------------------------------------------------ roman numerals

        /// <summary>Parses a canonical roman numeral 1..39 ("i".."xxxix"); returns 0 otherwise.</summary>
        public static int RomanToInt(string token)
        {
            if (string.IsNullOrEmpty(token) || token.Length > 6) return 0;
            string t = token.ToLowerInvariant();
            if (!Regex.IsMatch(t, "^x{0,3}(ix|iv|v?i{0,3})$")) return 0;
            int total = 0;
            for (int i = 0; i < t.Length; i++)
            {
                int v = t[i] == 'x' ? 10 : t[i] == 'v' ? 5 : 1;
                int nextV = i + 1 < t.Length ? (t[i + 1] == 'x' ? 10 : t[i + 1] == 'v' ? 5 : 1) : 0;
                total += v < nextV ? -v : v;
            }
            return total;
        }

        public static string ToRoman(int n)
        {
            if (n <= 0 || n >= 40) return n.ToString(CultureInfo.InvariantCulture);
            string[] ones = { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX" };
            return new string('X', n / 10) + ones[n % 10];
        }

        // ------------------------------------------------------------ similarity

        /// <summary>Meaningful tokens (stop words removed) of an already normalized name.</summary>
        public static HashSet<string> Tokens(string normalized)
            => new HashSet<string>((normalized ?? "").Split(' ').Where(w => w.Length > 0 && !StopWords.Contains(w)));

        private static bool IsNumber(string w) => w.All(char.IsDigit);

        /// <summary>
        /// Similarity in 0..1 between two normalized names.
        /// 1.0 = same name ignoring spaces; 0 when the numbers differ (sequels must not match);
        /// query fully contained in the candidate scores 0.5 + 0.5·precision (subtitles tolerated);
        /// otherwise Jaccard similarity of the token sets.
        /// </summary>
        public static double Score(string queryNorm, string candNorm)
        {
            if (string.IsNullOrEmpty(queryNorm) || string.IsNullOrEmpty(candNorm)) return 0;
            if (queryNorm.Replace(" ", "") == candNorm.Replace(" ", "")) return 1.0;
            HashSet<string> a = Tokens(queryNorm), b = Tokens(candNorm);
            if (a.Count == 0 || b.Count == 0) return 0;
            if (!new HashSet<string>(a.Where(IsNumber)).SetEquals(b.Where(IsNumber))) return 0;
            int inter = a.Count(b.Contains);
            if (inter == 0) return 0;
            double jaccard = (double)inter / (a.Count + b.Count - inter);
            if (inter == a.Count && a.Count >= 2)
            {
                double precision = (double)inter / b.Count;
                return Math.Max(jaccard, 0.5 + 0.5 * precision);
            }
            return jaccard;
        }

        /// <summary>True when the candidate looks like a soundtrack/demo/tool the query did not ask for.</summary>
        public static bool IsExcludedCandidate(string queryNorm, string candNorm)
        {
            HashSet<string> q = Tokens(queryNorm);
            HashSet<string> c = Tokens(candNorm);
            if (c.Any(w => NonGameWords.Contains(w) && !q.Contains(w))) return true;
            return c.Contains("season") && c.Contains("pass") && !q.Contains("pass");
        }

        /// <summary>
        /// Picks the confident match among search results (kept in search-rank order), or null.
        /// Ties: several exact names → the best-ranked one; tied non-exact scores → ambiguous → null.
        /// </summary>
        public static MatchCandidate PickBest(string name, IEnumerable<MatchCandidate> candidates, out double score)
        {
            score = 0;
            string q = Normalize(name);
            if (q.Length == 0 || candidates == null) return null;
            var scored = new List<KeyValuePair<MatchCandidate, double>>();
            foreach (MatchCandidate c in candidates)
            {
                if (c == null || string.IsNullOrEmpty(c.AppId)) continue;
                string n = Normalize(c.Name);
                if (IsExcludedCandidate(q, n)) continue;
                double s = Score(q, n);
                if (s >= Threshold) scored.Add(new KeyValuePair<MatchCandidate, double>(c, s));
            }
            if (scored.Count == 0) return null;
            double best = scored.Max(kv => kv.Value);
            var top = scored.Where(kv => Math.Abs(kv.Value - best) < 1e-9).Select(kv => kv.Key).ToList();
            bool distinct = top.Select(c => c.AppId).Distinct().Count() > 1;
            if (distinct && best < 1.0) return null;
            score = best;
            return top[0];
        }

        public static MatchCandidate PickBest(string name, IEnumerable<MatchCandidate> candidates)
            => PickBest(name, candidates, out _);

        // ------------------------------------------------------------ non-games

        /// <summary>Launchers, stores, installers and tools that should never be searched on Steam.</summary>
        /// <summary>Platforms whose games are never on Steam (skip Steam matching; SteamGridDB still applies).</summary>
        public static bool IsNonSteamPlatform(string platform) => platform != null && NonSteamPlatforms.Contains(platform);

        public static bool IsLikelyNonGame(string name, string platform = null)
        {
            if (platform != null && NonSteamPlatforms.Contains(platform)) return true;
            string n = Normalize(name);
            if (n.Length == 0) return true;
            if (NonGameNames.Contains(n)) return true;
            foreach (string w in n.Split(' '))
                if (NonGameTokens.Contains(w) || (w.Length > 8 && w.EndsWith("launcher", StringComparison.Ordinal)))
                    return true;
            return false;
        }
    }
}
