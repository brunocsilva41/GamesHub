using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GamesHub
{
    /// <summary>
    /// Allowlist between "discoverGames" and "addDiscovered": the page may only ask to add executables that the
    /// last discovery returned and that still exist. Arbitrary paths from the page are rejected. Thread-safe.
    /// </summary>
    public sealed class DiscoverySession
    {
        public const int MaxNameLength = 120;
        public const int MaxItems = 200;

        private readonly object _gate = new object();
        private Dictionary<string, DiscoveredGame> _allowed = new Dictionary<string, DiscoveredGame>();

        /// <summary>Replaces the allowlist with the latest discovery result.</summary>
        public void Remember(IEnumerable<DiscoveredGame> candidates)
        {
            var map = new Dictionary<string, DiscoveredGame>();
            foreach (DiscoveredGame c in candidates ?? Enumerable.Empty<DiscoveredGame>())
            {
                string k = DiscoveryRoots.Key(c?.Exe);
                if (k.Length > 0) map[k] = c;
            }
            lock (_gate) _allowed = map;
        }

        /// <summary>A validated request: the discovered exe and the (trimmed) name to use, or an error message.</summary>
        public sealed class Item
        {
            public string Exe = "";
            public string Name = "";
            public string Error;
        }

        /// <summary>Parses the page's "items" array ([{ exe, name }]). Unknown/missing executables become errors;
        /// duplicates are ignored; names are trimmed to <see cref="MaxNameLength"/> (empty → discovered name).</summary>
        public List<Item> Validate(object items, Func<string, bool> fileExists = null)
        {
            fileExists = fileExists ?? File.Exists;
            var result = new List<Item>();
            if (!(items is IEnumerable list) || items is string) return result;
            var seen = new HashSet<string>();
            foreach (object o in list.Cast<object>().Take(MaxItems))
            {
                if (!(o is IDictionary<string, object> d)) continue;
                string exe = Json.Str(d, "exe");
                string key = DiscoveryRoots.Key(exe);
                if (!seen.Add(key)) continue;
                DiscoveredGame found = null;
                lock (_gate) { if (key.Length > 0) _allowed.TryGetValue(key, out found); }
                if (found == null)
                {
                    result.Add(new Item { Exe = exe, Error = "Este jogo não faz parte da última busca. Busque novamente." });
                    continue;
                }
                if (!fileExists(found.Exe))
                {
                    result.Add(new Item { Exe = found.Exe, Error = "O executável de " + found.Name + " não existe mais." });
                    continue;
                }
                string name = CleanName(Json.Str(d, "name"));
                result.Add(new Item { Exe = found.Exe, Name = name.Length > 0 ? name : found.Name });
            }
            return result;
        }

        private static string CleanName(string s)
        {
            string t = new string((s ?? "").Where(ch => !char.IsControl(ch)).ToArray()).Trim();
            return t.Length > MaxNameLength ? t.Substring(0, MaxNameLength).Trim() : t;
        }
    }

    /// <summary>IProgress that calls back synchronously on the reporting thread (no SynchronizationContext hop).</summary>
    public sealed class CallbackProgress<T> : IProgress<T>
    {
        private readonly Action<T> _report;
        public CallbackProgress(Action<T> report) { _report = report; }
        public void Report(T value) => _report?.Invoke(value);
    }
}
