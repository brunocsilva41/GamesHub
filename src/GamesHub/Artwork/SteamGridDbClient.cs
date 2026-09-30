using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace GamesHub
{
    /// <summary>Minimal SteamGridDB v2 client (only used when the user configured an API key).</summary>
    public sealed class SteamGridDbClient
    {
        private readonly HttpFetcher _http;
        private readonly Func<string> _key;
        private volatile string _rejectedKey;

        public SteamGridDbClient(HttpFetcher http, Func<string> apiKey) { _http = http; _key = apiKey; }

        public bool Enabled
        {
            get
            {
                string k = (_key() ?? "").Trim();
                return k.Length > 0 && k != _rejectedKey;
            }
        }

        private async Task<FetchResult> GetAsync(string url)
        {
            string key = (_key() ?? "").Trim();
            FetchResult r = await _http.GetAsync(url, key).ConfigureAwait(false);
            if (r.Status == FetchStatus.Unauthorized && _rejectedKey != key)
            {
                _rejectedKey = key;
                Log.Warn("Art: SteamGridDB rejected the API key; SteamGridDB disabled until the key changes");
            }
            return r;
        }

        /// <summary>SteamGridDB game id for a Steam app id, or by fuzzy name. Items empty = not found.</summary>
        public async Task<SearchOutcome> FindGameAsync(string steamAppId, string name)
        {
            if (ArtKind.IsAppId(steamAppId))
            {
                FetchResult r = await GetAsync(SteamEndpoints.SgdbBySteamId(steamAppId)).ConfigureAwait(false);
                var byId = new SearchOutcome { Status = r.Status };
                if (r.Ok)
                {
                    IDictionary<string, object> data = Json.Obj(Parse(r.Text), "data");
                    string id = Json.Str(data, "id");
                    if (id.Length > 0) byId.Items.Add(new MatchCandidate(id, Json.Str(data, "name")));
                }
                if (byId.Items.Count > 0 || byId.Retryable) return byId;
            }
            var outcome = new SearchOutcome();
            foreach (string variant in SearchNames(name))
            {
                string term = NameMatcher.SearchQuery(variant);
                if (term.Length == 0) continue;
                FetchResult s = await GetAsync(SteamEndpoints.SgdbAutocomplete(term)).ConfigureAwait(false);
                outcome = new SearchOutcome { Status = s.Status };
                if (!s.Ok) return outcome;
                var candidates = new List<MatchCandidate>();
                var released = new Dictionary<string, long>();
                foreach (IDictionary<string, object> e in DataArray(s.Text))
                {
                    candidates.Add(new MatchCandidate(Json.Str(e, "id"), Json.Str(e, "name")));
                    released[Json.Str(e, "id")] = Json.Long(e, "release_date");
                }
                MatchCandidate best = PreferNewestExact(variant, candidates, released) ?? NameMatcher.PickBest(variant, candidates);
                if (best == null) continue;
                outcome.Items.Add(best);
                return outcome;
            }
            return outcome;
        }

        /// <summary>Several entries with exactly the query's name (e.g. "Point Blank" 1993 arcade vs 2008 PC FPS):
        /// the newest release is almost always the PC game a launcher has. Null when fewer than two exact names.</summary>
        public static MatchCandidate PreferNewestExact(string name, List<MatchCandidate> candidates, IDictionary<string, long> released)
        {
            string q = NameMatcher.Normalize(name).Replace(" ", "");
            var exact = candidates.Where(c => NameMatcher.Normalize(c.Name).Replace(" ", "") == q).ToList();
            if (exact.Count < 2) return null;
            return exact.OrderByDescending(c => released.TryGetValue(c.AppId, out long r) ? r : 0).First();
        }

        /// <summary>The name, then without a trailing client word ("Roblox Player" → "Roblox").</summary>
        public static IEnumerable<string> SearchNames(string name)
        {
            yield return name ?? "";
            string trimmed = System.Text.RegularExpressions.Regex.Replace(name ?? "", @"\s+(Player|Client)$", "",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
            if (trimmed.Length > 0 && !trimmed.Equals(name, StringComparison.OrdinalIgnoreCase)) yield return trimmed;
        }

        /// <summary>URL of the top-rated asset of a kind for a SteamGridDB game ("" = none).</summary>
        public async Task<KeyValuePair<FetchStatus, string>> AssetUrlAsync(string sgdbGameId, string kind)
        {
            string endpoint = SteamEndpoints.SgdbAssets(sgdbGameId, kind);
            if (endpoint == null) return new KeyValuePair<FetchStatus, string>(FetchStatus.NotFound, "");
            FetchResult r = await GetAsync(endpoint).ConfigureAwait(false);
            if (!r.Ok) return new KeyValuePair<FetchStatus, string>(r.Status, "");
            string found = DataArray(r.Text).Select(e => Json.Str(e, "url")).FirstOrDefault(IsSgdbCdnUrl);
            return found != null
                ? new KeyValuePair<FetchStatus, string>(FetchStatus.Ok, found)
                : new KeyValuePair<FetchStatus, string>(FetchStatus.NotFound, "");
        }

        /// <summary>Pure: only images served by SteamGridDB itself (the API returns https://cdn2.steamgriddb.com/...):
        /// https, a steamgriddb.com subdomain, default port, no credentials. Anything else in the JSON is ignored,
        /// so a tampered or compromised response cannot make us fetch arbitrary hosts (intranet, file shares...).</summary>
        public static bool IsSgdbCdnUrl(string url)
        {
            if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out Uri u)) return false;
            if (u.Scheme != Uri.UriSchemeHttps || !u.IsDefaultPort || u.UserInfo.Length > 0) return false;
            string host = u.IdnHost.TrimEnd('.');
            return u.HostNameType == UriHostNameType.Dns
                   && host.EndsWith(".steamgriddb.com", StringComparison.OrdinalIgnoreCase)
                   && host.Length > ".steamgriddb.com".Length;
        }

        private static IDictionary<string, object> Parse(string json)
        {
            try { return Json.DeserializeObject(json) as IDictionary<string, object>; }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
            {
                Log.Warn("Art: unexpected JSON from SteamGridDB", ex);
                return null;
            }
        }

        private static IEnumerable<IDictionary<string, object>> DataArray(string json)
        {
            IDictionary<string, object> doc = Parse(json);
            if (doc == null || !doc.TryGetValue("data", out object data) || !(data is IEnumerable arr) || data is string) yield break;
            foreach (object o in arr)
                if (o is IDictionary<string, object> e) yield return e;
        }
    }
}
