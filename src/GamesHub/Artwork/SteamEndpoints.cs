using System;
using System.Collections.Generic;

namespace GamesHub
{
    /// <summary>URL building for Steam and SteamGridDB endpoints (pure, testable).</summary>
    public static class SteamEndpoints
    {
        public const string SharedCdn = "https://shared.cloudflare.steamstatic.com/store_item_assets/steam/apps/";
        public const string LegacyCdn = "https://cdn.cloudflare.steamstatic.com/steam/apps/";
        public const string SgdbApi = "https://www.steamgriddb.com/api/v2/";

        /// <summary>Steam CDN file names to try for a kind, in order of preference.</summary>
        public static string[] FileNames(string kind)
        {
            switch (kind)
            {
                case ArtKind.Header: return new[] { "header.jpg" };
                case ArtKind.Capsule: return new[] { "library_600x900_2x.jpg", "library_600x900.jpg" };
                case ArtKind.Hero: return new[] { "library_hero.jpg" };
                case ArtKind.Logo: return new[] { "logo.png" };
                default: return new string[0];
            }
        }

        /// <summary>Candidate CDN URLs for a kind: each file on the new shared CDN, then the legacy CDN.</summary>
        public static List<string> ArtUrls(string appId, string kind)
        {
            var urls = new List<string>();
            foreach (string file in FileNames(kind))
            {
                urls.Add(SharedCdn + appId + "/" + file);
                urls.Add(LegacyCdn + appId + "/" + file);
            }
            return urls;
        }

        public static string AppDetails(string appId)
            => "https://store.steampowered.com/api/appdetails?appids=" + appId + "&l=english&cc=US";

        public static string StoreSearch(string term)
            => "https://store.steampowered.com/api/storesearch/?term=" + Uri.EscapeDataString(term) + "&l=english&cc=US";

        public static string CommunitySearch(string term)
            => "https://steamcommunity.com/actions/SearchApps/" + Uri.EscapeDataString(term);

        /// <summary>Small capsule used as the thumbnail of manual search results.</summary>
        public static string SmallCapsule(string appId) => SharedCdn + appId + "/capsule_231x87.jpg";

        // ------------------------------------------------------------ SteamGridDB

        public static string SgdbAutocomplete(string term) => SgdbApi + "search/autocomplete/" + Uri.EscapeDataString(term);
        public static string SgdbBySteamId(string appId) => SgdbApi + "games/steam/" + appId;

        public static string SgdbAssets(string sgdbGameId, string kind)
        {
            switch (kind)
            {
                case ArtKind.Capsule: return SgdbApi + "grids/game/" + sgdbGameId + "?dimensions=600x900&types=static&nsfw=false";
                case ArtKind.Header: return SgdbApi + "grids/game/" + sgdbGameId + "?dimensions=920x430,460x215&types=static&nsfw=false";
                case ArtKind.Hero: return SgdbApi + "heroes/game/" + sgdbGameId + "?types=static&nsfw=false";
                case ArtKind.Logo: return SgdbApi + "logos/game/" + sgdbGameId + "?types=static&nsfw=false";
                default: return null;
            }
        }
    }
}
