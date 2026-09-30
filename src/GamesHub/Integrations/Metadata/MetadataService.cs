using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace GamesHub
{
    /// <summary>Test/customization hooks. Defaults = production behaviour.</summary>
    public sealed class MetadataServiceOptions
    {
        public string CacheDir = Path.Combine(AppPaths.CacheDir, "meta");
        public Func<string, Task<MetadataHttpResult>> Fetch = MetadataStoreClient.GetAsync;
        public Func<DateTime> UtcNow = () => DateTime.UtcNow;
        /// <summary>Store API allows ~200 requests / 5 min.</summary>
        public TimeSpan MinInterval = TimeSpan.FromSeconds(1.5);
        /// <summary>Pause after HTTP 429/403.</summary>
        public TimeSpan Backoff = TimeSpan.FromMinutes(5);
        /// <summary>After a network/HTTP failure the app is not retried for this long (this session).</summary>
        public TimeSpan FailureRetry = TimeSpan.FromMinutes(30);
        /// <summary>FetchAsync never waits longer than this for a rate-limit slot (returns cached data instead).</summary>
        public TimeSpan MaxInteractiveWait = TimeSpan.FromSeconds(5);
    }

    public sealed class MetadataService : IMetadataService
    {
        private readonly AppSettings _settings;
        private readonly MetadataServiceOptions _opt;
        private readonly MetadataCache _cache;
        private readonly MetadataRateLimiter _limiter;

        private readonly object _gate = new object();
        private readonly Dictionary<string, Task<Outcome>> _inflight = new Dictionary<string, Task<Outcome>>();
        private readonly Dictionary<string, DateTime> _failedUntil = new Dictionary<string, DateTime>();
        private readonly LinkedList<string> _queue = new LinkedList<string>();
        private readonly HashSet<string> _queued = new HashSet<string>();
        private bool _workerRunning;
        private bool _backoffLogged;

        public event Action<string> MetadataUpdated;

        public MetadataService(AppSettings settings) : this(settings, new MetadataServiceOptions()) { }

        public MetadataService(AppSettings settings, MetadataServiceOptions options)
        {
            _settings = settings ?? new AppSettings();
            _opt = options ?? new MetadataServiceOptions();
            _cache = new MetadataCache(_opt.CacheDir, _opt.UtcNow);
            _limiter = new MetadataRateLimiter(_opt.MinInterval, _opt.UtcNow);
        }

        // ------------------------------------------------------------------ public API

        public GameInfo GetCached(string appId)
        {
            try
            {
                string id = Normalize(appId);
                if (id == null) return null;
                MetadataCacheEntry e = _cache.Get(id);
                return e != null && !e.Negative ? e.Info : null;
            }
            catch (Exception ex) when (ExpectedErrors.IsFileOrJson(ex))
            {
                Log.Warn("Metadata: GetCached failed for app " + appId, ex);
                return null;
            }
        }

        public async Task<GameInfo> FetchAsync(string appId)
        {
            try
            {
                string id = Normalize(appId);
                if (id == null) return null;
                MetadataCacheEntry e = _cache.Get(id);
                if (e != null && (_cache.IsFresh(e) || !CanFetch(id))) return e.Negative ? null : e.Info;
                if (e == null && !CanFetch(id)) return null;
                Outcome o = await Start(id, interactive: true).ConfigureAwait(false);
                return o.Info;
            }
            catch (Exception ex) when (ExpectedErrors.IsFileOrJson(ex))
            {
                // The network part cannot throw here (RunFetch catches); this covers the disk cache.
                Log.Warn("Metadata: FetchAsync failed for app " + appId, ex);
                return GetCached(appId);
            }
        }

        public void Prefetch(IEnumerable<string> appIds)
        {
            try
            {
                if (appIds == null || !_settings.FetchMetadata) return;
                bool start = false;
                foreach (string id in appIds.Select(Normalize).Where(x => x != null && !_cache.IsFresh(_cache.Get(x))))
                {
                    lock (_gate)
                    {
                        if (!_queued.Add(id)) continue;
                        _queue.AddLast(id);
                        if (!_workerRunning) { _workerRunning = true; start = true; }
                    }
                }
                if (start) Task.Run(WorkerLoop);
            }
            catch (Exception ex) when (ExpectedErrors.IsFileOrJson(ex))
            {
                Log.Warn("Metadata: Prefetch failed", ex);
            }
        }

        /// <summary>Every distinct genre, sorted with pt-BR rules (case-insensitive dedupe). For the UI filter.</summary>
        public static List<string> AllGenres(IEnumerable<GameInfo> infos)
        {
            var culture = CultureInfo.GetCultureInfo("pt-BR");
            var cmp = StringComparer.Create(culture, true);
            return (infos ?? Enumerable.Empty<GameInfo>())
                .Where(i => i?.Genres != null)
                .SelectMany(i => i.Genres)
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .Select(g => g.Trim())
                .Distinct(cmp)
                .OrderBy(g => g, cmp)
                .ToList();
        }

        /// <summary>Number of app ids waiting in the prefetch queue (diagnostics/tests).</summary>
        public int PendingCount { get { lock (_gate) return _queue.Count; } }

        // ------------------------------------------------------------------ internals

        private sealed class Outcome
        {
            public GameInfo Info;       // result to hand out (fresh, or stale/null on failure)
            public bool Throttled;      // could not run because of the rate limiter / back-off
            public static readonly Outcome Empty = new Outcome();
        }

        /// <summary>Digits-only app id, or null.</summary>
        public static string Normalize(string appId)
        {
            string s = (appId ?? "").Trim();
            if (s.Length == 0 || s.Length > 10 || !s.All(c => c >= '0' && c <= '9') || s.TrimStart('0') == "") return null;
            return s.TrimStart('0');
        }

        private bool CanFetch(string id)
        {
            if (!_settings.FetchMetadata) return false;
            lock (_gate) return !(_failedUntil.TryGetValue(id, out DateTime until) && _opt.UtcNow() < until);
        }

        /// <summary>Starts (or joins) the network fetch for an app id — concurrent callers share one request.</summary>
        private Task<Outcome> Start(string id, bool interactive)
        {
            lock (_gate)
            {
                if (_inflight.TryGetValue(id, out Task<Outcome> running)) return running;
                Task<Outcome> t = RunFetch(id, interactive);
                if (!t.IsCompleted) _inflight[id] = t;
                return t;
            }
        }

        private async Task<Outcome> RunFetch(string id, bool interactive)
        {
            try { return await FetchCore(id, interactive).ConfigureAwait(false); }
            // Resilience boundary: the shared fetch task every caller awaits (injectable HTTP, third-party store JSON, disk cache); callers get the cached data instead of a faulted task.
            catch (Exception ex)
            {
                Log.Warn("Metadata: fetch failed for app " + id, ex);
                return new Outcome { Info = GetCached(id) };
            }
            finally
            {
                lock (_gate) _inflight.Remove(id);
            }
        }

        private async Task<Outcome> FetchCore(string id, bool interactive)
        {
            await Task.Yield();   // never run the network path synchronously inside Start's lock
            MetadataCacheEntry cached = _cache.Get(id);
            GameInfo stale = cached != null && !cached.Negative ? cached.Info : null;

            // Wait for a rate-limit slot.
            while (true)
            {
                if (!_settings.FetchMetadata) return new Outcome { Info = stale };
                if (_limiter.TryAcquire(out TimeSpan wait)) break;
                if (interactive && wait > _opt.MaxInteractiveWait) return new Outcome { Info = stale, Throttled = true };
                await Task.Delay(wait < TimeSpan.FromMilliseconds(20) ? TimeSpan.FromMilliseconds(20) : wait).ConfigureAwait(false);
            }

            MetadataHttpResult r = await _opt.Fetch(id).ConfigureAwait(false) ?? new MetadataHttpResult();
            if (r.Status == 429 || r.Status == 403)
            {
                _limiter.Backoff(_opt.Backoff);
                bool log;
                lock (_gate) { log = !_backoffLogged; _backoffLogged = true; }
                if (log) Log.Warn("Metadata: Steam store throttled us (HTTP " + r.Status + "); pausing for " + _opt.Backoff.TotalMinutes + " min");
                return new Outcome { Info = stale, Throttled = true };
            }
            lock (_gate) _backoffLogged = false;

            if (r.Status != 200)
            {
                MarkFailed(id, r.Status == 0 ? "network error" : "HTTP " + r.Status, r.Error);
                return new Outcome { Info = stale };
            }

            MetadataParseResult p = MetadataParser.Parse(id, r.Body);
            switch (p.Status)
            {
                case MetadataParseStatus.Ok:
                    _cache.PutPositive(p.Info);
                    Raise(id);
                    return new Outcome { Info = p.Info };
                case MetadataParseStatus.NotFound:
                    _cache.PutNegative(id);
                    return Outcome.Empty;
                default:
                    MarkFailed(id, "unexpected response", null);
                    return new Outcome { Info = stale };
            }
        }

        private void MarkFailed(string id, string why, Exception ex)
        {
            lock (_gate) _failedUntil[id] = _opt.UtcNow() + _opt.FailureRetry;
            Log.Warn("Metadata: could not fetch app " + id + " (" + why + ")", ex);
        }

        private async Task WorkerLoop()
        {
            while (true)
            {
                string id;
                lock (_gate)
                {
                    if (_queue.Count == 0 || !_settings.FetchMetadata)
                    {
                        _queue.Clear();
                        _queued.Clear();
                        _workerRunning = false;
                        return;
                    }
                    id = _queue.First.Value;
                    _queue.RemoveFirst();
                }
                try
                {
                    if (!_cache.IsFresh(_cache.Get(id)) && CanFetch(id))
                    {
                        Outcome o = await Start(id, interactive: false).ConfigureAwait(false);
                        if (o.Throttled)
                        {
                            // Retry after the back-off; FetchCore waits for the limiter on the next attempt.
                            lock (_gate) { _queue.AddFirst(id); }
                            TimeSpan d = _limiter.Delay();
                            await Task.Delay(d > TimeSpan.Zero ? d : TimeSpan.FromMilliseconds(200)).ConfigureAwait(false);
                            continue;
                        }
                    }
                }
                // Resilience boundary: background worker loop; one app id must not stop the prefetch queue.
                catch (Exception ex)
                {
                    Log.Warn("Metadata: prefetch failed for app " + id, ex);
                }
                lock (_gate) _queued.Remove(id);
            }
        }

        private void Raise(string id)
        {
            try { MetadataUpdated?.Invoke(id); }
            // Resilience boundary: raises an event to arbitrary subscribers from a background fetch.
            catch (Exception ex) { Log.Warn("Metadata: MetadataUpdated handler threw for app " + id, ex); }
        }
    }
}
