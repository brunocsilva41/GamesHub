// OWNER: STEAMDATA agent. Small tolerant Valve KeyValues (VDF/ACF text) reader, private to this module.
// Streaming tokenizer over a TextReader: callers can materialize the whole tree (small files) or only
// one subtree by key path (localconfig.vdf can be several MB) while every other block is skipped.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace GamesHub
{
    /// <summary>A KeyValues block. Keys are case-insensitive; on duplicates the last one wins.</summary>
    public sealed class SteamKv
    {
        public readonly Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, SteamKv> Children = new Dictionary<string, SteamKv>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Child keys in file order (values and blocks), first occurrence only.</summary>
        public readonly List<string> Order = new List<string>();

        public string Str(string key, string def = "") => key != null && Values.TryGetValue(key, out string v) ? v : def;

        public long Long(string key, long def = 0)
            => long.TryParse(Str(key, null), System.Globalization.NumberStyles.Integer,
                             System.Globalization.CultureInfo.InvariantCulture, out long v) ? v : def;

        public SteamKv Node(string key) => key != null && Children.TryGetValue(key, out SteamKv n) ? n : null;

        /// <summary>Walks nested blocks: Path("a","b") == Node("a")?.Node("b").</summary>
        public SteamKv Path(params string[] keys)
        {
            SteamKv cur = this;
            foreach (string k in keys) { cur = cur?.Node(k); if (cur == null) return null; }
            return cur;
        }

        internal void SetValue(string key, string value)
        {
            if (!Values.ContainsKey(key) && !Children.ContainsKey(key)) Order.Add(key);
            Values[key] = value;
        }

        internal void SetChild(string key, SteamKv child)
        {
            if (!Values.ContainsKey(key) && !Children.ContainsKey(key)) Order.Add(key);
            Children[key] = child;
        }
    }

    public static class SteamKvParser
    {
        private enum Tok { End, Str, Open, Close }

        private sealed class Lexer
        {
            private readonly TextReader r;
            private readonly StringBuilder sb = new StringBuilder(64);
            public string Text;
            public Lexer(TextReader reader) { r = reader; }

            public Tok Next()
            {
                while (true)
                {
                    int c = r.Read();
                    if (c < 0) return Tok.End;
                    char ch = (char)c;
                    if (ch == '﻿' || char.IsWhiteSpace(ch)) continue;
                    if (ch == '{') return Tok.Open;
                    if (ch == '}') return Tok.Close;
                    if (ch == '/' && r.Peek() == '/') { SkipLine(); continue; }
                    if (ch == '[') { SkipConditional(); continue; }   // [$WIN32] style conditionals: ignored
                    if (ch == '"') { ReadQuoted(); return Tok.Str; }
                    ReadUnquoted(ch);
                    return Tok.Str;
                }
            }

            private void SkipLine() { int c; while ((c = r.Read()) >= 0 && c != '\n') { } }

            private void SkipConditional() { int c; while ((c = r.Read()) >= 0 && c != ']' && c != '\n') { } }

            private void ReadQuoted()
            {
                sb.Clear();
                int c;
                while ((c = r.Read()) >= 0 && c != '"')
                {
                    if (c == '\\')
                    {
                        int n = r.Read();
                        if (n < 0) break;
                        switch ((char)n)
                        {
                            case '\\': sb.Append('\\'); break;
                            case '"': sb.Append('"'); break;
                            case 'n': sb.Append('\n'); break;
                            case 't': sb.Append('\t'); break;
                            default: sb.Append('\\').Append((char)n); break;   // tolerate unknown escapes
                        }
                    }
                    else sb.Append((char)c);
                }
                Text = sb.ToString();
            }

            private void ReadUnquoted(char first)
            {
                sb.Clear();
                sb.Append(first);
                int c;
                while ((c = r.Peek()) >= 0 && !char.IsWhiteSpace((char)c) && c != '{' && c != '}' && c != '"')
                    sb.Append((char)r.Read());
                Text = sb.ToString();
            }
        }

        /// <summary>Parses the whole document. Never throws on malformed input (returns what was read).</summary>
        public static SteamKv Parse(string text) => Parse(new StringReader(text ?? ""));

        public static SteamKv Parse(TextReader reader)
        {
            var root = new SteamKv();
            if (reader == null) return root;
            var lx = new Lexer(reader);
            ReadBlock(lx, root, topLevel: true);
            return root;
        }

        /// <summary>Streams the document and materializes only the block at <paramref name="path"/>
        /// (case-insensitive keys, starting from the top-level key). Other blocks are skipped without
        /// allocating nodes. Returns null when the path does not exist.</summary>
        public static SteamKv ParsePath(TextReader reader, params string[] path)
        {
            if (reader == null || path == null || path.Length == 0) return null;
            var lx = new Lexer(reader);
            int depth = 0;   // number of path segments matched (we're inside that many matched blocks)
            while (true)
            {
                Tok t = lx.Next();
                if (t == Tok.End) return null;
                if (t == Tok.Close) { if (depth == 0) continue; return null; }  // left the matched parent: not found
                if (t == Tok.Open) { SkipBlock(lx); continue; }                 // stray block
                string key = lx.Text;
                Tok v = lx.Next();
                if (v == Tok.End) return null;
                if (v == Tok.Str) continue;                                      // key/value pair: irrelevant
                if (v == Tok.Close) { if (depth == 0) continue; return null; }
                // v == Open
                if (string.Equals(key, path[depth], StringComparison.OrdinalIgnoreCase))
                {
                    depth++;
                    if (depth == path.Length)
                    {
                        var node = new SteamKv();
                        ReadBlock(lx, node, topLevel: false);
                        return node;
                    }
                }
                else SkipBlock(lx);
            }
        }

        public static SteamKv ParsePathFromFile(string file, params string[] path)
        {
            using (var sr = new StreamReader(file, Encoding.UTF8, true, 1 << 16))
                return ParsePath(sr, path);
        }

        public static SteamKv ParseFile(string file)
        {
            using (var sr = new StreamReader(file, Encoding.UTF8, true, 1 << 16))
                return Parse(sr);
        }

        private static void SkipBlock(Lexer lx)
        {
            int d = 1;
            while (d > 0)
            {
                Tok t = lx.Next();
                if (t == Tok.End) return;
                if (t == Tok.Open) d++;
                else if (t == Tok.Close) d--;
            }
        }

        private static void ReadBlock(Lexer lx, SteamKv node, bool topLevel)
        {
            while (true)
            {
                Tok t = lx.Next();
                if (t == Tok.End) return;
                if (t == Tok.Close) { if (topLevel) continue; return; }   // stray '}' at top level: ignore
                if (t == Tok.Open)
                {
                    // Anonymous block (malformed): read it into a throwaway node.
                    ReadBlock(lx, new SteamKv(), topLevel: false);
                    continue;
                }
                string key = lx.Text;
                Tok v = lx.Next();
                if (v == Tok.End) return;
                if (v == Tok.Str) node.SetValue(key, lx.Text);
                else if (v == Tok.Open)
                {
                    var child = new SteamKv();
                    ReadBlock(lx, child, topLevel: false);
                    node.SetChild(key, child);
                }
                else { if (topLevel) continue; return; }  // key followed by '}' : dangling key
            }
        }
    }
}
