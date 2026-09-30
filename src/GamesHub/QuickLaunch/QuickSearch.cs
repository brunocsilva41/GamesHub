// Matching is accent/case-insensitive. Tiers (best first): exact name, name prefix, compact prefix
// ("halflife" → "Half-Life"), initials ("lmsh" → LEGO Marvel Super Heroes), every token is a word
// prefix, substring, tokens matching name/platform/collections, and finally an in-order subsequence.
// Within a tier, recently played / much played / favorite / running games get a small boost.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace GamesHub
{
    public sealed class QuickSearchHit
    {
        public Game Game;
        public int Score;
        /// <summary>Highlighted ranges in Game.Name as (start, length) pairs, sorted, non-overlapping.</summary>
        public List<int[]> Highlights = new List<int[]>();
    }

    /// <summary>Precomputed, folded view of a game used for matching.</summary>
    public sealed class QuickEntry
    {
        public Game Game;
        public string Folded = "";          // same length as Game.Name; non-alphanumerics → ' '
        public string Collapsed = "";       // words joined by single spaces
        public string Compact = "";         // words concatenated
        public int[] CompactMap;            // compact index → name index
        public List<Word> Words = new List<Word>();
        public List<string> Initials = new List<string>(); // "lmsh" (+ variant with roman numerals as digits)
        public List<string> Extra = new List<string>();    // folded platform + collection words

        public struct Word
        {
            public int Start;               // index in Game.Name
            public string Text;             // folded
            public string Alias;            // "5" for "v", "v" for "5" (roman numerals I–X), else null
        }
    }

    public static class QuickSearch
    {
        public const int MaxResults = 30;
        public const int MaxRecent = 12;

        private static readonly string[] Roman = { "", "i", "ii", "iii", "iv", "v", "vi", "vii", "viii", "ix", "x" };

        // ------------------------------------------------------------ folding

        /// <summary>Lowercases, strips diacritics and maps every non-alphanumeric char to ' '.
        /// Output has exactly the same length as the input (so indices map back to the original).</summary>
        public static string Fold(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s) sb.Append(FoldChar(c));
            return sb.ToString();
        }

        private static char FoldChar(char c)
        {
            if (c < 128)
            {
                if (c >= 'A' && c <= 'Z') return (char)(c + 32);
                return (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') ? c : ' ';
            }
            if (char.IsSurrogate(c)) return ' ';
            switch (c)
            {
                case 'ß': return 's';
                case 'æ': case 'Æ': return 'a';
                case 'ø': case 'Ø': return 'o';
                case 'œ': case 'Œ': return 'o';
                case 'ł': case 'Ł': return 'l';
                case 'đ': case 'Đ': return 'd';
            }
            string d = c.ToString().Normalize(NormalizationForm.FormD);
            char b = d.Length > 0 ? d[0] : c;
            if (CharUnicodeInfo.GetUnicodeCategory(b) == UnicodeCategory.NonSpacingMark) return ' ';
            b = char.ToLowerInvariant(b);
            return char.IsLetterOrDigit(b) ? b : ' ';
        }

        /// <summary>Folded query with runs of spaces collapsed and trimmed.</summary>
        public static string FoldQuery(string q) => string.Join(" ", Fold(q).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));

        // ------------------------------------------------------------ index

        public static List<QuickEntry> BuildIndex(IEnumerable<Game> games)
        {
            if (games == null) return new List<QuickEntry>();
            return games.Where(g => g != null).Select(BuildEntry).ToList();
        }

        public static QuickEntry BuildEntry(Game g)
        {
            string name = g.Name ?? "";
            string folded = Fold(name);
            var e = new QuickEntry { Game = g, Folded = folded };

            // Words: runs of alphanumerics, additionally split at letter/digit boundaries ("Left4Dead").
            int i = 0;
            while (i < folded.Length)
            {
                if (folded[i] == ' ') { i++; continue; }
                int start = i;
                bool digit = char.IsDigit(folded[i]);
                while (i < folded.Length && folded[i] != ' ' && char.IsDigit(folded[i]) == digit) i++;
                string text = folded.Substring(start, i - start);
                e.Words.Add(new QuickEntry.Word { Start = start, Text = text, Alias = RomanAlias(text) });
            }

            e.Collapsed = CollapseSpaces(folded);
            var compact = new StringBuilder();
            var map = new List<int>();
            for (int k = 0; k < folded.Length; k++)
                if (folded[k] != ' ') { compact.Append(folded[k]); map.Add(k); }
            e.Compact = compact.ToString();
            e.CompactMap = map.ToArray();

            string initials = new string(e.Words.Select(w => w.Text[0]).ToArray());
            e.Initials.Add(initials);
            if (e.Words.Any(w => w.Alias != null))
            {
                // Variant where roman numerals become digits and vice versa ("gta5" ↔ "Grand Theft Auto V").
                string alt = string.Concat(e.Words.Select(w => w.Alias != null && char.IsDigit(w.Alias[0]) != char.IsDigit(w.Text[0])
                    ? w.Alias : w.Text[0].ToString()));
                if (alt != initials) e.Initials.Add(alt);
            }

            foreach (string s in new[] { g.Platform }.Concat(g.Collections ?? new List<string>()))
                foreach (string w in Fold(s).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Where(w => !e.Extra.Contains(w)))
                    e.Extra.Add(w);   // Where is lazy, so words added earlier in this loop are seen too
            return e;
        }

        private static string RomanAlias(string w)
        {
            int idx = Array.IndexOf(Roman, w);
            if (idx > 0) return idx.ToString(CultureInfo.InvariantCulture);
            if (int.TryParse(w, NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n >= 1 && n <= 10 && w[0] != '0')
                return Roman[n];
            return null;
        }

        private static string CollapseSpaces(string s) => string.Join(" ", s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));

        // ------------------------------------------------------------ search

        /// <summary>Ranks the index for a query. Empty query → Recent().</summary>
        public static List<QuickSearchHit> Search(List<QuickEntry> index, string query, DateTime now, int max = MaxResults)
        {
            string q = FoldQuery(query);
            if (q.Length == 0) return Recent(index, now, MaxRecent);
            var hits = new List<QuickSearchHit>();
            foreach (QuickEntry e in index)
            {
                QuickSearchHit h = Match(e, q);
                if (h == null) continue;
                h.Score += Boost(e.Game, now);
                if (e.Game.Hidden) h.Score -= 300;
                hits.Add(h);
            }
            return hits.OrderByDescending(h => h.Score)
                       .ThenBy(h => h.Game.Name, StringComparer.CurrentCultureIgnoreCase)
                       .Take(max).ToList();
        }

        /// <summary>Running games first, then by last played; filled with favorites / most played. Hidden excluded.</summary>
        public static List<QuickSearchHit> Recent(List<QuickEntry> index, DateTime now, int max = MaxRecent)
        {
            return index.Select(e => e.Game).Where(g => !g.Hidden)
                .OrderByDescending(g => g.Running)
                .ThenByDescending(g => g.LastPlayed ?? DateTime.MinValue)
                .ThenByDescending(g => g.Favorite)
                .ThenByDescending(g => g.PlaySeconds)
                .ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
                .Take(max)
                .Select(g => new QuickSearchHit { Game = g, Score = 0 })
                .ToList();
        }

        /// <summary>Small usage-based boost (0..~60) so it reorders games within a tier, not across tiers.</summary>
        public static int Boost(Game g, DateTime now)
        {
            double b = 0;
            if (g.LastPlayed.HasValue)
            {
                double days = Math.Max(0, (now - g.LastPlayed.Value).TotalDays);
                b += 30.0 / (1.0 + days / 7.0);
            }
            if (g.PlaySeconds > 0) b += Math.Min(15.0, 5.0 * Math.Log10(1.0 + g.PlaySeconds / 3600.0) * 1.5);
            if (g.Favorite) b += 10;
            if (g.Running) b += 5;
            return (int)Math.Round(b);
        }

        /// <summary>Scores one entry against an already folded, collapsed query. null = no match.</summary>
        public static QuickSearchHit Match(QuickEntry e, string q)
        {
            if (e.Collapsed.Length == 0 || q.Length == 0) return null;
            string qc = q.Replace(" ", "");
            string[] tokens = q.Split(' ');
            var hit = new QuickSearchHit { Game = e.Game };

            if (e.Collapsed == q) { hit.Score = 1000; AddCompactRange(hit, e, 0, e.Compact.Length); return hit; }
            if (e.Collapsed.StartsWith(q, StringComparison.Ordinal) || e.Compact.StartsWith(qc, StringComparison.Ordinal))
            {
                hit.Score = (e.Collapsed.StartsWith(q, StringComparison.Ordinal) ? 900 : 860) - Math.Min(40, e.Compact.Length - qc.Length);
                AddCompactRange(hit, e, 0, qc.Length);
                return hit;
            }
            if (qc.Length >= 2)
            {
                foreach (string ini in e.Initials)
                {
                    if (ini == qc) { hit.Score = tokens.Length == 1 ? 820 : 800; HighlightWordStarts(hit, e, qc.Length); return hit; }
                }
                foreach (string ini in e.Initials)
                {
                    if (ini.StartsWith(qc, StringComparison.Ordinal))
                    {
                        hit.Score = 780 - Math.Min(30, (ini.Length - qc.Length) * 5);
                        HighlightWordStarts(hit, e, qc.Length);
                        return hit;
                    }
                }
            }
            List<int[]> wordRanges = MatchTokensAsWordPrefixes(e, tokens, out bool inOrder);
            if (wordRanges != null)
            {
                hit.Score = inOrder ? 700 : 650;
                hit.Highlights = Merge(wordRanges);
                return hit;
            }
            int pos = e.Collapsed.IndexOf(q, StringComparison.Ordinal);
            if (pos >= 0)
            {
                bool boundary = pos == 0 || e.Collapsed[pos - 1] == ' ';
                hit.Score = boundary ? 600 : 500;
                // Map collapsed position to compact position by counting non-space chars.
                int cpos = 0;
                for (int k = 0; k < pos; k++) if (e.Collapsed[k] != ' ') cpos++;
                AddCompactRange(hit, e, cpos, qc.Length);
                return hit;
            }
            int cp = e.Compact.IndexOf(qc, StringComparison.Ordinal);
            if (cp >= 0 && qc.Length >= 2)
            {
                hit.Score = 480;
                AddCompactRange(hit, e, cp, qc.Length);
                return hit;
            }
            // Every token matches a name word prefix, a platform/collection word, or is a name substring.
            var ranges = new List<int[]>();
            bool allTokens = true, anyInName = false;
            foreach (string t in tokens)
            {
                int[] r = FindWordPrefix(e, t, null);
                if (r != null) { ranges.Add(r); anyInName = true; continue; }
                int sub = t.Length >= 2 ? e.Folded.IndexOf(t, StringComparison.Ordinal) : -1;
                if (sub >= 0) { ranges.Add(new[] { sub, t.Length }); anyInName = true; continue; }
                if (e.Extra.Any(x => x.StartsWith(t, StringComparison.Ordinal))) continue;
                allTokens = false;
                break;
            }
            if (allTokens)
            {
                hit.Score = anyInName ? 420 : 400;
                hit.Highlights = Merge(ranges);
                return hit;
            }
            // In-order subsequence over the compact name, rewarding word starts and density.
            if (qc.Length >= 2)
            {
                var starts = new HashSet<int>(e.Words.Select(w => w.Start));
                int ci = 0, first = -1, last = -1, wordStartHits = 0;
                var idx = new List<int>();
                for (int k = 0; k < e.Compact.Length && ci < qc.Length; k++)
                {
                    if (e.Compact[k] != qc[ci]) continue;
                    if (first < 0) first = k;
                    last = k;
                    idx.Add(k);
                    if (starts.Contains(e.CompactMap[k])) wordStartHits++;
                    ci++;
                }
                if (ci == qc.Length)
                {
                    double density = (double)qc.Length / (last - first + 1);
                    hit.Score = 100 + (int)(150 * density) + Math.Min(50, wordStartHits * 10);
                    hit.Highlights = Merge(idx.Select(k => new[] { e.CompactMap[k], 1 }).ToList());
                    return hit;
                }
            }
            return null;
        }

        private static List<int[]> MatchTokensAsWordPrefixes(QuickEntry e, string[] tokens, out bool inOrder)
        {
            inOrder = true;
            var used = new HashSet<int>();
            var ranges = new List<int[]>();
            int lastWord = -1;
            foreach (string t in tokens)
            {
                int wi = -1;
                // Prefer the next word after the previous match (in-order), else any unused word.
                for (int k = lastWord + 1; k < e.Words.Count && wi < 0; k++)
                    if (!used.Contains(k) && WordMatches(e.Words[k], t)) wi = k;
                if (wi < 0)
                {
                    for (int k = 0; k < e.Words.Count && wi < 0; k++)
                        if (!used.Contains(k) && WordMatches(e.Words[k], t)) wi = k;
                    if (wi < 0) return null;
                    inOrder = false;
                }
                used.Add(wi);
                lastWord = wi;
                QuickEntry.Word w = e.Words[wi];
                ranges.Add(new[] { w.Start, w.Text.StartsWith(t, StringComparison.Ordinal) ? t.Length : w.Text.Length });
            }
            return ranges;
        }

        private static bool WordMatches(QuickEntry.Word w, string t)
            => w.Text.StartsWith(t, StringComparison.Ordinal) || (w.Alias != null && w.Alias == t);

        private static int[] FindWordPrefix(QuickEntry e, string t, HashSet<int> used)
        {
            for (int k = 0; k < e.Words.Count; k++)
                if ((used == null || !used.Contains(k)) && WordMatches(e.Words[k], t))
                    return new[] { e.Words[k].Start, e.Words[k].Text.StartsWith(t, StringComparison.Ordinal) ? t.Length : e.Words[k].Text.Length };
            return null;
        }

        private static void HighlightWordStarts(QuickSearchHit hit, QuickEntry e, int count)
        {
            var r = new List<int[]>();
            for (int k = 0; k < count && k < e.Words.Count; k++)
            {
                QuickEntry.Word w = e.Words[k];
                r.Add(new[] { w.Start, w.Alias != null && char.IsDigit(w.Text[0]) != char.IsDigit(w.Alias[0]) && w.Text.Length > 1 ? w.Text.Length : 1 });
            }
            hit.Highlights = Merge(r);
        }

        /// <summary>Highlights compact[start..start+len) mapped back to name positions.</summary>
        private static void AddCompactRange(QuickSearchHit hit, QuickEntry e, int start, int len)
        {
            var r = new List<int[]>();
            for (int k = start; k < start + len && k < e.CompactMap.Length; k++) r.Add(new[] { e.CompactMap[k], 1 });
            hit.Highlights = Merge(r);
        }

        /// <summary>Sorts and merges overlapping/adjacent (start,len) ranges.</summary>
        public static List<int[]> Merge(List<int[]> ranges)
        {
            var result = new List<int[]>();
            foreach (int[] r in ranges.Where(x => x[1] > 0).OrderBy(x => x[0]))
            {
                if (result.Count > 0)
                {
                    int[] last = result[result.Count - 1];
                    if (r[0] <= last[0] + last[1])
                    {
                        last[1] = Math.Max(last[1], r[0] + r[1] - last[0]);
                        continue;
                    }
                }
                result.Add(new[] { r[0], r[1] });
            }
            return result;
        }
    }
}
