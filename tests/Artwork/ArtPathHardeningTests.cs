using System;
using System.IO;

namespace GamesHub.Tests
{
    /// <summary>Artwork cache paths are built from app ids / kinds that can come from outside.</summary>
    public static class ArtPathHardeningTests
    {
        private static bool Throws(Action a)
        {
            try { a(); return false; }
            catch (ArgumentException) { return true; }
        }

        public static void TestSteamDirAcceptsOnlyAppIds()
        {
            var cache = new ArtCache(@"C:\Art");
            Assert.Equal(@"C:\Art\steam-730", cache.SteamDir("730"));
            Assert.Equal(@"C:\Art\steam-730\header", cache.SteamBase("730", ArtKind.Header));
            Assert.True(Throws(() => cache.SteamDir(@"..\..\Windows")), "traversal");
            Assert.True(Throws(() => cache.SteamDir(@"1\..\..\x")), "digits then traversal");
            Assert.True(Throws(() => cache.SteamDir("")), "empty");
            Assert.True(cache.FindSteam(@"..\x", ArtKind.Header) == null, "lookups with a bad id just find nothing");
        }

        public static void TestBaseNameAcceptsOnlyKnownKinds()
        {
            Assert.Equal("custom-logo", ArtCache.BaseName(ArtKind.Logo, "custom"));
            Assert.True(Throws(() => ArtCache.BaseName(@"..\..\evil", "custom")), "unknown kind");
            Assert.True(Throws(() => new ArtCache(@"C:\Art").CustomBase("g", @"x\..\..\y")), "custom path with a bad kind");
        }

        public static void TestIndexDropsNonNumericCachedMatches()
        {
            string dir = ArtTestUtil.TempDir();
            string file = Path.Combine(dir, "index.json");
            File.WriteAllText(file, "{\"schema\":1,\"negative\":{},\"search\":{"
                + "\"good\":{\"appId\":\"730\",\"at\":1},"
                + "\"none\":{\"appId\":\"\",\"at\":" + DateTimeOffset.UtcNow.ToUnixTimeSeconds() + "},"
                + "\"bad\":{\"appId\":\"..\\\\..\\\\x\",\"at\":1}}}");
            using (var index = new ArtIndex(file))
            {
                Assert.True(index.TryGetMatch("good", out string good) && good == "730", "numeric id kept");
                Assert.True(index.TryGetMatch("none", out string none) && none == "", "no-match entry kept");
                Assert.False(index.TryGetMatch("bad", out _), "path-like id dropped");
            }
        }
    }
}
