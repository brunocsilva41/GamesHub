using System;
using System.Collections.Generic;
using System.IO;

namespace GamesHub
{
    /// <summary>On-disk record. Negative = the store said success:false (app unknown / region-locked).</summary>
    public sealed class MetadataCacheEntry
    {
        public string AppId = "";
        public bool Negative;
        public long FetchedUtcTicks;
        public GameInfo Info;   // null when Negative

        public DateTime FetchedUtc => new DateTime(FetchedUtcTicks, DateTimeKind.Utc);
    }

    public sealed class MetadataCache
    {
        public static readonly TimeSpan PositiveTtl = TimeSpan.FromDays(30);
        public static readonly TimeSpan NegativeTtl = TimeSpan.FromDays(7);

        private readonly string _dir;
        private readonly Func<DateTime> _utcNow;
        private readonly object _gate = new object();
        private readonly Dictionary<string, MetadataCacheEntry> _mem = new Dictionary<string, MetadataCacheEntry>();
        private readonly HashSet<string> _diskMiss = new HashSet<string>();

        public MetadataCache(string dir, Func<DateTime> utcNow)
        {
            _dir = dir;
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        public static bool IsFresh(MetadataCacheEntry e, DateTime utcNow)
        {
            if (e == null) return false;
            TimeSpan age = utcNow - e.FetchedUtc;
            return age >= TimeSpan.Zero - TimeSpan.FromDays(1) && age < (e.Negative ? NegativeTtl : PositiveTtl);
        }

        public bool IsFresh(MetadataCacheEntry e) => IsFresh(e, _utcNow());

        /// <summary>Memory → disk. Returns null when nothing is cached (stale entries are returned).</summary>
        public MetadataCacheEntry Get(string appId)
        {
            lock (_gate)
            {
                if (_mem.TryGetValue(appId, out var hit)) return hit;
                if (_diskMiss.Contains(appId)) return null;
            }
            MetadataCacheEntry e = Load(appId);
            lock (_gate)
            {
                if (_mem.TryGetValue(appId, out var raced)) return raced;
                if (e != null) _mem[appId] = e; else _diskMiss.Add(appId);
            }
            return e;
        }

        public MetadataCacheEntry PutPositive(GameInfo info)
        {
            DateTime now = _utcNow();
            info.FetchedAt = now.ToLocalTime();
            return Put(new MetadataCacheEntry { AppId = info.AppId, FetchedUtcTicks = now.Ticks, Info = info });
        }

        public MetadataCacheEntry PutNegative(string appId)
            => Put(new MetadataCacheEntry { AppId = appId, Negative = true, FetchedUtcTicks = _utcNow().Ticks });

        private MetadataCacheEntry Put(MetadataCacheEntry e)
        {
            lock (_gate) { _mem[e.AppId] = e; _diskMiss.Remove(e.AppId); }
            string file = PathFor(e.AppId);
            if (file == null) return e;   // not a storable id: memory only
            try { Json.Save(file, e); }
            catch (Exception ex) when (ExpectedErrors.IsFileOrJson(ex)) { Log.Warn("Metadata: could not write cache for app " + e.AppId, ex); }
            return e;
        }

        /// <summary>Cache file of an app id, or null when the id would resolve outside the cache folder.</summary>
        private string PathFor(string appId) => SafePath.Combine(_dir, (appId ?? "") + ".json");

        private MetadataCacheEntry Load(string appId)
        {
            string file = PathFor(appId);
            if (file == null || !File.Exists(file)) return null;
            var e = Json.Load<MetadataCacheEntry>(file, null);
            if (e == null || e.AppId != appId || (!e.Negative && e.Info == null)) return null;
            if (e.Info != null)
            {
                e.Info.AppId = appId;
                e.Info.FetchedAt = e.FetchedUtc.ToLocalTime();
                e.Info.Genres = e.Info.Genres ?? new List<string>();
                e.Info.Categories = e.Info.Categories ?? new List<string>();
                e.Info.Developers = e.Info.Developers ?? new List<string>();
                e.Info.Publishers = e.Info.Publishers ?? new List<string>();
            }
            return e;
        }
    }
}
