using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GamesHub
{
    /// <summary>Background work of ArtworkService: one pipeline per game (icon → Steam match →
    /// Steam CDN → SteamGridDB), shared per-key downloads, and retry after network back-off.</summary>
    public sealed partial class ArtworkService
    {
        private readonly object _jobsGate = new object();
        private readonly HashSet<string> _running = new HashSet<string>();
        private readonly Dictionary<string, GameRef> _rerun = new Dictionary<string, GameRef>();
        private readonly Dictionary<string, GameRef> _deferred = new Dictionary<string, GameRef>();
        private readonly Timer _retryTimer;

        private struct StageResult
        {
            public bool Changed, Retry;
            public static StageResult operator +(StageResult a, StageResult b)
                => new StageResult { Changed = a.Changed || b.Changed, Retry = a.Retry || b.Retry };
        }

        // ------------------------------------------------------------ scheduling

        private void Schedule(GameRef g)
        {
            lock (_jobsGate)
            {
                if (_running.Contains(g.Id)) { _rerun[g.Id] = g; return; }
                _running.Add(g.Id);
                _deferred.Remove(g.Id);
            }
            Task.Run(() => RunPipelineAsync(g));
        }

        private async Task RunPipelineAsync(GameRef g)
        {
            var result = new StageResult();
            try
            {
                result = await ProcessAsync(g).ConfigureAwait(false);
            }
            // Resilience boundary: background task entry point (network, GDI+, shell and third-party data); the game is simply retried later.
            catch (Exception ex)
            {
                Log.Warn("Art: background work failed for " + g.Id, ex);
            }
            GameRef again;
            lock (_jobsGate)
            {
                _running.Remove(g.Id);
                if (_rerun.TryGetValue(g.Id, out again)) _rerun.Remove(g.Id);
                else if (result.Retry) Defer(g);
            }
            if (result.Changed) RaiseSoon(new[] { g.Id });
            if (again != null) Schedule(again);
        }

        // ------------------------------------------------------------ batched notifications

        private readonly HashSet<string> _toRaise = new HashSet<string>();
        private readonly Dictionary<string, HashSet<string>> _appUsers = new Dictionary<string, HashSet<string>>();
        private Timer _raiseTimer;

        /// <summary>Remembers which games display a Steam app's art, so all of them are notified.</summary>
        private void TrackAppUser(string appId, string gameId)
        {
            if (appId.Length == 0) return;
            lock (_jobsGate)
            {
                if (!_appUsers.TryGetValue(appId, out HashSet<string> set)) _appUsers[appId] = set = new HashSet<string>();
                set.Add(gameId);
            }
        }

        private IEnumerable<string> AppUsers(string appId)
        {
            lock (_jobsGate)
                return _appUsers.TryGetValue(appId, out HashSet<string> set) ? set.ToList() : new List<string>();
        }

        /// <summary>Coalesces notifications (250 ms) so each game is raised at most once per batch.</summary>
        private void RaiseSoon(IEnumerable<string> ids)
        {
            lock (_jobsGate)
            {
                foreach (string id in ids) _toRaise.Add(id);
                if (_raiseTimer == null) _raiseTimer = new Timer(_ => FlushRaises(), null, Timeout.Infinite, Timeout.Infinite);
                _raiseTimer.Change(250, Timeout.Infinite);
            }
        }

        private void FlushRaises()
        {
            List<string> ids;
            lock (_jobsGate)
            {
                ids = _toRaise.ToList();
                _toRaise.Clear();
            }
            foreach (string id in ids) Raise(id);
        }

        /// <summary>Called under _jobsGate: keep the game for a retry when the network is usable again.</summary>
        private void Defer(GameRef g)
        {
            _deferred[g.Id] = g;
            DateTime until = _http.Backoff.PausedUntil;
            TimeSpan wait = until > DateTime.UtcNow ? until - DateTime.UtcNow + TimeSpan.FromSeconds(2) : TimeSpan.FromMinutes(1);
            _retryTimer.Change(wait, Timeout.InfiniteTimeSpan);
        }

        private void RetryDeferred()
        {
            List<GameRef> list;
            lock (_jobsGate)
            {
                list = _deferred.Values.ToList();
                _deferred.Clear();
            }
            foreach (GameRef g in list) Schedule(g);
        }

        // ------------------------------------------------------------ pipeline

        private async Task<StageResult> ProcessAsync(GameRef g)
        {
            var result = new StageResult();
            string gk = ArtCache.GameKey(g.Id);

            if (_cache.FindBest(g.Id, "", ArtKind.Icon) == null && g.HasIconSource && !_index.IsNegative(NegIcon(gk)))
                result += await _tasks.Run("icon:" + gk, () => ExtractIconAsync(g, gk)).ConfigureAwait(false);

            if (!_settings.AutoArtwork) return result;
            bool notOnSteam = g.SteamAppId == ArtKind.NotOnSteam;   // user said: not a Steam game
            bool knownApp = !notOnSteam && ArtKind.IsAppId(g.SteamAppId);
            if (!knownApp && NameMatcher.IsLikelyNonGame(g.Name)) return result; // launchers, tools…

            string appId = knownApp ? g.SteamAppId : "";
            // Riot/Roblox/… games and user-marked ones skip Steam matching but still get SteamGridDB art.
            if (!knownApp && !notOnSteam && !NameMatcher.IsNonSteamPlatform(g.Platform))
            {
                MatchOutcome m = await MatchAsync(g).ConfigureAwait(false);
                if (m.Retry) { result.Retry = true; return result; }
                appId = m.AppId;
                TrackAppUser(appId, g.Id);
            }
            if (appId.Length > 0)
                result += await _tasks.Run("steam:" + appId, () => FetchSteamAsync(appId)).ConfigureAwait(false);
            if (_sgdb.Enabled && !result.Retry)
                result += await _tasks.Run("sgdb:" + gk, () => FetchSgdbAsync(g, gk, appId)).ConfigureAwait(false);
            return result;
        }

        private async Task<StageResult> ExtractIconAsync(GameRef g, string gk)
        {
            bool ok = await _icons.Value.ExtractAsync(new[] { g.Exe, g.FilePath }, _cache.IconFile(g.Id)).ConfigureAwait(false);
            if (!ok)
            {
                _index.MarkNegative(NegIcon(gk));
                Log.Info("Art: no usable icon for " + g.Id);
            }
            return new StageResult { Changed = ok };
        }

        // ------------------------------------------------------------ Steam matching

        private sealed class MatchOutcome { public string AppId = ""; public bool Retry; }

        private async Task<MatchOutcome> MatchAsync(GameRef g)
        {
            string key = MatchKey(g.Name);
            if (key.Length == 0) return new MatchOutcome();
            if (_index.TryGetMatch(key, out string cached)) return new MatchOutcome { AppId = cached };
            return await _tasks.Run("match:" + key, () => SearchMatchAsync(g.Name, key)).ConfigureAwait(false);
        }

        private async Task<MatchOutcome> SearchMatchAsync(string name, string key)
        {
            var queries = new[] { NameMatcher.SearchQuery(name), NameMatcher.RomanQuery(name) }.Where(q => q.Length > 0).Distinct().ToList();
            MatchCandidate best = null;
            double score = 0;
            bool retry = false, anyHits = false;
            async Task<bool> TryAsync(string q, bool community)
            {
                SearchOutcome o = community
                    ? await _steam.CommunitySearchAsync(q).ConfigureAwait(false)
                    : await _steam.StoreSearchAsync(q).ConfigureAwait(false);
                if (o.Retryable) { retry = true; return false; }
                anyHits |= o.Items.Count > 0;
                best = NameMatcher.PickBest(name, o.Items, out score);
                return best != null;
            }
            bool found = false;
            foreach (bool community in new[] { false, true })
                foreach (string q in queries)
                    if (!found && !retry) found = await TryAsync(q, community).ConfigureAwait(false);
            string shortQ = NameMatcher.ShortQuery(name);
            if (!found && !retry && !anyHits && shortQ.Length > 0)
                await TryAsync(shortQ, false).ConfigureAwait(false);
            if (best != null)
            {
                _index.SetMatch(key, best.AppId);
                Log.Info("Art: matched '" + name + "' to Steam app " + best.AppId + " '" + best.Name + "' (score " + score.ToString("0.00") + ")");
                return new MatchOutcome { AppId = best.AppId };
            }
            if (retry) return new MatchOutcome { Retry = true };
            _index.SetMatch(key, "");
            Log.Info("Art: no confident Steam match for '" + name + "'");
            return new MatchOutcome();
        }

        // ------------------------------------------------------------ Steam CDN

        private async Task<StageResult> FetchSteamAsync(string appId)
        {
            var kinds = ArtKind.Remote.Where(k => _cache.FindSteam(appId, k) == null && !_index.IsNegative(NegSteam(appId, k))).ToList();
            FetchStatus[] statuses = await Task.WhenAll(kinds.Select(k => FetchSteamKindAsync(appId, k))).ConfigureAwait(false);
            bool changed = statuses.Contains(FetchStatus.Ok);
            if (changed) RaiseSoon(AppUsers(appId));   // every game showing this app's art
            return new StageResult
            {
                Changed = changed,
                Retry = statuses.Any(s => s == FetchStatus.Transient || s == FetchStatus.Offline),
            };
        }

        private async Task<FetchStatus> FetchSteamKindAsync(string appId, string kind)
        {
            foreach (string url in SteamEndpoints.ArtUrls(appId, kind))
            {
                FetchStatus s = await DownloadImageAsync(url, _cache.SteamBase(appId, kind)).ConfigureAwait(false);
                if (s != FetchStatus.NotFound) return s;
            }
            if (kind == ArtKind.Header)
            {
                // Newer apps keep their art under hashed paths; the store API knows the real URL.
                AppDetails d = await _steam.AppDetailsAsync(appId).ConfigureAwait(false);
                if (d.Status == FetchStatus.Transient || d.Status == FetchStatus.Offline) return d.Status;
                if (d.HeaderImage.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    FetchStatus s = await DownloadImageAsync(d.HeaderImage, _cache.SteamBase(appId, kind)).ConfigureAwait(false);
                    if (s != FetchStatus.NotFound) return s;
                }
            }
            _index.MarkNegative(NegSteam(appId, kind));
            Log.Info("Art: Steam has no " + kind + " for app " + appId);
            return FetchStatus.NotFound;
        }

        /// <summary>Downloads, validates and stores an image. Invalid/non-image bodies count as NotFound.</summary>
        private async Task<FetchStatus> DownloadImageAsync(string url, string basePath, string bearer = null)
        {
            FetchResult r = await _http.GetAsync(url, bearer).ConfigureAwait(false);
            if (r.Status == FetchStatus.Unauthorized) return FetchStatus.NotFound;
            if (!r.Ok) return r.Status;
            if (!ImageFiles.IsValidImage(r.Body, out string ext))
            {
                Log.Info("Art: ignoring invalid image from " + url);
                return FetchStatus.NotFound;
            }
            ImageFiles.SaveBytes(basePath, ext, r.Body);
            return FetchStatus.Ok;
        }

        // ------------------------------------------------------------ SteamGridDB

        private static string SgdbKey(string appId, string matchKey)
            => appId.Length > 0 ? "sgdb|steam:" + appId : "sgdb|" + matchKey;

        private async Task<StageResult> FetchSgdbAsync(GameRef g, string gk, string appId)
        {
            var kinds = ArtKind.Remote.Where(k =>
                _cache.FindBest(g.Id, appId, k) == null
                && (appId.Length == 0 || _index.IsNegative(NegSteam(appId, k)))
                && !_index.IsNegative(NegSgdb(gk, k))).ToList();
            if (kinds.Count == 0) return new StageResult();

            string cacheKey = SgdbKey(appId, MatchKey(g.Name));
            if (!_index.TryGetMatch(cacheKey, out string sgdbId))
            {
                SearchOutcome found = await _sgdb.FindGameAsync(appId, g.Name).ConfigureAwait(false);
                if (found.Retryable) return new StageResult { Retry = true };
                if (found.Status == FetchStatus.Unauthorized) return new StageResult();
                sgdbId = found.Items.Count > 0 ? found.Items[0].AppId : "";
                _index.SetMatch(cacheKey, sgdbId);
            }
            if (sgdbId.Length == 0)
            {
                foreach (string k in kinds) _index.MarkNegative(NegSgdb(gk, k));
                Log.Info("Art: SteamGridDB has no entry for '" + g.Name + "'");
                return new StageResult();
            }

            var result = new StageResult();
            foreach (string kind in kinds)
            {
                KeyValuePair<FetchStatus, string> asset = await _sgdb.AssetUrlAsync(sgdbId, kind).ConfigureAwait(false);
                FetchStatus s = asset.Key;
                if (s == FetchStatus.Ok) s = await DownloadImageAsync(asset.Value, _cache.SgdbBase(g.Id, kind)).ConfigureAwait(false);
                if (s == FetchStatus.Ok) result.Changed = true;
                else if (s == FetchStatus.Transient || s == FetchStatus.Offline) result.Retry = true;
                else if (s == FetchStatus.NotFound) _index.MarkNegative(NegSgdb(gk, kind));
            }
            return result;
        }
    }
}
