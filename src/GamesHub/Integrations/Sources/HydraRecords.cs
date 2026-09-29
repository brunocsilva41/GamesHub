// OWNER: SOURCES agent. Pure parsing of Hydra Launcher library records (LevelDB "!games!<shop>:<objectId>"
// keys whose values are JSON objects) into Game objects.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace GamesHub
{
    public static class HydraRecords
    {
        public const string GamesPrefix = "!games!";

        public static string GameId(string shop, string objectId) => "hydra:" + shop + ":" + objectId;

        /// <summary>URI Hydra itself uses in its desktop shortcuts.</summary>
        public static string RunUri(string shop, string objectId)
            => "hydralauncher://run?shop=" + Uri.EscapeDataString(shop ?? "") + "&objectId=" + Uri.EscapeDataString(objectId ?? "");

        /// <summary>Parses one record. Returns null when the record is deleted, malformed or not installed
        /// (no executablePath, or fileExists(executablePath) is false).</summary>
        public static Game Parse(string key, string json, Func<string, bool> fileExists)
        {
            if (string.IsNullOrEmpty(json)) return null;
            IDictionary<string, object> d;
            try { d = Json.DeserializeObject(json) as IDictionary<string, object>; }
            catch (Exception ex) { Log.Warn("Hydra: bad JSON for " + key, ex); return null; }
            if (d == null || Json.Bool(d, "isDeleted")) return null;

            string shop = Json.Str(d, "shop"), objectId = Json.Str(d, "objectId");
            if ((shop == "" || objectId == "") && key != null && key.StartsWith(GamesPrefix, StringComparison.Ordinal))
            {
                string rest = key.Substring(GamesPrefix.Length);
                int colon = rest.IndexOf(':');
                if (colon > 0)
                {
                    if (shop == "") shop = rest.Substring(0, colon);
                    if (objectId == "") objectId = rest.Substring(colon + 1);
                }
            }
            if (shop == "" || objectId == "") return null;

            string exe = Json.Str(d, "executablePath").Trim();
            if (exe == "" || !IsLocalPath(exe) || !fileExists(exe)) return null;

            string title = Json.Str(d, "title").Trim();
            bool steam = string.Equals(shop, "steam", StringComparison.OrdinalIgnoreCase);
            var g = new Game
            {
                Id = GameId(shop, objectId),
                Name = title != "" ? title : Path.GetFileNameWithoutExtension(exe),
                Source = "hydra",
                Platform = steam ? "Steam" : "PC",
                SteamAppId = steam && IsDigits(objectId) ? objectId : "",
                LaunchTarget = exe,
                Exe = exe,
                InstallDir = InstallDirFromExe(exe),
                PlaySeconds = Math.Max(0, Json.Long(d, "playTimeInMilliseconds") / 1000),
                LastPlayed = ParseDate(Json.Str(d, "lastTimePlayed")),
            };
            DateTime? added = ParseDate(Json.Str(d, "addedToLibraryAt"));
            if (added.HasValue) g.AddedAt = added.Value;
            return g;
        }

        /// <summary>Game root from its exe. Unreal "…\&lt;Game&gt;\&lt;Project&gt;\Binaries\Win64\x.exe" → "…\&lt;Game&gt;".
        /// Never returns a drive root ("" instead).</summary>
        public static string InstallDirFromExe(string exe)
        {
            try
            {
                string dir = Path.GetDirectoryName(exe) ?? "";
                string leaf = Path.GetFileName(dir);
                string parent = Path.GetDirectoryName(dir) ?? "";
                if ((leaf.Equals("Win64", StringComparison.OrdinalIgnoreCase) || leaf.Equals("Win32", StringComparison.OrdinalIgnoreCase))
                    && Path.GetFileName(parent).Equals("Binaries", StringComparison.OrdinalIgnoreCase))
                {
                    string project = Path.GetDirectoryName(parent) ?? "";          // <Project>
                    string root = Path.GetDirectoryName(project) ?? "";            // <Game>
                    if (root != "" && Path.GetPathRoot(root) != root) dir = root;
                    else if (project != "") dir = project;
                }
                return dir == "" || Path.GetPathRoot(dir) == dir ? "" : dir;
            }
            catch (Exception ex) { Log.Warn("Hydra: bad executable path " + exe, ex); return ""; }
        }

        private static bool IsLocalPath(string p)
            => p.Length > 2 && ((char.IsLetter(p[0]) && p[1] == ':') || p.StartsWith(@"\\")) && p.IndexOfAny(Path.GetInvalidPathChars()) < 0;

        private static bool IsDigits(string s)
        {
            if (s.Length == 0) return false;
            foreach (char c in s) if (c < '0' || c > '9') return false;
            return true;
        }

        private static DateTime? ParseDate(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime dt))
                return dt.Kind == DateTimeKind.Utc ? dt.ToLocalTime() : dt;
            return null;
        }
    }
}
