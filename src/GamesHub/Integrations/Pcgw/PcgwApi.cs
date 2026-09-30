using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace GamesHub
{
    /// <summary>Result of a lookup: Ok=false means a network/API failure (do not cache as "not found").</summary>
    public struct PcgwLookup
    {
        public bool Ok;
        public string Title;
        public static PcgwLookup Fail => new PcgwLookup { Ok = false };
        public static PcgwLookup Result(string title) => new PcgwLookup { Ok = true, Title = title };
    }

    public static class PcgwApi
    {
        public const string Base = "https://www.pcgamingwiki.com/w/api.php";

        public static string CargoByAppIdUrl(string appId) =>
            Base + "?action=cargoquery&tables=Infobox_game&fields=_pageName=Page,Steam_AppID"
                 + "&where=" + Uri.EscapeDataString("Infobox_game.Steam_AppID HOLDS \"" + appId + "\"") + "&format=json";

        public static string OpenSearchUrl(string query) =>
            Base + "?action=opensearch&namespace=0&limit=10&redirects=resolve&format=json&search=" + Uri.EscapeDataString(query);

        public static string WikitextUrl(string page) =>
            Base + "?action=parse&prop=wikitext&redirects=1&format=json&page=" + Uri.EscapeDataString(page);

        public static async Task<PcgwLookup> FindByAppIdAsync(string appId)
        {
            string body = await PcgwHttp.GetPcgwAsync(CargoByAppIdUrl(appId)).ConfigureAwait(false);
            if (body == null) return PcgwLookup.Fail;
            try { return PcgwLookup.Result(ParseCargoPages(body).FirstOrDefault()); }
            catch (Exception ex) when (ExpectedErrors.IsJson(ex)) { Log.Warn("PCGW: bad cargo response for appid " + appId, ex); return PcgwLookup.Fail; }
        }

        public static async Task<PcgwLookup> FindByNameAsync(string name)
        {
            string query = PcgwNames.CleanForSearch(name);
            if (query.Length == 0) return PcgwLookup.Result(null);
            string body = await PcgwHttp.GetPcgwAsync(OpenSearchUrl(query)).ConfigureAwait(false);
            if (body == null) return PcgwLookup.Fail;
            try { return PcgwLookup.Result(PcgwNames.BestMatch(name, ParseOpenSearch(body))); }
            catch (Exception ex) when (ExpectedErrors.IsJson(ex)) { Log.Warn("PCGW: bad opensearch response for " + name, ex); return PcgwLookup.Fail; }
        }

        /// <summary>Returns (resolved title, wikitext) or null on failure / missing page.</summary>
        public static async Task<Tuple<string, string>> GetWikitextAsync(string page)
        {
            string body = await PcgwHttp.GetPcgwAsync(WikitextUrl(page)).ConfigureAwait(false);
            if (body == null) return null;
            try { return ParseWikitext(body); }
            catch (Exception ex) when (ExpectedErrors.IsJson(ex)) { Log.Warn("PCGW: bad parse response for " + page, ex); return null; }
        }

        // ---------------------------------------------------------------- JSON parsing (public for tests)

        public static List<string> ParseCargoPages(string json)
        {
            var list = new List<string>();
            var root = Json.DeserializeObject(json) as IDictionary<string, object>;
            if (root == null || !root.TryGetValue("cargoquery", out object cq) || !(cq is IEnumerable rows)) return list;
            foreach (object row in rows)
            {
                var t = Json.Obj(row as IDictionary<string, object>, "title");
                string page = Json.Str(t, "Page");
                if (page.Length > 0 && !list.Contains(page)) list.Add(page);
            }
            return list;
        }

        public static List<string> ParseOpenSearch(string json)
        {
            var list = new List<string>();
            if (Json.DeserializeObject(json) is object[] arr && arr.Length > 1 && arr[1] is IEnumerable titles)
                foreach (object t in titles)
                    if (t is string s && s.Length > 0) list.Add(s);
            return list;
        }

        public static Tuple<string, string> ParseWikitext(string json)
        {
            var root = Json.DeserializeObject(json) as IDictionary<string, object>;
            var parse = Json.Obj(root, "parse");
            if (parse == null) return null; // {"error":{"code":"missingtitle",...}}
            string title = Json.Str(parse, "title");
            object wt = parse.TryGetValue("wikitext", out object w) ? w : null;
            string text = wt is IDictionary<string, object> d ? Json.Str(d, "*") : Convert.ToString(wt);
            return Tuple.Create(title, text ?? "");
        }

        public static string WikiUrl(string title)
        {
            string t = (title ?? "").Trim().Replace(' ', '_');
            string e = Uri.EscapeDataString(t);
            foreach (string keep in new[] { "%3A", "%28", "%29", "%2C", "%21", "%27", "%2F" })
                e = e.Replace(keep, Uri.UnescapeDataString(keep));
            return "https://www.pcgamingwiki.com/wiki/" + e;
        }
    }
}
