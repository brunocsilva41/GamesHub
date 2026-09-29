// OWNER: META agent. Maps a Steam store "appdetails" JSON response to GameInfo.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace GamesHub
{
    public enum MetadataParseStatus { Ok, NotFound, Invalid }

    public sealed class MetadataParseResult
    {
        public MetadataParseStatus Status;
        public GameInfo Info;   // non-null only when Status == Ok
    }

    /// <summary>Pure parsing helpers for https://store.steampowered.com/api/appdetails (no I/O).</summary>
    public static class MetadataParser
    {
        /// <summary>Steam category id → short pt-BR label. Order here is the display order. Unlisted ids
        /// (trading cards, remote play on phone/TV, accessibility flags, family sharing...) are dropped.</summary>
        private static readonly KeyValuePair<int, string>[] CategoryMap =
        {
            P(2, "Um jogador"),
            P(1, "Multijogador"),
            P(20, "MMO"),
            P(49, "PvP"),
            P(36, "PvP online"),
            P(47, "PvP em LAN"),
            P(9, "Cooperativo"),
            P(38, "Coop online"),
            P(39, "Coop local"),
            P(48, "Coop em LAN"),
            P(24, "Tela dividida"),
            P(37, "Tela dividida"),
            P(27, "Multiplataforma"),
            P(44, "Remote Play Together"),
            P(28, "Suporte total a controle"),
            P(18, "Suporte parcial a controle"),
            P(22, "Conquistas"),
            P(23, "Nuvem Steam"),
            P(30, "Oficina Steam"),
            P(51, "Oficina Steam"),
            P(17, "Editor de fases"),
            P(31, "Suporte a VR"),
            P(53, "Suporte a VR"),
            P(54, "Apenas VR"),
        };

        public const string FullController = "Suporte total a controle";
        public const string PartialController = "Suporte parcial a controle";

        private static KeyValuePair<int, string> P(int id, string label) => new KeyValuePair<int, string>(id, label);

        /// <summary>Parses the raw response body for <paramref name="appId"/>. Never throws.</summary>
        public static MetadataParseResult Parse(string appId, string json)
        {
            try
            {
                var root = Json.DeserializeObject(json ?? "") as IDictionary<string, object>;
                var node = Json.Obj(root, appId);
                if (node == null) return new MetadataParseResult { Status = MetadataParseStatus.Invalid };
                var data = Json.Obj(node, "data");
                if (!Json.Bool(node, "success") || data == null)
                    return new MetadataParseResult { Status = MetadataParseStatus.NotFound };
                return new MetadataParseResult { Status = MetadataParseStatus.Ok, Info = Map(appId, data) };
            }
            catch (Exception ex)
            {
                Log.Warn("Metadata: invalid store JSON for app " + appId, ex);
                return new MetadataParseResult { Status = MetadataParseStatus.Invalid };
            }
        }

        private static GameInfo Map(string appId, IDictionary<string, object> d)
        {
            var info = new GameInfo { AppId = appId, FetchedAt = DateTime.Now };
            info.Genres = Descriptions(d, "genres").Select(x => x.Value).Where(s => s != "").Distinct().ToList();

            var catIds = new HashSet<int>(Descriptions(d, "categories").Select(x => x.Key));
            string controller = Json.Str(d, "controller_support").Trim().ToLowerInvariant();
            info.Categories = MapCategories(catIds, controller);
            info.ControllerSupport = controller == "full" || controller == "partial" || catIds.Contains(28) || catIds.Contains(18);

            info.ShortDescription = StripHtml(Json.Str(d, "short_description"));
            info.ReleaseDate = Json.Str(Json.Obj(d, "release_date"), "date").Trim();
            info.Developers = Strings(d, "developers");
            info.Publishers = Strings(d, "publishers");
            info.Metacritic = (int)Json.Long(Json.Obj(d, "metacritic"), "score");
            info.Website = Json.Str(d, "website").Trim();
            return info;
        }

        /// <summary>Maps category ids to short pt-BR labels (deduplicated, display order), adding the
        /// controller label from the "controller_support" field ("full"/"partial") when the category is absent.</summary>
        public static List<string> MapCategories(ICollection<int> ids, string controllerSupport = "")
        {
            var set = new HashSet<int>(ids ?? new int[0]);
            if (controllerSupport == "full") set.Add(28);
            else if (controllerSupport == "partial" && !set.Contains(28)) set.Add(18);
            if (set.Contains(28)) set.Remove(18);
            // Generic "PvP" is redundant when a more specific PvP flavour is present.
            if (set.Contains(36) || set.Contains(47)) set.Remove(49);

            var result = new List<string>();
            foreach (var kv in CategoryMap)
                if (set.Contains(kv.Key) && !result.Contains(kv.Value)) result.Add(kv.Value);
            return result;
        }

        /// <summary>Strips HTML tags, decodes entities and collapses whitespace.</summary>
        public static string StripHtml(string html)
        {
            if (string.IsNullOrEmpty(html)) return "";
            string s = Regex.Replace(html, @"<\s*(br|/p|/li|/h\d)\s*/?\s*>", " ", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"<[^>]*>", "");
            s = WebUtility.HtmlDecode(s);
            s = Regex.Replace(s, @"\s+", " ");
            return s.Trim();
        }

        /// <summary>Items of an array of { id, description } objects.</summary>
        private static IEnumerable<KeyValuePair<int, string>> Descriptions(IDictionary<string, object> d, string key)
        {
            if (!d.TryGetValue(key, out object v) || !(v is IEnumerable list) || v is string) yield break;
            foreach (object o in list)
            {
                var item = o as IDictionary<string, object>;
                if (item == null) continue;
                int.TryParse(Json.Str(item, "id"), out int id);
                yield return new KeyValuePair<int, string>(id, Json.Str(item, "description").Trim());
            }
        }

        private static List<string> Strings(IDictionary<string, object> d, string key)
        {
            var result = new List<string>();
            if (!d.TryGetValue(key, out object v) || !(v is IEnumerable list) || v is string) return result;
            foreach (object o in list)
            {
                string s = Convert.ToString(o)?.Trim();
                if (!string.IsNullOrEmpty(s) && !result.Contains(s)) result.Add(s);
            }
            return result;
        }
    }
}
