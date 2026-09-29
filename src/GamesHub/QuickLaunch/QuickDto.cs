using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace GamesHub
{
    public static class QuickDto
    {
        public const string ArtBase = "https://" + WebViewEnv.ArtHost + "/";

        public static List<Dictionary<string, object>> Items(IEnumerable<QuickSearchHit> hits)
            => hits.Select(Item).ToList();

        public static Dictionary<string, object> Item(QuickSearchHit h)
        {
            Game g = h.Game;
            Artwork a = g.Art ?? new Artwork();
            return new Dictionary<string, object>
            {
                ["id"] = g.Id,
                ["name"] = g.Name,
                ["platform"] = g.Platform,
                ["collections"] = g.Collections ?? new List<string>(),
                ["running"] = g.Running,
                ["favorite"] = g.Favorite,
                ["hidden"] = g.Hidden,
                ["lastPlayed"] = g.LastPlayed?.ToString("o", CultureInfo.InvariantCulture),
                ["playSeconds"] = g.PlaySeconds,
                ["header"] = ArtUrl(a.Header),
                ["icon"] = ArtUrl(a.Icon),
                ["hl"] = h.Highlights,
            };
        }

        /// <summary>"steam-730/header.jpg" → https://art.gameshub.example/steam-730/header.jpg?v=&lt;mtime ticks&gt; (null when empty).</summary>
        public static string ArtUrl(string rel)
        {
            if (string.IsNullOrWhiteSpace(rel)) return null;
            string clean = rel.Replace('\\', '/').TrimStart('/');
            string url = ArtBase + string.Join("/", clean.Split('/').Select(Uri.EscapeDataString));
            try
            {
                string file = Path.Combine(AppPaths.ArtDir, clean.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(file)) url += "?v=" + File.GetLastWriteTimeUtc(file).Ticks.ToString(CultureInfo.InvariantCulture);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException)
            {
                Log.Warn("Quick-launch: bad art path " + rel, ex);
            }
            return url;
        }
    }
}
