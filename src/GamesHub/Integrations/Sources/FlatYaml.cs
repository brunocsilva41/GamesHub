// product_settings.yaml). Nested maps, lists and multi-line values are ignored.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace GamesHub
{
    public static class FlatYaml
    {
        /// <summary>Returns top-level "key: scalar" pairs. Quotes are removed; keys without a scalar value
        /// (nested maps / lists) are omitted. Never throws.</summary>
        public static Dictionary<string, string> Parse(string text)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(text)) return result;
            // Only top-level "key: value" lines: skip blanks, nested/indented lines, comments, list items and
            // document markers ("---" / "...").
            foreach (string line in text.Split('\n').Select(raw => raw.TrimEnd('\r'))
                                        .Where(l => l.Length > 0 && " \t#-".IndexOf(l[0]) < 0 && !l.StartsWith("...")))
            {
                int colon = FindKeyColon(line);
                if (colon <= 0) continue;
                string key = Unquote(line.Substring(0, colon).Trim());
                string value = line.Substring(colon + 1).Trim();
                if (key.Length == 0 || value.Length == 0) continue;           // nested block follows
                if (value == "{}" || value == "[]") continue;
                result[key] = ParseScalar(value);
            }
            return result;
        }

        // First ':' that is followed by space/end and is not inside quotes.
        private static int FindKeyColon(string line)
        {
            char quote = '\0';
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quote != '\0')
                {
                    if (c == quote) quote = '\0';
                    continue;
                }
                if (c == '"' || c == '\'') { quote = c; continue; }
                if (c == ':' && (i + 1 == line.Length || line[i + 1] == ' ' || line[i + 1] == '\t')) return i;
            }
            return -1;
        }

        private static string ParseScalar(string v)
        {
            if (v.Length >= 1 && v[0] == '"')
            {
                var sb = new StringBuilder();
                for (int i = 1; i < v.Length; i++)
                {
                    char c = v[i];
                    if (c == '"') break;
                    if (c == '\\' && i + 1 < v.Length)
                    {
                        char n = v[++i];
                        switch (n)
                        {
                            case 'n': sb.Append('\n'); break;
                            case 't': sb.Append('\t'); break;
                            case '"': sb.Append('"'); break;
                            case '\\': sb.Append('\\'); break;
                            case '/': sb.Append('/'); break;
                            default: sb.Append('\\').Append(n); break;
                        }
                        continue;
                    }
                    sb.Append(c);
                }
                return sb.ToString();
            }
            if (v.Length >= 1 && v[0] == '\'')
            {
                int end = v.IndexOf('\'', 1);
                while (end > 0 && end + 1 < v.Length && v[end + 1] == '\'') end = v.IndexOf('\'', end + 2);
                string inner = end > 0 ? v.Substring(1, end - 1) : v.Substring(1);
                return inner.Replace("''", "'");
            }
            int hash = v.IndexOf(" #", StringComparison.Ordinal);
            if (hash >= 0) v = v.Substring(0, hash).TrimEnd();
            return v;
        }

        private static string Unquote(string s)
        {
            if (s.Length >= 2 && (s[0] == '"' || s[0] == '\'') && s[s.Length - 1] == s[0]) return s.Substring(1, s.Length - 2);
            return s;
        }
    }
}
