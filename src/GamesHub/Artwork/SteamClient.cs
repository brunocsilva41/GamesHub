// OWNER: ART agent.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GamesHub
{
    /// <summary>Result of a remote search: candidates in rank order + whether the call failed transiently.</summary>
    public sealed class SearchOutcome
    {
        public List<MatchCandidate> Items = new List<MatchCandidate>();
        public FetchStatus Status = FetchStatus.Ok;
        public bool Retryable => Status == FetchStatus.Transient || Status == FetchStatus.Offline;
    }

    public sealed class AppDetails
    {
        public FetchStatus Status = FetchStatus.Ok;
        public string Name = "";
        public string HeaderImage = "";
    }

    /// <summary>Steam store/community JSON APIs (search, app details). Parsing is static and testable.</summary>
    public sealed class SteamClient
    {
        private readonly HttpFetcher _http;
        public SteamClient(HttpFetcher http) { _http = http; }

        public async Task<SearchOutcome> StoreSearchAsync(string term)
        {
            FetchResult r = await _http.GetAsync(SteamEndpoints.StoreSearch(term)).ConfigureAwait(false);
            return new SearchOutcome { Status = r.Status, Items = r.Ok ? ParseStoreSearch(r.Text) : new List<MatchCandidate>() };
        }

        public async Task<SearchOutcome> CommunitySearchAsync(string term)
        {
            FetchResult r = await _http.GetAsync(SteamEndpoints.CommunitySearch(term)).ConfigureAwait(false);
            return new SearchOutcome { Status = r.Status, Items = r.Ok ? ParseCommunitySearch(r.Text) : new List<MatchCandidate>() };
        }

        public async Task<AppDetails> AppDetailsAsync(string appId)
        {
            FetchResult r = await _http.GetAsync(SteamEndpoints.AppDetails(appId)).ConfigureAwait(false);
            if (!r.Ok) return new AppDetails { Status = r.Status };
            AppDetails d = ParseAppDetails(r.Text, appId);
            return d ?? new AppDetails { Status = FetchStatus.NotFound };
        }

        // ------------------------------------------------------------ parsing

        /// <summary>storesearch: { total, items: [ { type, name, id, tiny_image } ] }</summary>
        public static List<MatchCandidate> ParseStoreSearch(string json)
        {
            var list = new List<MatchCandidate>();
            var doc = SafeParse(json) as IDictionary<string, object>;
            if (doc == null || !(doc.TryGetValue("items", out object items) && items is IEnumerable arr)) return list;
            foreach (object o in arr)
            {
                var e = o as IDictionary<string, object>;
                if (e == null) continue;
                string type = Json.Str(e, "type", "app");
                if (type != "app") continue;
                string id = Json.Str(e, "id");
                if (ArtKind.IsAppId(id)) list.Add(new MatchCandidate(id, Json.Str(e, "name"), Json.Str(e, "tiny_image")));
            }
            return list;
        }

        /// <summary>SearchApps: [ { appid, name, icon, logo } ]</summary>
        public static List<MatchCandidate> ParseCommunitySearch(string json)
        {
            var list = new List<MatchCandidate>();
            if (!(SafeParse(json) is IEnumerable arr) || arr is string) return list;
            foreach (object o in arr)
            {
                var e = o as IDictionary<string, object>;
                if (e == null) continue;
                string id = Json.Str(e, "appid");
                if (ArtKind.IsAppId(id)) list.Add(new MatchCandidate(id, Json.Str(e, "name"), Json.Str(e, "icon")));
            }
            return list;
        }

        /// <summary>appdetails: { "&lt;id&gt;": { success, data: { name, header_image } } } → null when unknown.</summary>
        public static AppDetails ParseAppDetails(string json, string appId)
        {
            var doc = SafeParse(json) as IDictionary<string, object>;
            IDictionary<string, object> entry = Json.Obj(doc, appId);
            if (entry == null || !Json.Bool(entry, "success")) return null;
            IDictionary<string, object> data = Json.Obj(entry, "data");
            if (data == null) return null;
            return new AppDetails { Name = Json.Str(data, "name"), HeaderImage = Json.Str(data, "header_image") };
        }

        private static object SafeParse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return Json.DeserializeObject(json); }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
            {
                Log.Warn("Art: unexpected JSON from Steam (" + json.Length + " chars)", ex);
                return null;
            }
        }
    }
}
