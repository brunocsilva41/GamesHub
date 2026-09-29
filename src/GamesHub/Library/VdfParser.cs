// OWNER: LIB agent. Tolerant Valve KeyValues (VDF/ACF text) parser.
using System;
using System.Collections.Generic;
using System.Text;

namespace GamesHub
{
    /// <summary>A KeyValues object. Values are either string or VdfNode. Keys are case-insensitive;
    /// duplicate keys keep the first value.</summary>
    internal sealed class VdfNode
    {
        public readonly Dictionary<string, object> Items = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> Keys = new List<string>();   // insertion order

        public string Str(string key, string def = "") => Items.TryGetValue(key, out object v) && v is string s ? s : def;
        public VdfNode Node(string key) => Items.TryGetValue(key, out object v) ? v as VdfNode : null;

        internal void Add(string key, object value)
        {
            if (Items.ContainsKey(key)) return;
            Items[key] = value;
            Keys.Add(key);
        }
    }

    internal static class VdfParser
    {
        /// <summary>Parses text into a root node (containing e.g. "AppState" → node). Never throws on malformed
        /// input: it returns whatever was parsed before the problem.</summary>
        public static VdfNode Parse(string text)
        {
            var root = new VdfNode();
            var stack = new Stack<VdfNode>();
            stack.Push(root);
            int pos = 0;
            string pendingKey = null;
            text = text ?? "";
            while (true)
            {
                Token t = Next(text, ref pos);
                if (t.Kind == TokenKind.End) break;
                VdfNode cur = stack.Peek();
                switch (t.Kind)
                {
                    case TokenKind.Open:
                        var child = new VdfNode();
                        cur.Add(pendingKey ?? "", child);
                        stack.Push(child);
                        pendingKey = null;
                        break;
                    case TokenKind.Close:
                        pendingKey = null;
                        if (stack.Count > 1) stack.Pop();
                        break;
                    case TokenKind.Condition:
                        break; // "[$WIN32]" platform conditionals: ignored (value is kept)
                    default:
                        if (pendingKey == null) pendingKey = t.Text;
                        else { cur.Add(pendingKey, t.Text); pendingKey = null; }
                        break;
                }
            }
            return root;
        }

        private enum TokenKind { End, String, Open, Close, Condition }
        private struct Token { public TokenKind Kind; public string Text; }

        private static Token Next(string s, ref int i)
        {
            while (i < s.Length)
            {
                char c = s[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }
                if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
                {
                    while (i < s.Length && s[i] != '\n') i++;
                    continue;
                }
                if (c == '{') { i++; return new Token { Kind = TokenKind.Open }; }
                if (c == '}') { i++; return new Token { Kind = TokenKind.Close }; }
                if (c == '[')
                {
                    int end = s.IndexOf(']', i);
                    i = end < 0 ? s.Length : end + 1;
                    return new Token { Kind = TokenKind.Condition };
                }
                if (c == '"') return new Token { Kind = TokenKind.String, Text = ReadQuoted(s, ref i) };
                int start = i;
                while (i < s.Length && !char.IsWhiteSpace(s[i]) && s[i] != '{' && s[i] != '}' && s[i] != '"') i++;
                return new Token { Kind = TokenKind.String, Text = s.Substring(start, i - start) };
            }
            return new Token { Kind = TokenKind.End };
        }

        private static string ReadQuoted(string s, ref int i)
        {
            var sb = new StringBuilder();
            i++; // opening quote
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c == '\\' && i < s.Length)
                {
                    char e = s[i++];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': sb.Append('\r'); break;
                        case '\\': sb.Append('\\'); break;
                        case '"': sb.Append('"'); break;
                        default: sb.Append('\\').Append(e); break;
                    }
                }
                else sb.Append(c);
            }
            return sb.ToString(); // unterminated: tolerate
        }
    }
}
