using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace GamesHub
{
    public static class PcgwNames
    {
        private static readonly string[] EditionSuffixes =
        {
            "game of the year edition", "goty edition", "goty", "definitive edition", "deluxe edition",
            "complete edition", "ultimate edition", "gold edition", "standard edition", "digital deluxe edition",
        };

        /// <summary>Lowercase, no accents/trademarks/punctuation, "&amp;" → "and", single spaces.</summary>
        public static string Normalize(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            string s = name.Replace("™", "").Replace("®", "").Replace("©", "").Replace("&", " and ")
                           .Replace("'", "").Replace("’", "").Replace("`", "");
            s = s.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                UnicodeCategory cat = CharUnicodeInfo.GetUnicodeCategory(c);
                if (cat == UnicodeCategory.NonSpacingMark) continue;
                sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
            }
            return string.Join(" ", sb.ToString().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
        }

        /// <summary>Name used for the wiki search URL: trademarks and trailing "(...)" tags removed.</summary>
        public static string CleanForSearch(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            string s = name.Replace("™", "").Replace("®", "").Replace("©", "").Trim();
            // "Game (DirectX 11)" / "Game [Beta]" → "Game"
            while (s.Length > 0 && (s.EndsWith(")") || s.EndsWith("]")))
            {
                int open = s.LastIndexOfAny(new[] { '(', '[' });
                if (open <= 0) break;
                s = s.Substring(0, open).TrimEnd();
            }
            return string.Join(" ", s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
        }

        /// <summary>Normalized query variants, most specific first (exact, without "(...)", without edition suffix).</summary>
        public static List<string> QueryVariants(string name)
        {
            var list = new List<string>();
            void Add(string v) { v = Normalize(v); if (v.Length > 0 && !list.Contains(v)) list.Add(v); }
            Add(name);
            string clean = CleanForSearch(name);
            Add(clean);
            string n = Normalize(clean);
            foreach (string suf in EditionSuffixes)
                if (n.EndsWith(" " + suf)) { Add(n.Substring(0, n.Length - suf.Length - 1)); break; }
            return list;
        }

        /// <summary>Title without its subtitle ("A: B" / "A - B" → "A"), normalized; "" when there is none.</summary>
        public static string NormalizeWithoutSubtitle(string title)
        {
            if (string.IsNullOrEmpty(title)) return "";
            int cut = title.IndexOf(':');
            int dash = title.IndexOf(" - ", StringComparison.Ordinal);
            if (dash > 0 && (cut < 0 || dash < cut)) cut = dash;
            return cut > 0 ? Normalize(title.Substring(0, cut)) : "";
        }

        /// <summary>Picks the best title for a game name, or null. Conservative: exact normalized match first;
        /// otherwise a UNIQUE title whose main part (before ':' / ' - ') equals the query (query ≥ 2 words).</summary>
        public static string BestMatch(string name, IEnumerable<string> titles)
        {
            List<string> all = titles?.Where(t => !string.IsNullOrEmpty(t)).Distinct().ToList() ?? new List<string>();
            if (all.Count == 0) return null;
            List<string> variants = QueryVariants(name);
            foreach (string q in variants)
            {
                string exact = all.FirstOrDefault(t => Normalize(t) == q);
                if (exact != null) return exact;
            }
            foreach (string q in variants)
            {
                if (q.Split(' ').Length < 2) continue;
                List<string> sub = all.Where(t => NormalizeWithoutSubtitle(t) == q).ToList();
                if (sub.Count == 1) return sub[0];
            }
            return null;
        }
    }
}
