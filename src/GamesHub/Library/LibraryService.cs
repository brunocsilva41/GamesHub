// User actions (launch/add/remove/undo/update) live in LibraryService.Actions.cs.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GamesHub
{
    public sealed partial class LibraryService : ILibraryService
    {
        private const int ChangedDelayMs = 300, ArtDelayMs = 150, FolderDebounceMs = 700, StoreDebounceMs = 2000;

        private readonly AppSettings _settings;
        private readonly IArtworkService _art;
        private readonly LibraryStore _store;
        private readonly string _trashDir;
        private readonly FolderSource _folder = new FolderSource();

        // Snapshot (guarded by _gate). Games in these collections are private objects; callers get clones.
        private readonly object _gate = new object();
        private List<Game> _list = new List<Game>();
        private Dictionary<string, Game> _byId = new Dictionary<string, Game>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, Game> _sourceById = new Dictionary<string, Game>(StringComparer.OrdinalIgnoreCase);
        private List<TrackTarget> _trackTargets = new List<TrackTarget>();
        private readonly HashSet<string> _running = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Source data + art cache (guarded by _rebuildLock, which serializes scans and rebuilds).
        private readonly object _rebuildLock = new object();
        private List<Game> _folderGames = new List<Game>(), _steamGames = new List<Game>(), _epicGames = new List<Game>(), _extraGames = new List<Game>();
        private readonly List<IExtraSource> _extraSources = new List<IExtraSource>();
        private readonly Dictionary<string, KeyValuePair<string, Artwork>> _artCache = new Dictionary<string, KeyValuePair<string, Artwork>>(StringComparer.OrdinalIgnoreCase);
        private volatile int _resolvingThread;
        private volatile string _resolvingId;

        private readonly object _pendingArtGate = new object();
        private readonly HashSet<string> _pendingArt = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Timer _artTimer, _changedTimer;
        private int _changedArmed;

        private readonly object _infraGate = new object();
        private DebouncedWatcher _folderWatcher;
        private List<DebouncedWatcher> _storeWatchers = new List<DebouncedWatcher>();
        private string _storeWatchKey = "";
        private PlayTracker _tracker;
        private volatile bool _started, _disposed;

        public event Action Changed;
        public event Action<string, bool> RunningChanged;

        public LibraryService(AppSettings settings, IArtworkService art)
            : this(settings, art, AppPaths.LibraryFile, AppPaths.TrashDir) { }

        /// <summary>Test seam: custom metadata file and trash folder.</summary>
        internal LibraryService(AppSettings settings, IArtworkService art, string libraryFile, string trashDir)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _art = art;
            _trashDir = trashDir;
            _store = new LibraryStore(libraryFile);
            _artTimer = new Timer(_ => ProcessPendingArt(), null, Timeout.Infinite, Timeout.Infinite);
            _changedTimer = new Timer(_ => FireChanged(), null, Timeout.Infinite, Timeout.Infinite);
            if (_art != null) _art.ArtworkUpdated += OnArtworkUpdated;
        }

        // ------------------------------------------------------------------ queries

        public List<Game> GetGames() { lock (_gate) return _list.Select(LibraryMerge.Clone).ToList(); }

        public Game Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            lock (_gate) return _byId.TryGetValue(id, out Game g) ? LibraryMerge.Clone(g) : null;
        }

        public List<string> GetCollections()
        {
            lock (_gate)
                return _list.SelectMany(g => g.Collections).Distinct(StringComparer.CurrentCultureIgnoreCase)
                            .OrderBy(c => c, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        // ------------------------------------------------------------------ lifecycle

        /// <summary>Registers an additional importer (Riot, Hydra...). Call before Start(); later calls take
        /// effect on the next scan. Enabled per settings.Import&lt;Name&gt; (ImportRiot, ImportHydra).</summary>
        public void AddExtraSource(IExtraSource source)
        {
            if (source == null) return;
            lock (_extraSources) _extraSources.Add(source);
        }

        private bool IsExtraSourceEnabled(IExtraSource source)
        {
            switch ((source.Name ?? "").ToLowerInvariant())
            {
                case "riot": return _settings.ImportRiot;
                case "hydra": return _settings.ImportHydra;
                default: return true;
            }
        }

        public void Start()
        {
            if (_started) return;
            _started = true;
            Task.Run(() =>
            {
                try
                {
                    _store.Load();
                    _store.ImportLegacyPlaylog(_settings.GamesDir);
                    ScanAll(false);
                    EnsureInfrastructure();
                }
                // Resilience boundary: background task entry point; the error is logged and the app keeps running.
                catch (Exception ex) { Log.Error("Library start failed", ex); }
            });
        }

        public void Rescan()
        {
            if (_disposed) return;
            try
            {
                ScanAll(true);
                EnsureInfrastructure();
            }
            // Resilience boundary: user-triggered full scan across all sources; a failure must not reach the UI bridge.
            catch (Exception ex) { Log.Error("Library rescan failed", ex); }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_art != null) _art.ArtworkUpdated -= OnArtworkUpdated;
            lock (_infraGate)
            {
                _folderWatcher?.Dispose();
                _folderWatcher = null;
                foreach (DebouncedWatcher w in _storeWatchers) w.Dispose();
                _storeWatchers.Clear();
                _tracker?.Dispose();
                _tracker = null;
            }
            _artTimer.Dispose();
            _changedTimer.Dispose();
            _store.Dispose();
        }

        // ------------------------------------------------------------------ scanning

        private void ScanAll(bool forceArt)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            lock (_rebuildLock)
            {
                _folderGames = _folder.Scan(_settings.GamesDir);
                ScanStoresLocked();
                RebuildLocked(forceArt);
            }
            Log.Info($"Library scan: {_folderGames.Count} folder, {_steamGames.Count} steam, {_epicGames.Count} epic, {_extraGames.Count} extra in {sw.ElapsedMilliseconds} ms");
        }

        private void ScanFolder()
        {
            if (_disposed) return;
            lock (_rebuildLock)
            {
                _folderGames = _folder.Scan(_settings.GamesDir);
                RebuildLocked(false);
            }
        }

        private void ScanStores()
        {
            if (_disposed) return;
            lock (_rebuildLock)
            {
                ScanStoresLocked();
                RebuildLocked(false);
            }
        }

        private void ScanStoresLocked()
        {
            _steamGames = _settings.ImportSteam ? SteamSource.Scan(SteamSource.LibraryFolders(SteamSource.FindSteamPath())) : new List<Game>();
            _epicGames = _settings.ImportEpic ? EpicSource.Scan(EpicSource.ManifestsDir) : new List<Game>();
            List<IExtraSource> extras;
            lock (_extraSources) extras = _extraSources.ToList();
            var extraGames = new List<Game>();
            foreach (IExtraSource src in extras.Where(IsExtraSourceEnabled))
            {
                try { extraGames.AddRange((src.Scan() ?? new List<Game>()).Where(g => g != null && !string.IsNullOrEmpty(g.Id)).Select(Normalize)); }
                // Resilience boundary: pluggable importer parsing third-party launcher data (Riot, Hydra); one failing source must not stop the scan.
                catch (Exception ex) { Log.Warn("Extra source '" + src.Name + "' failed", ex); }
            }
            _extraGames = extraGames;
        }

        /// <summary>Guards against null fields from external importers.</summary>
        private static Game Normalize(Game g)
        {
            Game c = LibraryMerge.Clone(g);
            c.Name = c.Name ?? c.Id; c.Source = c.Source ?? ""; c.Platform = c.Platform ?? "PC";
            c.LaunchTarget = c.LaunchTarget ?? ""; c.LaunchArgs = c.LaunchArgs ?? ""; c.FilePath = c.FilePath ?? "";
            c.Ext = c.Ext ?? ""; c.InstallDir = c.InstallDir ?? ""; c.Exe = c.Exe ?? ""; c.SteamAppId = c.SteamAppId ?? "";
            return c;
        }

        /// <summary>Merges sources + metadata into a new snapshot. Caller holds _rebuildLock.</summary>
        private void RebuildLocked(bool forceArt)
        {
            HashSet<string> ignored = _store.Ignored();
            List<Game> sources = LibraryMerge.Dedupe(_folderGames, _steamGames.Concat(_epicGames).Concat(_extraGames))
                .Where(g => g.Source == GameRules.SourceFolder || !ignored.Contains(g.Id)).ToList();
            _store.EnsureAdded(sources.Select(s => new KeyValuePair<string, DateTime>(s.Id, s.AddedAt)));

            HashSet<string> running;
            lock (_gate) running = new HashSet<string>(_running, StringComparer.OrdinalIgnoreCase);
            var list = new List<Game>(sources.Count);
            foreach (Game src in sources)
            {
                Game g = LibraryMerge.ApplyMeta(src, _store.Get(src.Id), src.AddedAt);
                g.Running = running.Contains(g.Id);
                g.Art = ResolveArt(g, forceArt);
                list.Add(g);
            }
            var liveIds = new HashSet<string>(list.Select(g => g.Id), StringComparer.OrdinalIgnoreCase);
            foreach (string gone in _artCache.Keys.Where(k => !liveIds.Contains(k)).ToList()) _artCache.Remove(gone);
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));

            var targets = BuildTrackTargets(list, _settings.GamesDir);
            lock (_gate)
            {
                _list = list;
                _byId = list.ToDictionary(g => g.Id, StringComparer.OrdinalIgnoreCase);
                _sourceById = sources.ToDictionary(g => g.Id, StringComparer.OrdinalIgnoreCase);
                _trackTargets = targets;
            }
            RaiseChanged();
        }

        private Artwork ResolveArt(Game g, bool force)
        {
            string sig = LibraryMerge.ArtSignature(g);
            if (!force && _artCache.TryGetValue(g.Id, out var cached) && cached.Key == sig) return LibraryMerge.CloneArt(cached.Value);
            Artwork art = null;
            if (_art != null)
            {
                _resolvingThread = Thread.CurrentThread.ManagedThreadId;
                _resolvingId = g.Id;
                try { art = _art.Resolve(LibraryMerge.Clone(g)); }
                // Resilience boundary: per-game artwork during a rebuild; one bad entry must not stop the rebuild.
                catch (Exception ex) { Log.Warn("Artwork resolve failed for " + g.Id, ex); }
                finally { _resolvingThread = 0; _resolvingId = null; }
            }
            art = art ?? new Artwork();
            _artCache[g.Id] = new KeyValuePair<string, Artwork>(sig, art);
            return LibraryMerge.CloneArt(art);
        }

        internal static List<TrackTarget> BuildTrackTargets(IEnumerable<Game> games, string gamesDir)
        {
            var targets = new List<TrackTarget>();
            foreach (Game g in games)
            {
                string dir = g.InstallDir.Length > 0 && !GameRules.IsGenericDir(g.InstallDir, gamesDir) ? GameRules.NormalizeDir(g.InstallDir) : "";
                string exe = g.Exe.Length > 0 && !GameRules.IsLauncherExe(g.Exe) ? g.Exe : "";
                if (dir.Length == 0 && exe.Length == 0) continue;
                targets.Add(new TrackTarget { Id = g.Id, Exe = exe, DirPrefix = dir.Length > 0 ? dir + "\\" : "" });
            }
            return targets;
        }

        // ------------------------------------------------------------------ artwork events

        private void OnArtworkUpdated(string id)
        {
            if (string.IsNullOrEmpty(id) || _disposed) return;
            // Raised synchronously from inside our own Resolve call for the same game: the result we are
            // about to receive already reflects it. Re-resolving would recurse.
            if (_resolvingThread == Thread.CurrentThread.ManagedThreadId && string.Equals(_resolvingId, id, StringComparison.OrdinalIgnoreCase)) return;
            lock (_pendingArtGate) _pendingArt.Add(id);
            try { _artTimer.Change(ArtDelayMs, Timeout.Infinite); }
            catch (ObjectDisposedException ex) { Log.Warn("Timer used after dispose", ex); }
        }

        private void ProcessPendingArt()
        {
            if (_disposed) return;
            List<string> ids;
            lock (_pendingArtGate) { ids = _pendingArt.ToList(); _pendingArt.Clear(); }
            if (ids.Count == 0) return;
            try
            {
                lock (_rebuildLock)
                {
                    var updated = new Dictionary<string, Artwork>(StringComparer.OrdinalIgnoreCase);
                    foreach (string id in ids)
                    {
                        Game g = Get(id);
                        if (g != null) updated[id] = ResolveArt(g, true);
                    }
                    if (updated.Count == 0) return;
                    lock (_gate)
                    {
                        foreach (var kv in updated.Where(kv => _byId.ContainsKey(kv.Key)))
                            _byId[kv.Key].Art = kv.Value;
                    }
                }
                RaiseChanged();
            }
            // Resilience boundary: timer-thread entry point; an unhandled exception would crash the process.
            catch (Exception ex) { Log.Error("Artwork refresh failed", ex); }
        }

        // ------------------------------------------------------------------ events

        /// <summary>Throttled: at most one Changed per ChangedDelayMs, always after the latest change.</summary>
        private void RaiseChanged()
        {
            if (_disposed || Interlocked.Exchange(ref _changedArmed, 1) == 1) return;
            try { _changedTimer.Change(ChangedDelayMs, Timeout.Infinite); }
            catch (ObjectDisposedException ex) { Log.Warn("Timer used after dispose", ex); }
        }

        private void FireChanged()
        {
            Interlocked.Exchange(ref _changedArmed, 0);
            if (_disposed) return;
            try { Changed?.Invoke(); }
            // Resilience boundary: raises an event to arbitrary subscribers on a timer thread.
            catch (Exception ex) { Log.Error("Library Changed handler failed", ex); }
        }

        /// <summary>Applies a metadata-only change to the live snapshot without a full rebuild.</summary>
        private void Patch(string id, Action<Game> patch)
        {
            lock (_gate) if (_byId.TryGetValue(id, out Game g)) patch(g);
            RaiseChanged();
        }
    }
}
