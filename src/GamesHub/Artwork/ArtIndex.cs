using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace GamesHub
{
    /// <summary>
    /// Persistent bookkeeping for the artwork cache (index.json in the art folder):
    ///  - negative cache: things that were not found (404, no icon...) — retried after 7 days;
    ///  - search cache: normalized name → Steam app id ("" = no match, retried after 14 days).
    /// Thread-safe. Saves are debounced and atomic.
    /// </summary>
    public sealed class ArtIndex : IDisposable
    {
        public const int SchemaVersion = 1;
        public static readonly TimeSpan NegativeTtl = TimeSpan.FromDays(7);
        public static readonly TimeSpan NoMatchTtl = TimeSpan.FromDays(14);

        private readonly string _file;
        private readonly Func<DateTime> _now;
        private readonly object _gate = new object();
        private readonly Dictionary<string, long> _negative = new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly Dictionary<string, SearchEntry> _search = new Dictionary<string, SearchEntry>(StringComparer.Ordinal);
        private readonly Timer _saveTimer;
        private bool _dirty;

        private struct SearchEntry { public string AppId; public long At; }

        public ArtIndex(string file, Func<DateTime> utcNow = null)
        {
            _file = file;
            _now = utcNow ?? (() => DateTime.UtcNow);
            _saveTimer = new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
            Load();
        }

        private long Now => new DateTimeOffset(_now(), TimeSpan.Zero).ToUnixTimeSeconds();

        // ------------------------------------------------------------ negative cache

        public bool IsNegative(string key)
        {
            lock (_gate)
                return _negative.TryGetValue(key, out long at) && Now - at < (long)NegativeTtl.TotalSeconds;
        }

        public void MarkNegative(string key)
        {
            lock (_gate) { _negative[key] = Now; Touch(); }
        }

        /// <summary>Removes every negative entry whose key starts with the prefix.</summary>
        public void ClearNegative(string prefix)
        {
            lock (_gate)
            {
                foreach (string k in _negative.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
                    _negative.Remove(k);
                Touch();
            }
        }

        // ------------------------------------------------------------ search cache

        /// <summary>True when a still-valid answer is cached. appId == "" means "no match".</summary>
        public bool TryGetMatch(string key, out string appId)
        {
            appId = "";
            if (string.IsNullOrEmpty(key)) return false;
            lock (_gate)
            {
                if (!_search.TryGetValue(key, out SearchEntry e)) return false;
                if (e.AppId.Length == 0 && Now - e.At >= (long)NoMatchTtl.TotalSeconds) return false;
                appId = e.AppId;
                return true;
            }
        }

        public void SetMatch(string key, string appId)
        {
            if (string.IsNullOrEmpty(key)) return;
            lock (_gate) { _search[key] = new SearchEntry { AppId = appId ?? "", At = Now }; Touch(); }
        }

        /// <summary>Adds a match only when nothing is cached for the key (legacy import).</summary>
        public bool AddMatchIfMissing(string key, string appId)
        {
            if (string.IsNullOrEmpty(key)) return false;
            lock (_gate)
            {
                if (_search.ContainsKey(key)) return false;
                _search[key] = new SearchEntry { AppId = appId ?? "", At = Now };
                Touch();
                return true;
            }
        }

        public void RemoveMatch(string key)
        {
            lock (_gate) { if (_search.Remove(key)) Touch(); }
        }

        // ------------------------------------------------------------ persistence

        private void Touch()
        {
            _dirty = true;
            _saveTimer.Change(1500, Timeout.Infinite);
        }

        public void Flush()
        {
            lock (_gate)
            {
                if (!_dirty) return;
                var neg = _negative.ToDictionary(kv => kv.Key, kv => (object)kv.Value);
                var search = _search.ToDictionary(kv => kv.Key,
                    kv => (object)new Dictionary<string, object> { { "appId", kv.Value.AppId }, { "at", kv.Value.At } });
                var doc = new Dictionary<string, object> { { "schema", SchemaVersion }, { "negative", neg }, { "search", search } };
                try
                {
                    Json.Save(_file, doc);
                    _dirty = false;
                }
                catch (Exception ex) { Log.Warn("Art: cannot save " + _file, ex); }
            }
        }

        private void Load()
        {
            if (!File.Exists(_file)) return;
            IDictionary<string, object> doc;
            try { doc = Json.DeserializeObject(File.ReadAllText(_file)) as IDictionary<string, object>; }
            catch (Exception ex) { Log.Warn("Art: index unreadable, starting fresh: " + _file, ex); return; }
            if (doc == null) return;
            if (Json.Long(doc, "schema") != SchemaVersion)
            {
                Log.Info("Art: index schema changed, starting fresh");
                return;
            }
            IDictionary<string, object> neg = Json.Obj(doc, "negative");
            if (neg != null)
                foreach (string k in neg.Keys) _negative[k] = Json.Long(neg, k);
            IDictionary<string, object> search = Json.Obj(doc, "search");
            if (search != null)
                foreach (string k in search.Keys)
                {
                    IDictionary<string, object> e = Json.Obj(search, k);
                    if (e != null) _search[k] = new SearchEntry { AppId = Json.Str(e, "appId"), At = Json.Long(e, "at") };
                }
        }

        public void Dispose()
        {
            _saveTimer.Dispose();
            Flush();
        }
    }
}
