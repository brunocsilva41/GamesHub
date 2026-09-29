// a YAML dump generated from PCGamingWiki's save/config tables, keyed by PCGW page title, with Steam ids.
// Used when the PCGW API is unreachable (Cloudflare challenge). ~17 MB on disk (2.4 MB gzip on the wire);
// only a small index (title → byte offset, steam id → title) is kept in memory.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GamesHub
{
    public sealed class LudusaviEntry
    {
        public string Title = "";
        public List<string> SteamIds = new List<string>();
        public List<PcgwRow> Rows = new List<PcgwRow>();
    }

    public sealed class LudusaviManifest
    {
        public const string Url = "https://raw.githubusercontent.com/mtkennerly/ludusavi-manifest/master/data/manifest.yaml";
        private static readonly TimeSpan MaxAge = TimeSpan.FromDays(14);
        private static readonly TimeSpan RetryDelay = TimeSpan.FromHours(1);

        private readonly string _file;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private DateTime _lastAttemptUtc = DateTime.MinValue;
        private DateTime _indexedMtime = DateTime.MinValue;
        private Dictionary<string, long> _offsets;                 // title → byte offset of its first line
        private Dictionary<string, string> _bySteamId;             // appid → title
        private Dictionary<string, List<string>> _byNorm;          // normalized title / main title → titles

        public LudusaviManifest(string file) { _file = file; }

        public bool Loaded => _offsets != null;

        /// <summary>Downloads (if missing/older than 14 days) and indexes the manifest. Never throws.</summary>
        public async Task<bool> EnsureAsync()
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                bool exists = File.Exists(_file);
                bool stale = !exists || DateTime.UtcNow - File.GetLastWriteTimeUtc(_file) > MaxAge;
                if (stale && DateTime.UtcNow - _lastAttemptUtc > RetryDelay)
                {
                    _lastAttemptUtc = DateTime.UtcNow;
                    Log.Info("PCGW: downloading Ludusavi manifest");
                    if (!await PcgwHttp.DownloadToFileAsync(Url, _file, TimeSpan.FromMinutes(2)).ConfigureAwait(false) && !exists)
                        return false;
                }
                if (!File.Exists(_file)) return false;
                DateTime mtime = File.GetLastWriteTimeUtc(_file);
                if (_offsets == null || mtime != _indexedMtime)
                {
                    BuildIndex();
                    _indexedMtime = mtime;
                }
                return _offsets != null;
            }
            catch (Exception ex)
            {
                Log.Warn("PCGW: Ludusavi manifest unavailable", ex);
                return false;
            }
            finally { _gate.Release(); }
        }

        public string FindTitleByAppId(string appId)
        {
            if (_bySteamId == null || string.IsNullOrEmpty(appId)) return null;
            return _bySteamId.TryGetValue(appId, out string t) ? t : null;
        }

        public string FindTitleByName(string name)
        {
            if (_byNorm == null) return null;
            var candidates = new List<string>();
            foreach (string q in PcgwNames.QueryVariants(name))
                if (_byNorm.TryGetValue(q, out List<string> list)) candidates.AddRange(list);
            return PcgwNames.BestMatch(name, candidates);
        }

        public LudusaviEntry GetEntry(string title)
        {
            if (_offsets == null || title == null || !_offsets.TryGetValue(title, out long offset)) return null;
            try
            {
                var lines = new List<string>();
                using (var fs = new FileStream(_file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    fs.Seek(offset, SeekOrigin.Begin);
                    using (var sr = new StreamReader(fs, new UTF8Encoding(false)))
                    {
                        string line;
                        while ((line = sr.ReadLine()) != null)
                        {
                            if (lines.Count > 0 && IsTopLevel(line)) break;
                            lines.Add(line);
                            if (lines.Count > 5000) break;
                        }
                    }
                }
                return ParseEntry(lines);
            }
            catch (Exception ex)
            {
                Log.Warn("PCGW: cannot read manifest entry " + title, ex);
                return null;
            }
        }

        // ---------------------------------------------------------------- indexing

        private void BuildIndex()
        {
            var offsets = new Dictionary<string, long>(StringComparer.Ordinal);
            var bySteam = new Dictionary<string, string>(StringComparer.Ordinal);
            var byNorm = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            byte[] bytes = File.ReadAllBytes(_file);
            string title = null, section = null, sub = null;
            int pos = 0;
            while (pos < bytes.Length)
            {
                int nl = Array.IndexOf(bytes, (byte)'\n', pos);
                int end = nl < 0 ? bytes.Length : nl;
                int len = end - pos;
                if (len > 0 && bytes[end - 1] == '\r') len--;
                byte first = len > 0 ? bytes[pos] : (byte)0;
                if (len > 0 && first != (byte)' ' && first != (byte)'#' && first != (byte)'-')
                {
                    string line = Encoding.UTF8.GetString(bytes, pos, len);
                    title = UnquoteKey(line);
                    section = sub = null;
                    if (title.Length > 0 && !offsets.ContainsKey(title))
                    {
                        offsets[title] = pos;
                        AddNorm(byNorm, PcgwNames.Normalize(title), title);
                        AddNorm(byNorm, PcgwNames.NormalizeWithoutSubtitle(title), title);
                    }
                }
                else if (title != null && len > 2 && first == (byte)' ' && bytes[pos + 1] == (byte)' ')
                {
                    // Only "steam:" / "id:" sections matter for the index; decode just those lines.
                    if (bytes[pos + 2] != (byte)' ')
                    {
                        string line = Encoding.ASCII.GetString(bytes, pos, Math.Min(len, 40)).Trim();
                        section = line == "steam:" ? "steam" : line == "id:" ? "id" : null;
                        sub = null;
                    }
                    else if (section != null)
                    {
                        string line = Encoding.ASCII.GetString(bytes, pos, Math.Min(len, 80)).Trim();
                        if (section == "steam" && line.StartsWith("id: ")) AddSteam(bySteam, line.Substring(4), title);
                        else if (section == "id")
                        {
                            if (line.EndsWith(":")) sub = line.TrimEnd(':');
                            else if (sub == "steamExtra" && line.StartsWith("- ")) AddSteam(bySteam, line.Substring(2), title);
                        }
                    }
                }
                pos = end + 1;
            }
            _offsets = offsets;
            _bySteamId = bySteam;
            _byNorm = byNorm;
            Log.Info("PCGW: Ludusavi manifest indexed (" + offsets.Count + " games, " + bySteam.Count + " Steam ids)");
        }

        private static void AddSteam(Dictionary<string, string> d, string id, string title)
        {
            id = id.Trim().Trim('"');
            if (id.Length > 0 && id.All(char.IsDigit) && !d.ContainsKey(id)) d[id] = title;
        }

        private static void AddNorm(Dictionary<string, List<string>> d, string key, string title)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (!d.TryGetValue(key, out List<string> list)) d[key] = list = new List<string>();
            if (list.Count < 20) list.Add(title);
        }

        private static bool IsTopLevel(string line) =>
            line.Length > 0 && line[0] != ' ' && line[0] != '#' && line[0] != '-';

        // ---------------------------------------------------------------- entry parsing (public for tests)

        /// <summary>Key of a YAML mapping line: strips indentation, quotes and the trailing ":" / ": {}".</summary>
        public static string UnquoteKey(string line)
        {
            string s = line.Trim();
            if (s.EndsWith(": {}")) s = s.Substring(0, s.Length - 4);
            else if (s.EndsWith(":")) s = s.Substring(0, s.Length - 1);
            if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"') return UnescapeDouble(s.Substring(1, s.Length - 2));
            if (s.Length >= 2 && s[0] == '\'' && s[s.Length - 1] == '\'') return s.Substring(1, s.Length - 2).Replace("''", "'");
            return s;
        }

        private static string UnescapeDouble(string s)
        {
            if (s.IndexOf('\\') < 0) return s;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] != '\\' || i + 1 >= s.Length) { sb.Append(s[i]); continue; }
                char c = s[++i];
                switch (c)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u' when i + 4 < s.Length:
                        sb.Append((char)Convert.ToInt32(s.Substring(i + 1, 4), 16)); i += 4; break;
                    default: sb.Append(c); break; // \" \\ \/
                }
            }
            return sb.ToString();
        }

        private sealed class Cond { public string Os = ""; public string Store = ""; }

        private sealed class Item
        {
            public string Key = "";
            public bool Registry;
            public List<string> Tags = new List<string>();
            public List<Cond> When = new List<Cond>();
        }

        /// <summary>Parses one top-level entry (its lines, starting with the title line).</summary>
        public static LudusaviEntry ParseEntry(IList<string> lines)
        {
            var e = new LudusaviEntry();
            if (lines == null || lines.Count == 0) return e;
            e.Title = UnquoteKey(lines[0]);
            var items = new List<Item>();
            string section = null, idSub = null, itemSub = null;
            Item item = null;
            Cond cond = null;
            for (int i = 1; i < lines.Count; i++)
            {
                string raw = lines[i];
                if (raw.Trim().Length == 0) continue;
                int indent = raw.Length - raw.TrimStart(' ').Length;
                string t = raw.Trim();
                if (indent == 2) { section = t.TrimEnd(':'); item = null; idSub = null; continue; }
                if (section == "steam" && indent == 4 && t.StartsWith("id: ")) e.SteamIds.Add(t.Substring(4).Trim().Trim('"'));
                else if (section == "id")
                {
                    if (indent == 4) idSub = t.TrimEnd(':');
                    else if (idSub == "steamExtra" && t.StartsWith("- ")) e.SteamIds.Add(t.Substring(2).Trim().Trim('"'));
                }
                else if (section == "files" || section == "registry")
                {
                    if (indent == 4) { item = new Item { Key = UnquoteKey(t), Registry = section == "registry" }; items.Add(item); itemSub = null; }
                    else if (item != null && indent == 6) { itemSub = t.TrimEnd(':'); cond = null; }
                    else if (item != null && indent >= 8)
                    {
                        if (itemSub == "tags" && t.StartsWith("- ")) item.Tags.Add(t.Substring(2).Trim());
                        else if (itemSub == "when")
                        {
                            if (t.StartsWith("- ")) { cond = new Cond(); item.When.Add(cond); t = t.Substring(2).Trim(); }
                            if (cond != null) ApplyCond(cond, t);
                        }
                    }
                }
            }
            foreach (Item it in items) AddRows(e.Rows, it);
            return e;
        }

        private static void ApplyCond(Cond c, string kv)
        {
            int colon = kv.IndexOf(':');
            if (colon < 0) return;
            string k = kv.Substring(0, colon).Trim(), v = kv.Substring(colon + 1).Trim().Trim('"');
            if (k == "os") c.Os = v;
            else if (k == "store") c.Store = v;
        }

        private static void AddRows(List<PcgwRow> rows, Item it)
        {
            string platform = PlatformOf(it);
            if (platform == null) return;
            string raw = it.Registry ? RegistryToRaw(it.Key) : PathToRaw(it.Key, platform == "Steam");
            if (raw == null) return;
            bool save = it.Tags.Contains("save"), config = it.Tags.Contains("config");
            if (!save && !config) save = true;
            if (save) rows.Add(new PcgwRow { Kind = "save", Platform = platform, Raw = raw });
            if (config) rows.Add(new PcgwRow { Kind = "config", Platform = platform, Raw = raw });
        }

        /// <summary>"Windows" | "Steam" (Steam userdata/cloud, any OS) | null (other OS / Microsoft Store only).</summary>
        private static string PlatformOf(Item it)
        {
            string k = it.Key;
            if (k.Contains("<xdg") || k.StartsWith("<home>/.") || k.Contains("/Library/")) return null;
            if (it.When.Count == 0) return "Windows";
            if (it.When.Any(c => c.Os == "windows" && c.Store != "microsoft")) return "Windows";
            if (it.When.Any(c => c.Os == "" && c.Store == "steam")) return "Steam";
            if (it.When.Any(c => c.Os == "" && c.Store != "microsoft")) return "Windows";
            return null;
        }

        private static readonly (string from, string to)[] Placeholders =
        {
            ("<base>", "{{p|game}}"), ("<game>", "{{p|game}}"), ("<storeUserId>", "{{p|uid}}"),
            ("<home>", "{{p|userprofile}}"), ("<winAppData>", "{{p|appdata}}"),
            ("<winLocalAppDataLow>", "{{p|userprofile\\AppData\\LocalLow}}"), ("<winLocalAppData>", "{{p|localappdata}}"),
            ("<winDocuments>", "{{p|userprofile\\Documents}}"), ("<winPublic>", "{{p|public}}"),
            ("<winProgramData>", "{{p|programdata}}"), ("<winDir>", "{{p|windir}}"), ("<osUserName>", "{{p|username}}"),
        };

        /// <summary>Ludusavi path → PCGW-style raw path, e.g. "&lt;winAppData&gt;/Foo" → "{{p|appdata}}\Foo".</summary>
        public static string PathToRaw(string path, bool steam)
        {
            string s = path;
            foreach (var (from, to) in Placeholders) s = s.Replace(from, to);
            s = s.Replace("<root>", steam ? "{{p|steam}}" : "{{p|root}}"); // non-Steam store roots are unknown
            if (s.Contains("<")) return null; // unknown placeholder
            s = s.Replace("**", "*").Replace('/', '\\');
            return s;
        }

        public static string RegistryToRaw(string key)
        {
            string s = key.Replace('/', '\\');
            if (s.StartsWith("HKEY_CURRENT_USER", StringComparison.OrdinalIgnoreCase)) return "{{p|hkcu}}" + s.Substring(17);
            if (s.StartsWith("HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase)) return "{{p|hklm}}" + s.Substring(18);
            return null;
        }
    }
}
