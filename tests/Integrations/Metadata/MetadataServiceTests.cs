// OWNER: META agent. Cache freshness, negative results, dedupe, prefetch, back-off and rate limiting (no network).
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GamesHub.Tests
{
    public static class MetadataServiceTests
    {
        private sealed class Clock
        {
            public DateTime Now = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
            public DateTime Get() => Now;
        }

        private sealed class FakeStore
        {
            public int Calls;
            public readonly List<string> Ids = new List<string>();
            public Func<string, MetadataHttpResult> Respond = id => Ok(id);
            public Task<MetadataHttpResult> Fetch(string id)
            {
                lock (Ids) { Calls++; Ids.Add(id); }
                return Task.FromResult(Respond(id));
            }
        }

        private static MetadataHttpResult Ok(string id)
        {
            string body;
            if (id == "730") body = MetadataFixtures.CounterStrike2;
            else if (id == "1966720") body = MetadataFixtures.LethalCompany;
            else if (id == "389730") body = MetadataFixtures.Tekken7;
            else body = "{\"" + id + "\":{\"success\":false}}";
            return new MetadataHttpResult { Status = 200, Body = body };
        }

        private static string TempDir()
        {
            string d = Path.Combine(Path.GetTempPath(), "gameshub-meta-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(d);
            return d;
        }

        private static MetadataService New(FakeStore store, Clock clock, string dir = null, AppSettings settings = null, double intervalMs = 0)
            => new MetadataService(settings ?? new AppSettings(), new MetadataServiceOptions
            {
                CacheDir = dir ?? TempDir(),
                Fetch = store.Fetch,
                UtcNow = clock.Get,
                MinInterval = TimeSpan.FromMilliseconds(intervalMs),
            });

        // ------------------------------------------------------------------ cache

        public static void TestFreshnessRules()
        {
            var now = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
            MetadataCacheEntry Pos(double days) => new MetadataCacheEntry { AppId = "1", Info = new GameInfo(), FetchedUtcTicks = now.AddDays(-days).Ticks };
            MetadataCacheEntry Neg(double days) => new MetadataCacheEntry { AppId = "1", Negative = true, FetchedUtcTicks = now.AddDays(-days).Ticks };
            Assert.False(MetadataCache.IsFresh(null, now));
            Assert.True(MetadataCache.IsFresh(Pos(0), now));
            Assert.True(MetadataCache.IsFresh(Pos(29.9), now));
            Assert.False(MetadataCache.IsFresh(Pos(30), now));
            Assert.True(MetadataCache.IsFresh(Neg(6.9), now));
            Assert.False(MetadataCache.IsFresh(Neg(7), now));
            Assert.False(MetadataCache.IsFresh(Pos(-3), now), "far-future timestamp (clock change) is stale");
        }

        public static void TestFetchCachesToDiskAndServesOffline()
        {
            var clock = new Clock(); var store = new FakeStore(); string dir = TempDir();
            var svc = New(store, clock, dir);
            Assert.Equal(null, svc.GetCached("730"), "nothing cached yet");
            GameInfo i = svc.FetchAsync("730").Result;
            Assert.Equal("Ação|Gratuitos para Jogar", string.Join("|", i.Genres));
            Assert.True(File.Exists(Path.Combine(dir, "730.json")));
            Assert.Equal(1, store.Calls);

            // Fresh cache: no network.
            Assert.NotNull(svc.FetchAsync("730").Result);
            Assert.Equal(1, store.Calls);

            // A new instance reads from disk (GetCached never hits the network).
            var store2 = new FakeStore();
            var svc2 = New(store2, clock, dir);
            GameInfo c = svc2.GetCached("730");
            Assert.NotNull(c);
            Assert.Equal("21/ago./2012", c.ReleaseDate);
            Assert.Equal("Valve", string.Join("|", c.Developers));
            Assert.Equal(clock.Now.ToLocalTime(), c.FetchedAt);
            Assert.Equal(0, store2.Calls);
        }

        public static void TestStaleCacheRefetchesAndKeepsOldDataOnFailure()
        {
            var clock = new Clock(); var store = new FakeStore(); string dir = TempDir();
            var svc = New(store, clock, dir);
            svc.FetchAsync("389730").Wait();
            clock.Now = clock.Now.AddDays(31);
            store.Respond = id => new MetadataHttpResult { Status = 500 };
            GameInfo i = svc.FetchAsync("389730").Result;
            Assert.Equal(2, store.Calls, "stale → refetch");
            Assert.NotNull(i, "stale data served when refetch fails");
            Assert.Equal(82, i.Metacritic);
            // Failure is remembered for a while: no retry storm.
            svc.FetchAsync("389730").Wait();
            Assert.Equal(2, store.Calls);
        }

        public static void TestNegativeResultIsCached()
        {
            var clock = new Clock(); var store = new FakeStore(); string dir = TempDir();
            var svc = New(store, clock, dir);
            Assert.Equal(null, svc.FetchAsync("999999").Result);
            Assert.Equal(null, svc.GetCached("999999"));
            Assert.True(File.Exists(Path.Combine(dir, "999999.json")));
            Assert.Equal(null, svc.FetchAsync("999999").Result);
            Assert.Equal(1, store.Calls, "negative result served from cache");

            var svc2 = New(store, clock, dir);   // persisted across instances
            Assert.Equal(null, svc2.FetchAsync("999999").Result);
            Assert.Equal(1, store.Calls);

            clock.Now = clock.Now.AddDays(8);    // negative TTL is 7 days
            svc2.FetchAsync("999999").Wait();
            Assert.Equal(2, store.Calls);
        }

        public static void TestFetchMetadataDisabledNeverTouchesNetwork()
        {
            var clock = new Clock(); var store = new FakeStore(); string dir = TempDir();
            New(store, clock, dir).FetchAsync("730").Wait();
            var off = new AppSettings { FetchMetadata = false };
            var svc = New(store, clock, dir, off);
            clock.Now = clock.Now.AddDays(60);
            Assert.NotNull(svc.FetchAsync("730").Result, "stale cache still served");
            Assert.Equal(null, svc.FetchAsync("1966720").Result);
            svc.Prefetch(new[] { "1966720", "389730" });
            Thread.Sleep(100);
            Assert.Equal(1, store.Calls);
            Assert.Equal(0, svc.PendingCount);
        }

        public static void TestInvalidIdsAreIgnored()
        {
            var store = new FakeStore();
            var svc = New(store, new Clock());
            Assert.Equal(null, svc.FetchAsync("").Result);
            Assert.Equal(null, svc.FetchAsync(null).Result);
            Assert.Equal(null, svc.FetchAsync("..\\x").Result);
            Assert.Equal(null, svc.GetCached("abc"));
            svc.Prefetch(new[] { "", null, "x" });
            svc.Prefetch(null);
            Assert.Equal(0, store.Calls);
        }

        // ------------------------------------------------------------------ concurrency / prefetch

        public static void TestConcurrentFetchesAreDeduplicated()
        {
            var clock = new Clock();
            var gate = new TaskCompletionSource<MetadataHttpResult>();
            int calls = 0;
            var svc = new MetadataService(new AppSettings(), new MetadataServiceOptions
            {
                CacheDir = TempDir(), UtcNow = clock.Get, MinInterval = TimeSpan.Zero,
                Fetch = id => { Interlocked.Increment(ref calls); return gate.Task; },
            });
            Task<GameInfo> a = svc.FetchAsync("730"), b = svc.FetchAsync("730");
            Thread.Sleep(100);
            gate.SetResult(Ok("730"));
            Assert.NotNull(a.Result);
            Assert.NotNull(b.Result);
            Assert.Equal(1, calls);
        }

        public static void TestPrefetchRaisesUpdatedAndSkipsFresh()
        {
            var clock = new Clock(); var store = new FakeStore(); string dir = TempDir();
            var svc = New(store, clock, dir);
            svc.FetchAsync("730").Wait();           // fresh → skipped by prefetch

            var updated = new List<string>();
            var done = new CountdownEvent(2);
            svc.MetadataUpdated += id => { lock (updated) updated.Add(id); done.Signal(); };
            svc.Prefetch(new[] { "730", "1966720", "1966720", "389730", "999999" });
            Assert.True(done.Wait(5000), "two MetadataUpdated events");
            Thread.Sleep(100);
            Assert.Equal(4, store.Calls, "730 skipped, duplicate 1966720 deduped");
            updated.Sort();
            Assert.Equal("1966720|389730", string.Join("|", updated), "no event for negative result");
            Assert.Equal(0, svc.PendingCount);
        }

        public static void TestPrefetchIsRateLimited()
        {
            var store = new FakeStore();
            var times = new List<DateTime>();
            store.Respond = id => { lock (times) times.Add(DateTime.UtcNow); return Ok(id); };
            // Real clock for this one: 3 requests with a 150 ms interval.
            var svc = new MetadataService(new AppSettings(), new MetadataServiceOptions
            {
                CacheDir = TempDir(), Fetch = store.Fetch, MinInterval = TimeSpan.FromMilliseconds(150),
            });
            var done = new CountdownEvent(3);
            svc.MetadataUpdated += id => done.Signal();
            svc.Prefetch(new[] { "730", "1966720", "389730" });
            Assert.True(done.Wait(5000));
            Assert.Equal(3, times.Count);
            for (int k = 1; k < times.Count; k++)
                Assert.True((times[k] - times[k - 1]).TotalMilliseconds >= 130, "gap " + (times[k] - times[k - 1]).TotalMilliseconds);
        }

        public static void TestThrottleTriggersBackoff()
        {
            var clock = new Clock(); var store = new FakeStore();
            store.Respond = id => new MetadataHttpResult { Status = 429 };
            var svc = New(store, clock);
            Assert.Equal(null, svc.FetchAsync("730").Result);
            Assert.Equal(1, store.Calls);
            // In back-off: interactive fetches return immediately (cached/null) without a request.
            Assert.Equal(null, svc.FetchAsync("1966720").Result);
            Assert.Equal(1, store.Calls);
            // After the back-off window the store is tried again.
            clock.Now = clock.Now.AddMinutes(6);
            store.Respond = Ok;
            Assert.NotNull(svc.FetchAsync("1966720").Result);
            Assert.Equal(2, store.Calls);
        }

        // ------------------------------------------------------------------ rate limiter (injected clock)

        public static void TestRateLimiterTiming()
        {
            var clock = new Clock();
            var rl = new MetadataRateLimiter(TimeSpan.FromSeconds(1.5), clock.Get);
            Assert.True(rl.TryAcquire(out TimeSpan w), "first request goes immediately");
            Assert.Equal(TimeSpan.Zero, w);
            Assert.False(rl.TryAcquire(out w));
            Assert.Equal(TimeSpan.FromSeconds(1.5), w);
            clock.Now = clock.Now.AddSeconds(1);
            Assert.False(rl.TryAcquire(out w));
            Assert.Equal(TimeSpan.FromSeconds(0.5), w);
            clock.Now = clock.Now.AddSeconds(0.5);
            Assert.True(rl.TryAcquire(out w));
            Assert.Equal(TimeSpan.FromSeconds(1.5), rl.Delay());
        }

        public static void TestRateLimiterBackoff()
        {
            var clock = new Clock();
            var rl = new MetadataRateLimiter(TimeSpan.FromSeconds(1.5), clock.Get);
            rl.TryAcquire(out _);
            rl.Backoff(TimeSpan.FromMinutes(5));
            Assert.True(rl.InBackoff);
            Assert.Equal(TimeSpan.FromMinutes(5), rl.Delay());
            rl.Backoff(TimeSpan.FromMinutes(1));     // shorter back-off never shortens the window
            Assert.Equal(TimeSpan.FromMinutes(5), rl.Delay());
            clock.Now = clock.Now.AddMinutes(5);
            Assert.False(rl.InBackoff);
            Assert.True(rl.TryAcquire(out _));
        }
    }
}
