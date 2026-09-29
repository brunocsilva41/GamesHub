using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace GamesHub
{
    /// <summary>One save/config location as written on the wiki (unexpanded).</summary>
    public sealed class PcgwRow
    {
        public string Kind = "";      // "save" | "config"
        public string Platform = "";  // "Windows" | "Steam"
        public string Raw = "";       // e.g. {{p|appdata}}\Foo\saves\
    }

    public static class PcgwWikitext
    {
        private static readonly Regex Comments = new Regex(@"<!--.*?(-->|$)", RegexOptions.Singleline);
        private static readonly Regex RefBlocks = new Regex(@"<ref\b[^>/]*>.*?</ref\s*>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        private static readonly Regex RefSelfClosing = new Regex(@"<ref\b[^>]*/>", RegexOptions.IgnoreCase);
        private static readonly Regex LineBreaks = new Regex(@"<br\s*/?>", RegexOptions.IgnoreCase);
        private static readonly Regex HtmlTags = new Regex(@"</?[a-zA-Z][^<>]*>");
        private static readonly Regex RowStart = new Regex(@"\{\{\s*Game[ _]data/(saves|config)\s*\|", RegexOptions.IgnoreCase);
        private static readonly Regex DrivePath = new Regex(@"^[A-Za-z]:\\");

        /// <summary>Removes comments, &lt;ref&gt;s and HTML tags (&lt;br&gt; becomes a path separator).</summary>
        public static string Clean(string wikitext)
        {
            string s = wikitext ?? "";
            s = Comments.Replace(s, "");
            s = RefBlocks.Replace(s, "");
            s = RefSelfClosing.Replace(s, "");
            s = LineBreaks.Replace(s, "|");
            s = HtmlTags.Replace(s, "");
            return s;
        }

        /// <summary>All Windows and Steam save/config rows, in page order, de-duplicated per kind.</summary>
        public static List<PcgwRow> ExtractRows(string wikitext)
        {
            var rows = new List<PcgwRow>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string s = Clean(wikitext);
            foreach (Match m in RowStart.Matches(s))
            {
                int start = m.Index + 2; // after "{{"
                int end = FindTemplateEnd(s, m.Index);
                if (end < 0) continue;
                List<string> parts = SplitTopLevel(s.Substring(start, end - start));
                if (parts.Count < 3) continue;
                string kind = m.Groups[1].Value.Equals("saves", StringComparison.OrdinalIgnoreCase) ? "save" : "config";
                string platform = NormalizePlatform(parts[1]);
                if (platform == null) continue;
                for (int i = 2; i < parts.Count; i++)
                {
                    string raw = parts[i].Trim();
                    if (!LooksLikePath(raw)) continue;
                    if (seen.Add(kind + "|" + raw))
                        rows.Add(new PcgwRow { Kind = kind, Platform = platform, Raw = raw });
                }
            }
            return rows;
        }

        private static string NormalizePlatform(string p)
        {
            string t = (p ?? "").Trim();
            if (t.Equals("Windows", StringComparison.OrdinalIgnoreCase)) return "Windows";
            if (t.Equals("Steam", StringComparison.OrdinalIgnoreCase)) return "Steam";
            return null; // Microsoft Store, OS X, Linux, Steam Play (Linux), ...
        }

        public static bool LooksLikePath(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return false;
            return raw.StartsWith("{{", StringComparison.Ordinal) && raw.IndexOf("{{p|", StringComparison.OrdinalIgnoreCase) >= 0
                || DrivePath.IsMatch(raw);
        }

        /// <summary>Index of the "}}" that closes the template opened at <paramref name="open"/>, or -1.</summary>
        public static int FindTemplateEnd(string s, int open)
        {
            int depth = 0;
            for (int i = open; i < s.Length - 1; i++)
            {
                if (s[i] == '{' && s[i + 1] == '{') { depth++; i++; }
                else if (s[i] == '}' && s[i + 1] == '}')
                {
                    depth--;
                    if (depth == 0) return i;
                    i++;
                }
            }
            return -1;
        }

        /// <summary>Splits template content on '|' that are not inside nested {{…}} or [[…]].</summary>
        public static List<string> SplitTopLevel(string content)
        {
            var parts = new List<string>();
            var sb = new StringBuilder();
            int braces = 0, links = 0;
            for (int i = 0; i < content.Length; i++)
            {
                char c = content[i];
                char n = i + 1 < content.Length ? content[i + 1] : '\0';
                if (c == '{' && n == '{') { braces++; sb.Append("{{"); i++; continue; }
                if (c == '}' && n == '}' && braces > 0) { braces--; sb.Append("}}"); i++; continue; }
                if (c == '[' && n == '[') { links++; sb.Append("[["); i++; continue; }
                if (c == ']' && n == ']' && links > 0) { links--; sb.Append("]]"); i++; continue; }
                if (c == '|' && braces == 0 && links == 0) { parts.Add(sb.ToString()); sb.Clear(); continue; }
                sb.Append(c);
            }
            parts.Add(sb.ToString());
            return parts;
        }
    }
}
