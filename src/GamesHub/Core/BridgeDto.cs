using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GamesHub
{
    /// <summary>Pure mapping between contract types and the camelCase JSON shapes of the bridge protocol.</summary>
    public static class BridgeDto
    {
        public const string AppHost = WebViewEnv.AppHost;
        public const string ArtHost = WebViewEnv.ArtHost;
        public const string AppOrigin = "https://" + AppHost;
        public const string ArtOrigin = "https://" + ArtHost;
        public const string StartUrl = AppOrigin + "/index.html";

        public static readonly string[] ArtKinds = { "header", "capsule", "hero", "logo", "icon" };

        /// <summary>True for URIs served from the app virtual host.</summary>
        public static bool IsAppUri(string uri)
            => Uri.TryCreate(uri, UriKind.Absolute, out Uri u)
               && u.Scheme == Uri.UriSchemeHttps
               && string.Equals(u.Host, AppHost, StringComparison.OrdinalIgnoreCase)
               && u.IsDefaultPort;

        public static Dictionary<string, object> Game(Game g, Func<string, long?> artStamp)
        {
            Artwork art = g.Art ?? new Artwork();
            return new Dictionary<string, object>
            {
                ["id"] = g.Id ?? "",
                ["name"] = g.Name ?? "",
                ["platform"] = g.Platform ?? "",
                ["source"] = g.Source ?? "",
                ["ext"] = g.Ext ?? "",
                ["filePath"] = g.FilePath ?? "",
                ["installDir"] = g.InstallDir ?? "",
                ["launchArgs"] = g.LaunchArgs ?? "",
                ["steamAppId"] = g.SteamAppId ?? "",
                ["favorite"] = g.Favorite,
                ["hidden"] = g.Hidden,
                ["collections"] = (g.Collections ?? new List<string>()).ToList(),
                ["lastPlayed"] = g.LastPlayed.HasValue ? IsoDate(g.LastPlayed.Value) : null,
                ["playSeconds"] = g.PlaySeconds,
                ["addedAt"] = IsoDate(g.AddedAt),
                ["running"] = g.Running,
                ["sizeBytes"] = g.SizeBytes,
                ["updatePending"] = g.UpdatePending,
                ["broken"] = g.Broken,
                ["brokenReason"] = g.BrokenReason ?? "",
                ["genres"] = (g.Genres ?? new List<string>()).ToList(),
                ["variants"] = (g.Variants ?? new List<GameVariant>())
                    .Where(v => v != null)
                    .Select(v => new Dictionary<string, object> { ["id"] = v.Id ?? "", ["label"] = v.Label ?? "" })
                    .ToList(),
                ["art"] = new Dictionary<string, object>
                {
                    ["header"] = ArtUrl(art.Header, artStamp),
                    ["capsule"] = ArtUrl(art.Capsule, artStamp),
                    ["hero"] = ArtUrl(art.Hero, artStamp),
                    ["logo"] = ArtUrl(art.Logo, artStamp),
                    ["icon"] = ArtUrl(art.Icon, artStamp),
                },
            };
        }

        /// <summary>"https://art.gameshub.example/&lt;rel&gt;?v=&lt;stamp&gt;" or null when the path is empty,
        /// unsafe, or the file does not exist (stamp == null).</summary>
        public static string ArtUrl(string rel, Func<string, long?> artStamp)
        {
            if (string.IsNullOrWhiteSpace(rel)) return null;
            string norm = rel.Replace('\\', '/').TrimStart('/');
            string[] segments = norm.Split('/');
            if (segments.Any(s => s.Length == 0 || s == "." || s == ".." || s.IndexOf(':') >= 0)) return null;
            long? stamp = artStamp(norm);
            if (stamp == null) return null;
            return ArtOrigin + "/" + string.Join("/", segments.Select(Uri.EscapeDataString)) + "?v=" + stamp.Value;
        }

        /// <summary>Default artStamp: last write time (UTC ticks) of the file under AppPaths.ArtDir.</summary>
        public static long? FileStamp(string rel)
        {
            try
            {
                string full = PathGuard.ResolveUnder(AppPaths.ArtDir, rel.Replace('/', Path.DirectorySeparatorChar));
                return full != null && File.Exists(full) ? File.GetLastWriteTimeUtc(full).Ticks : (long?)null;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is NotSupportedException || ex is UnauthorizedAccessException)
            {
                Log.Warn("Art stamp failed for " + rel, ex);
                return null;
            }
        }

        /// <summary>ISO 8601 in UTC ("2024-05-01T12:30:00.000Z"). Unspecified kinds are treated as local time.</summary>
        public static string IsoDate(DateTime dt)
        {
            if (dt.Kind == DateTimeKind.Unspecified) dt = DateTime.SpecifyKind(dt, DateTimeKind.Local);
            return dt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture);
        }

        public static Dictionary<string, object> OpResultData(OpResult r) => new Dictionary<string, object>
        {
            ["message"] = r?.Message ?? "",
            ["undoToken"] = r?.UndoToken,
            ["gameId"] = r?.GameId,
        };

        /// <summary>Used inside lists (addFiles) where each entry carries its own ok flag.</summary>
        public static Dictionary<string, object> OpResultEntry(OpResult r)
        {
            Dictionary<string, object> d = OpResultData(r);
            d["ok"] = r != null && r.Ok;
            return d;
        }

        public static Dictionary<string, object> Reply(object id, bool ok, object data, string error)
        {
            var d = new Dictionary<string, object> { ["type"] = "reply", ["id"] = id, ["ok"] = ok };
            if (ok) d["data"] = data ?? new Dictionary<string, object>();
            else
            {
                d["error"] = string.IsNullOrEmpty(error) ? "Algo deu errado." : error;
                if (data != null) d["data"] = data;
            }
            return d;
        }

        public static Dictionary<string, object> Event(string name, object data)
            => new Dictionary<string, object> { ["type"] = "event", ["name"] = name, ["data"] = data ?? new Dictionary<string, object>() };

        public static Dictionary<string, object> Update(UpdateInfo info) => new Dictionary<string, object>
        {
            ["available"] = info != null && info.Available,
            ["version"] = info?.Version ?? "",
            ["notes"] = info?.Notes ?? "",
            ["pageUrl"] = info?.PageUrl ?? "",
        };

        /// <summary>Most recently played visible games, newest first.</summary>
        public static List<Game> RecentGames(IEnumerable<Game> games, int count)
            => (games ?? Enumerable.Empty<Game>())
                .Where(g => g != null && !g.Hidden && g.LastPlayed.HasValue)
                .OrderByDescending(g => g.LastPlayed.GetValueOrDefault())
                .ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
                .Take(count)
                .ToList();

        /// <summary>Parses the "edit" object of updateGame. Absent / wrongly typed fields stay null (unchanged).</summary>
        public static GameEdit ParseEdit(IDictionary<string, object> d)
        {
            var e = new GameEdit();
            if (d == null) return e;
            e.Name = StringOrNull(d, "name");
            e.LaunchArgs = StringOrNull(d, "launchArgs");
            e.SteamAppId = StringOrNull(d, "steamAppId");
            if (e.SteamAppId != null)
            {
                e.SteamAppId = e.SteamAppId.Trim();
                if (e.SteamAppId.Length > 0 && !e.SteamAppId.All(char.IsDigit))
                    throw new BridgeException("O ID da Steam deve conter apenas números.");
            }
            if (d.TryGetValue("favorite", out object fav) && fav is bool fb) e.Favorite = fb;
            if (d.TryGetValue("hidden", out object hid) && hid is bool hb) e.Hidden = hb;
            if (d.TryGetValue("collections", out object col) && col is IEnumerable list && !(col is string))
            {
                e.Collections = list.Cast<object>()
                    .Select(o => (Convert.ToString(o) ?? "").Trim())
                    .Where(s => s.Length > 0)
                    .Distinct(StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
            }
            return e;
        }

        private static string StringOrNull(IDictionary<string, object> d, string key)
            => d.TryGetValue(key, out object v) && v is string s ? s : null;
    }

    /// <summary>Thrown by bridge handlers for expected failures; Message is pt-BR and shown to the user.</summary>
    public sealed class BridgeException : Exception
    {
        public BridgeException(string message) : base(message) { }
    }
}
