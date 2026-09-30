using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;

namespace GamesHub
{
    internal sealed class GameMeta
    {
        public string NameOverride = "";
        public string LaunchArgs = "";
        public string SteamAppIdOverride = "";
        public bool Favorite;
        public bool Hidden;
        public List<string> Collections = new List<string>();
        public DateTime? LastPlayed;
        public long PlaySeconds;
        public DateTime? AddedAt;

        public GameMeta Clone()
        {
            var c = (GameMeta)MemberwiseClone();
            c.Collections = new List<string>(Collections);
            return c;
        }
    }

    /// <summary>Thread-safe metadata store with debounced atomic saves.</summary>
    internal sealed class LibraryStore : IDisposable
    {
        public const int SchemaVersion = 1;

        private readonly object _gate = new object();
        private readonly object _ioGate = new object();
        private readonly string _file;
        private readonly Dictionary<string, GameMeta> _games = new Dictionary<string, GameMeta>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Timer _saveTimer;
        private bool _legacyImported, _dirty, _disposed;
        private DateTime? _saveDue;

        public LibraryStore(string file)
        {
            _file = file;
            _saveTimer = new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
        }

        public void Load()
        {
            // Json.Load quarantines a corrupt file and falls back to library.json.bak, so the next save cannot
            // overwrite the last good copy.
            var raw = Json.Load<Dictionary<string, object>>(_file, null);
            lock (_gate) FromJson(raw);
        }

        /// <summary>Copy of the metadata for id, or null.</summary>
        public GameMeta Get(string id)
        {
            lock (_gate) return _games.TryGetValue(id, out GameMeta m) ? m.Clone() : null;
        }

        /// <summary>Mutates (creating if needed) the metadata for id, then schedules a save.</summary>
        public void Edit(string id, Action<GameMeta> edit, int saveDelayMs = 400)
        {
            lock (_gate)
            {
                if (!_games.TryGetValue(id, out GameMeta m)) _games[id] = m = new GameMeta();
                edit(m);
            }
            ScheduleSave(saveDelayMs);
        }

        /// <summary>Records AddedAt for ids seen for the first time. Returns true when anything changed.</summary>
        public bool EnsureAdded(IEnumerable<KeyValuePair<string, DateTime>> seen)
        {
            bool changed = false;
            lock (_gate)
            {
                foreach (var kv in seen)
                {
                    if (!_games.TryGetValue(kv.Key, out GameMeta m)) _games[kv.Key] = m = new GameMeta();
                    if (m.AddedAt == null) { m.AddedAt = kv.Value; changed = true; }
                }
            }
            if (changed) ScheduleSave(1000);
            return changed;
        }

        public bool IsIgnored(string id) { lock (_gate) return _ignored.Contains(id); }
        public HashSet<string> Ignored() { lock (_gate) return new HashSet<string>(_ignored, StringComparer.OrdinalIgnoreCase); }

        public void SetIgnored(string id, bool ignored)
        {
            lock (_gate) { if (ignored) _ignored.Add(id); else _ignored.Remove(id); }
            ScheduleSave(0);
        }

        /// <summary>One-time, read-only import of v1 &lt;gamesDir&gt;\_hub\playlog.json (lastPlayed per shortcut).</summary>
        public void ImportLegacyPlaylog(string gamesDir)
        {
            lock (_gate) if (_legacyImported) return;
            string file = Path.Combine(AppPaths.LegacyHubDir(gamesDir), "playlog.json");
            Dictionary<string, DateTime> map = new Dictionary<string, DateTime>();
            try
            {
                if (File.Exists(file)) map = MapLegacyPlaylog(Json.DeserializeObject(File.ReadAllText(file)) as IDictionary<string, object>);
            }
            catch (Exception ex) when (ExpectedErrors.IsFileOrJson(ex)) { Log.Warn("Cannot import legacy playlog " + file, ex); }
            lock (_gate)
            {
                foreach (var kv in map)
                {
                    if (!_games.TryGetValue(kv.Key, out GameMeta m)) _games[kv.Key] = m = new GameMeta();
                    if (m.LastPlayed == null || m.LastPlayed < kv.Value) m.LastPlayed = kv.Value;
                }
                _legacyImported = true;
            }
            Log.Info("Legacy playlog import: " + map.Count + " entries");
            ScheduleSave(0);
        }

        /// <summary>Pure: v1 playlog { "&lt;full path&gt;": "&lt;ISO date&gt;" } → folder id → local time.</summary>
        public static Dictionary<string, DateTime> MapLegacyPlaylog(IDictionary<string, object> playlog)
        {
            var result = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            if (playlog == null) return result;
            foreach (var kv in playlog)
            {
                string name;
                try { name = Path.GetFileName(kv.Key ?? ""); }
                catch (ArgumentException ex) { Log.Warn("Bad legacy playlog key: " + kv.Key, ex); continue; }
                DateTime? when = ParseDate(Convert.ToString(kv.Value));
                if (name.Length == 0 || when == null) continue;
                string id = GameRules.FolderId(name);
                if (!result.TryGetValue(id, out DateTime prev) || prev < when.Value) result[id] = when.Value;
            }
            return result;
        }

        /// <summary>Marks dirty and saves within delayMs. An already scheduled earlier save is never postponed,
        /// so a stream of small edits (play time) still gets persisted.</summary>
        public void ScheduleSave(int delayMs)
        {
            DateTime due = DateTime.UtcNow.AddMilliseconds(Math.Max(1, delayMs));
            lock (_gate)
            {
                _dirty = true;
                if (_disposed || (_saveDue != null && _saveDue <= due)) return;
                _saveDue = due;
            }
            try { _saveTimer.Change(Math.Max(1, delayMs), Timeout.Infinite); }
            catch (ObjectDisposedException ex) { Log.Warn("Timer used after dispose", ex); }
        }

        public void Flush()
        {
            lock (_ioGate)
            {
                string json;
                lock (_gate)
                {
                    _saveDue = null;
                    if (!_dirty) return;
                    json = Json.Serialize(ToJson());
                    _dirty = false;
                }
                try { Json.WriteAllTextAtomic(_file, json); }
                catch (Exception ex) when (ExpectedErrors.IsFileSystem(ex))
                {
                    Log.Error("Cannot save " + _file, ex);
                    lock (_gate) _dirty = true;
                }
            }
        }

        public void Dispose()
        {
            lock (_gate) _disposed = true;
            _saveTimer.Dispose();
            Flush();
        }

        // ------------------------------------------------------------------ (de)serialization

        private Dictionary<string, object> ToJson()
        {
            var games = new Dictionary<string, object>();
            foreach (var kv in _games.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                GameMeta m = kv.Value;
                var o = new Dictionary<string, object>();
                if (m.NameOverride.Length > 0) o["nameOverride"] = m.NameOverride;
                if (m.LaunchArgs.Length > 0) o["launchArgs"] = m.LaunchArgs;
                if (m.SteamAppIdOverride.Length > 0) o["steamAppIdOverride"] = m.SteamAppIdOverride;
                if (m.Favorite) o["favorite"] = true;
                if (m.Hidden) o["hidden"] = true;
                if (m.Collections.Count > 0) o["collections"] = m.Collections;
                if (m.LastPlayed != null) o["lastPlayed"] = FormatDate(m.LastPlayed.Value);
                if (m.PlaySeconds > 0) o["playSeconds"] = m.PlaySeconds;
                if (m.AddedAt != null) o["addedAt"] = FormatDate(m.AddedAt.Value);
                if (o.Count > 0) games[kv.Key] = o;
            }
            return new Dictionary<string, object>
            {
                ["version"] = SchemaVersion,
                ["legacyImported"] = _legacyImported,
                ["ignored"] = _ignored.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
                ["games"] = games,
            };
        }

        private void FromJson(IDictionary<string, object> root)
        {
            _games.Clear();
            _ignored.Clear();
            _legacyImported = Json.Bool(root, "legacyImported");
            if (root == null) return;
            if (root.TryGetValue("ignored", out object ign) && ign is IEnumerable list && !(ign is string))
                foreach (object x in list) { string s = Convert.ToString(x); if (!string.IsNullOrEmpty(s)) _ignored.Add(s); }
            IDictionary<string, object> games = Json.Obj(root, "games");
            if (games == null) return;
            foreach (var kv in games)
            {
                var o = kv.Value as IDictionary<string, object>;
                if (o == null) continue;
                var m = new GameMeta
                {
                    NameOverride = Json.Str(o, "nameOverride"),
                    LaunchArgs = Json.Str(o, "launchArgs"),
                    SteamAppIdOverride = Json.Str(o, "steamAppIdOverride"),
                    Favorite = Json.Bool(o, "favorite"),
                    Hidden = Json.Bool(o, "hidden"),
                    LastPlayed = ParseDate(Json.Str(o, "lastPlayed")),
                    PlaySeconds = Math.Max(0, Json.Long(o, "playSeconds")),
                    AddedAt = ParseDate(Json.Str(o, "addedAt")),
                };
                if (o.TryGetValue("collections", out object cols) && cols is IEnumerable ce && !(cols is string))
                    m.Collections = ce.Cast<object>().Select(Convert.ToString).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
                _games[kv.Key] = m;
            }
        }

        public static string FormatDate(DateTime d) => new DateTimeOffset(d).ToString("o", CultureInfo.InvariantCulture);

        public static DateTime? ParseDate(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTimeOffset d)
                ? d.LocalDateTime : (DateTime?)null;
        }
    }
}
