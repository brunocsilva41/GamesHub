// OWNER: PCGW agent. Disk cache in AppPaths.CacheDir\pcgw\: lookups (appid/name → page title) and page rows.
// Positive entries live 14 days, negative ones 3 days.
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace GamesHub
{
    public sealed class PcgwLookupEntry
    {
        public string Key = "";
        public string Title;           // null = not found
        public string Source = "";     // "pcgw" | "ludusavi"
        public long FetchedTicks;
    }

    public sealed class PcgwPageEntry
    {
        public string Title = "";
        public string Source = "";
        public long FetchedTicks;
        public List<PcgwRow> Rows = new List<PcgwRow>();
    }

    public sealed class PcgwCache
    {
        public static readonly TimeSpan PositiveTtl = TimeSpan.FromDays(14);
        public static readonly TimeSpan NegativeTtl = TimeSpan.FromDays(3);

        private readonly string _dir;
        public PcgwCache(string dir) { _dir = dir; }
        public string Dir => _dir;

        public static bool IsFresh(long ticks, bool positive, DateTime nowUtc) =>
            nowUtc - new DateTime(ticks, DateTimeKind.Utc) < (positive ? PositiveTtl : NegativeTtl);

        /// <summary>Fresh cached lookup, or null.</summary>
        public PcgwLookupEntry GetLookup(string key)
        {
            var e = Json.Load<PcgwLookupEntry>(FileFor("lookup", key), null);
            if (e == null || e.Key != key || !IsFresh(e.FetchedTicks, e.Title != null, DateTime.UtcNow)) return null;
            return e;
        }

        public void PutLookup(string key, string title, string source) =>
            Save(FileFor("lookup", key), new PcgwLookupEntry { Key = key, Title = title, Source = source, FetchedTicks = DateTime.UtcNow.Ticks });

        public PcgwPageEntry GetPage(string title)
        {
            var e = Json.Load<PcgwPageEntry>(FileFor("page", title), null);
            if (e == null || e.Rows == null || !IsFresh(e.FetchedTicks, true, DateTime.UtcNow)) return null;
            return e;
        }

        public void PutPage(string title, string source, List<PcgwRow> rows) =>
            Save(FileFor("page", title), new PcgwPageEntry { Title = title, Source = source, Rows = rows, FetchedTicks = DateTime.UtcNow.Ticks });

        private void Save(string file, object value)
        {
            try { Json.Save(file, value); }
            catch (Exception ex) { Log.Warn("PCGW: cannot write cache " + file, ex); }
        }

        private string FileFor(string prefix, string key)
        {
            using (var sha = SHA1.Create())
            {
                byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(key ?? ""));
                var sb = new StringBuilder(prefix + "-");
                for (int i = 0; i < 10; i++) sb.Append(h[i].ToString("x2"));
                return Path.Combine(_dir, sb + ".json");
            }
        }
    }
}
