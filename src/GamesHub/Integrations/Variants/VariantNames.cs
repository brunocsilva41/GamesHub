// name ("LEGO Marvel Super Heroes 2") and a launch qualifier ("DirectX 11").
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace GamesHub
{
    public static class VariantNames
    {
        private sealed class Rule
        {
            public Regex Rx;
            /// <summary>Returns the canonical label for the match; null = noise (stripped, no label).</summary>
            public Func<Match, string> Label;
        }

        private const RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        // A qualifier must be separated from the base name (so "SKlauncher" stays intact).
        private const string Sep = @"(?:^|[\s\-–—:_,/|]+)";

        // Core qualifier vocabulary (without anchors) -> canonical label.
        private static readonly KeyValuePair<string, Func<Match, string>>[] Vocabulary =
        {
            Kv(@"(?:directx|direct\s*x|dx|d3d)\s*-?\s*(?<n>9|10|11|12)", m => "DirectX " + m.Groups["n"].Value),
            Kv(@"vulkan", m => "Vulkan"),
            Kv(@"open\s*gl", m => "OpenGL"),
            Kv(@"(?<n>32|64)\s*-?\s*bits?", m => m.Groups["n"].Value + "-bit"),
            Kv(@"x86", m => "x86"),
            Kv(@"x64", m => "x64"),
            Kv(@"safe\s*mode|modo\s+seguro", m => "Modo seguro"),
            Kv(@"launcher", m => "Launcher"),
            Kv(@"multi\s*-?\s*player|multijogador|mp", m => "Multijogador"),
            Kv(@"single\s*-?\s*player|um\s+jogador|sp", m => "Um jogador"),
            Kv(@"zombies?", m => "Zombies"),
            Kv(@"vr", m => "VR"),
            Kv(@"beta", m => "Beta"),
            Kv(@"legacy", m => "Legacy"),
            Kv(@"classic", m => "Clássico"),
        };

        private static readonly Rule[] Rules = BuildRules();

        // Parenthetical contents that describe how the copy was obtained, not a launch mode.
        private static readonly Regex NoiseParen = new Regex(
            @"\b(?:install|crack(?:ed)?|repack|portable|fitgirl|dodi|elamigos|codex|skidrow|empress|plaza|" +
            @"gog|steam|epic|atalho|shortcut|c[óo]pia|copy|update|build|v\d[\w.]*)\b", Opt);
        private static readonly Regex Paren = new Regex(@"\s*[\(\[\{](?<in>[^\(\)\[\]\{\}]*)[\)\]\}]\s*$", Opt);

        // Words that make a trailing token part of the title ("Plants vs. Zombies").
        private static readonly HashSet<string> Connectors = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "vs", "versus", "of", "the", "and", "a", "an", "de", "do", "da", "dos", "das", "e", "&", "+" };

        private static KeyValuePair<string, Func<Match, string>> Kv(string p, Func<Match, string> f)
            => new KeyValuePair<string, Func<Match, string>>(p, f);

        private static Rule[] BuildRules()
        {
            var list = new List<Rule>
            {
                // Noise first: file extensions and Windows "- Atalho" / "- Shortcut" suffixes.
                new Rule { Rx = new Regex(@"\.(?:exe|lnk|url|bat|cmd)\s*$", Opt), Label = null },
                new Rule { Rx = new Regex(@"[\s\-–—]+(?:atalho|shortcut|c[óo]pia|copy)(?:\s*\(\d+\))?\s*$", Opt), Label = null },
            };
            foreach (var kv in Vocabulary)
                list.Add(new Rule { Rx = new Regex(Sep + "(?:" + kv.Key + @")\s*$", Opt), Label = kv.Value });
            return list.ToArray();
        }

        private static string MatchWholeVocabulary(string text)
        {
            string t = (text ?? "").Trim();
            foreach (var kv in Vocabulary)
            {
                Match m = Regex.Match(t, "^(?:" + kv.Key + @")$", Opt);
                if (m.Success) return kv.Value(m);
            }
            return null;
        }

        /// <summary>Splits a display name into base name + qualifier labels (in reading order).</summary>
        public static string Parse(string name, out List<string> qualifiers)
        {
            qualifiers = new List<string>();
            string cur = (name ?? "").Trim();
            for (int guard = 0; guard < 12; guard++)
            {
                string next = null, label = null;

                Match pm = Paren.Match(cur);
                if (pm.Success && pm.Index > 0)
                {
                    string inner = pm.Groups["in"].Value.Trim();
                    next = cur.Substring(0, pm.Index);
                    label = MatchWholeVocabulary(inner);
                    if (label == null && inner.Length > 0 && !NoiseParen.IsMatch(inner)) label = inner;
                }
                else
                {
                    foreach (Rule r in Rules)
                    {
                        Match m = r.Rx.Match(cur);
                        if (!m.Success || m.Index == 0) continue;
                        next = cur.Substring(0, m.Index);
                        label = r.Label?.Invoke(m);
                        break;
                    }
                }

                if (next == null) break;
                next = next.TrimEnd(' ', '\t', '-', '–', '—', ':', '_', ',', '/', '|');
                if (Normalize(next).Length < 2) break;                       // never strip to nothing
                string lastWord = next.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                                      .LastOrDefault()?.TrimEnd('.') ?? "";
                if (label != null && Connectors.Contains(lastWord)) break;    // "Plants vs. Zombies"
                cur = next;
                if (!string.IsNullOrEmpty(label)) qualifiers.Insert(0, label);
            }
            return cur.Trim();
        }

        public static string BaseName(string name) => Parse(name, out _);

        public static string Qualifier(string name)
        {
            Parse(name, out List<string> q);
            return string.Join(" ", q.Distinct(StringComparer.OrdinalIgnoreCase));
        }

        /// <summary>Case/accent/punctuation-insensitive comparison key.</summary>
        public static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string d = s.Replace("&", " and ").Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(d.Length);
            bool space = false;
            foreach (char c in d)
            {
                UnicodeCategory cat = CharUnicodeInfo.GetUnicodeCategory(c);
                if (cat == UnicodeCategory.NonSpacingMark) continue;
                if (c == '®' || c == '™' || c == '©' || c == '\'' || c == '’') continue;
                if (char.IsLetterOrDigit(c)) { sb.Append(char.ToLowerInvariant(c)); space = false; }
                else if (!space && sb.Length > 0) { sb.Append(' '); space = true; }
            }
            return sb.ToString().Trim();
        }

        public static string BaseKey(string name) => Normalize(BaseName(name));
    }
}
