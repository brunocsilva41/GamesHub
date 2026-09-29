// OWNER: STEAMDATA agent. Active Steam user (loginusers.vdf) and per-user play stats (localconfig.vdf).
using System;
using System.Collections.Generic;
using System.IO;

namespace GamesHub
{
    public sealed class SteamAppPlay
    {
        public long PlaytimeMinutes;
        public long LastPlayedUnix;
    }

    public static class SteamUsers
    {
        public const ulong SteamId64Base = 76561197960265728UL;

        /// <summary>SteamID64 → 32-bit account id (folder name under userdata). "" when not a valid id64.</summary>
        public static string AccountIdFromSteamId64(string id64)
        {
            if (!ulong.TryParse((id64 ?? "").Trim(), out ulong v) || v <= SteamId64Base) return "";
            ulong acc = v - SteamId64Base;
            return acc > uint.MaxValue ? "" : acc.ToString();
        }

        /// <summary>The SteamID64 of the user with MostRecent "1"; else the one with the highest Timestamp;
        /// else the first listed. "" when there are no users.</summary>
        public static string PickActiveUser(SteamKv loginUsersDoc)
        {
            SteamKv users = loginUsersDoc?.Node("users");
            if (users == null) return "";
            string best = "", first = "";
            long bestTs = long.MinValue;
            foreach (string id in users.Order)
            {
                SteamKv u = users.Node(id);
                if (u == null || !SteamManifests.IsDigits(id)) continue;
                if (first.Length == 0) first = id;
                if (u.Str("MostRecent").Trim() == "1") return id;
                long ts = u.Long("Timestamp", long.MinValue);
                if (ts > bestTs) { bestTs = ts; best = id; }
            }
            return best.Length > 0 ? best : first;
        }

        /// <summary>Chooses userdata/&lt;accountId&gt;/config/localconfig.vdf for the active account; if missing,
        /// the most recently modified localconfig.vdf among all userdata folders. Null if none.</summary>
        public static string FindLocalConfig(string steamRoot, string accountId)
        {
            string userdata = Path.Combine(steamRoot ?? "", "userdata");
            if (!Directory.Exists(userdata)) return null;
            if (!string.IsNullOrEmpty(accountId))
            {
                string f = LocalConfigPath(userdata, accountId);
                if (File.Exists(f)) return f;
            }
            string best = null;
            DateTime bestTime = DateTime.MinValue;
            foreach (string dir in Directory.GetDirectories(userdata))
            {
                string f = LocalConfigPath(userdata, Path.GetFileName(dir));
                if (!File.Exists(f)) continue;
                DateTime t = File.GetLastWriteTimeUtc(f);
                if (best == null || t > bestTime) { best = f; bestTime = t; }
            }
            return best;
        }

        private static string LocalConfigPath(string userdata, string id) => Path.Combine(userdata, id, "config", "localconfig.vdf");

        public static readonly string[] AppsPath = { "UserLocalConfigStore", "Software", "Valve", "Steam", "apps" };

        /// <summary>Streams localconfig.vdf and extracts per-app Playtime (minutes) / LastPlayed (unix seconds).
        /// Only the apps subtree is materialized.</summary>
        public static Dictionary<string, SteamAppPlay> ReadPlayStats(TextReader reader)
        {
            return ExtractPlayStats(SteamKvParser.ParsePath(reader, AppsPath));
        }

        public static Dictionary<string, SteamAppPlay> ExtractPlayStats(SteamKv apps)
        {
            var result = new Dictionary<string, SteamAppPlay>(StringComparer.Ordinal);
            if (apps == null) return result;
            foreach (var kv in apps.Children)
            {
                if (!SteamManifests.IsDigits(kv.Key)) continue;
                long minutes = Math.Max(0, kv.Value.Long("Playtime"));
                long last = Math.Max(0, kv.Value.Long("LastPlayed"));
                if (minutes == 0 && last == 0) continue;
                result[kv.Key] = new SteamAppPlay { PlaytimeMinutes = minutes, LastPlayedUnix = last };
            }
            return result;
        }

        /// <summary>Unix seconds → local DateTime; null for 0/negative/out of range.</summary>
        public static DateTime? FromUnix(long seconds)
        {
            if (seconds <= 0) return null;
            try { return DateTimeOffset.FromUnixTimeSeconds(seconds).LocalDateTime; }
            catch (ArgumentOutOfRangeException) { return null; }
        }
    }
}
