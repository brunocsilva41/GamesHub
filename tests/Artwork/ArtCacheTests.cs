// OWNER: ART agent. Cache layout, index and URL tests (temp folders only, no network).
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text.RegularExpressions;

namespace GamesHub.Tests
{
    internal static class ArtTestUtil
    {
        public static string TempDir()
        {
            string d = Path.Combine(Path.GetTempPath(), "gameshub-art-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(d);
            return d;
        }

        public static byte[] ImageBytes(int w, int h, ImageFormat fmt, Color? fill = null)
        {
            using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            using (var ms = new MemoryStream())
            {
                using (Graphics g = Graphics.FromImage(bmp)) g.Clear(fill ?? Color.CornflowerBlue);
                bmp.Save(ms, fmt);
                return ms.ToArray();
            }
        }

        public static void WriteImage(string path, ImageFormat fmt, int w = 32, int h = 32)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, ImageBytes(w, h, fmt));
        }

        public static void Cleanup(string dir)
        {
            try { Directory.Delete(dir, true); }
            catch (IOException ex) { Console.WriteLine("  (cleanup skipped: " + ex.Message + ")"); }
        }
    }

    public static class ArtCacheTests
    {
        public static void TestGameKeyIsStable()
        {
            string k = ArtCache.GameKey("folder:tekken 7.exe");
            Assert.True(Regex.IsMatch(k, "^g-[0-9a-f]{16}$"), k);
            Assert.Equal(k, ArtCache.GameKey("FOLDER:Tekken 7.exe"));
            Assert.Equal("g-cbf29ce484222325", ArtCache.GameKey(""), "FNV-1a offset basis for empty input");
            Assert.True(k != ArtCache.GameKey("folder:tekken 8.exe"));
            Assert.Equal("steam-730", ArtCache.SteamKey("730"));
        }

        public static void TestResolvePrecedence()
        {
            string root = ArtTestUtil.TempDir();
            try
            {
                var cache = new ArtCache(root);
                string id = "folder:game.lnk";
                string gk = ArtCache.GameKey(id);
                ArtTestUtil.WriteImage(Path.Combine(root, "steam-10", "header.jpg"), ImageFormat.Jpeg);
                ArtTestUtil.WriteImage(Path.Combine(root, "steam-10", "capsule.jpg"), ImageFormat.Jpeg);
                ArtTestUtil.WriteImage(Path.Combine(root, gk, "custom-header.png"), ImageFormat.Png);
                ArtTestUtil.WriteImage(Path.Combine(root, gk, "sgdb-capsule.jpg"), ImageFormat.Jpeg);
                ArtTestUtil.WriteImage(Path.Combine(root, gk, "sgdb-hero.png"), ImageFormat.Png);
                ArtTestUtil.WriteImage(Path.Combine(root, gk, "icon.png"), ImageFormat.Png);

                Artwork a = cache.Resolve(id, "10");
                Assert.Equal(gk + "/custom-header.png", a.Header, "custom beats steam");
                Assert.Equal("steam-10/capsule.jpg", a.Capsule, "steam beats sgdb");
                Assert.Equal(gk + "/sgdb-hero.png", a.Hero, "sgdb when steam missing");
                Assert.Equal(null, a.Logo);
                Assert.Equal(gk + "/icon.png", a.Icon);

                ArtTestUtil.WriteImage(Path.Combine(root, gk, "custom-icon.png"), ImageFormat.Png);
                Assert.Equal(gk + "/custom-icon.png", cache.Resolve(id, "10").Icon, "custom icon beats extracted");

                Artwork noApp = cache.Resolve(id, "");
                Assert.Equal(gk + "/sgdb-capsule.jpg", noApp.Capsule, "without app id only per-game art");
                Assert.Equal(null, cache.Resolve("folder:other.lnk", "").Header);
            }
            finally { ArtTestUtil.Cleanup(root); }
        }

        public static void TestSteamUrls()
        {
            var capsule = SteamEndpoints.ArtUrls("123", ArtKind.Capsule);
            Assert.Equal(4, capsule.Count);
            Assert.Equal("https://shared.cloudflare.steamstatic.com/store_item_assets/steam/apps/123/library_600x900_2x.jpg", capsule[0]);
            Assert.Equal("https://cdn.cloudflare.steamstatic.com/steam/apps/123/library_600x900_2x.jpg", capsule[1]);
            Assert.True(capsule[2].EndsWith("/123/library_600x900.jpg"));
            Assert.True(SteamEndpoints.ArtUrls("5", ArtKind.Logo)[0].EndsWith("/5/logo.png"));
            Assert.True(SteamEndpoints.ArtUrls("5", ArtKind.Hero)[1].EndsWith("/steam/apps/5/library_hero.jpg"));
            Assert.Equal(0, SteamEndpoints.ArtUrls("5", ArtKind.Icon).Count);
            Assert.True(SteamEndpoints.StoreSearch("liars bar & co?").Contains("term=liars%20bar%20%26%20co%3F&"), SteamEndpoints.StoreSearch("liars bar & co?"));
            Assert.True(SteamEndpoints.AppDetails("730").StartsWith("https://store.steampowered.com/api/appdetails?appids=730"));
            Assert.True(SteamEndpoints.SgdbAssets("42", ArtKind.Capsule).Contains("grids/game/42?dimensions=600x900"));
        }

        public static void TestAppIdValidation()
        {
            Assert.True(ArtKind.IsAppId("730"));
            Assert.False(ArtKind.IsAppId(""));
            Assert.False(ArtKind.IsAppId("0"));
            Assert.False(ArtKind.IsAppId("73a"));
            Assert.False(ArtKind.IsAppId(null));
        }
    }

    public static class ArtIndexTests
    {
        public static void TestNegativeCacheExpires()
        {
            string dir = ArtTestUtil.TempDir();
            DateTime now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            using (var idx = new ArtIndex(Path.Combine(dir, "index.json"), () => now))
            {
                Assert.False(idx.IsNegative("steam:1:hero"));
                idx.MarkNegative("steam:1:hero");
                Assert.True(idx.IsNegative("steam:1:hero"));
                now = now.AddDays(6.9);
                Assert.True(idx.IsNegative("steam:1:hero"), "still negative before 7 days");
                now = now.AddDays(0.2);
                Assert.False(idx.IsNegative("steam:1:hero"), "retried after 7 days");
            }
            ArtTestUtil.Cleanup(dir);
        }

        public static void TestSearchCacheExpiry()
        {
            string dir = ArtTestUtil.TempDir();
            DateTime now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            using (var idx = new ArtIndex(Path.Combine(dir, "index.json"), () => now))
            {
                idx.SetMatch("league of legends", "");
                idx.SetMatch("tekken 7", "389730");
                Assert.True(idx.TryGetMatch("league of legends", out string none));
                Assert.Equal("", none);
                now = now.AddDays(15);
                Assert.False(idx.TryGetMatch("league of legends", out _), "no-match retried after 14 days");
                Assert.True(idx.TryGetMatch("tekken 7", out string id), "positive matches do not expire");
                Assert.Equal("389730", id);
                Assert.False(idx.AddMatchIfMissing("tekken 7", "1"));
                idx.RemoveMatch("tekken 7");
                Assert.False(idx.TryGetMatch("tekken 7", out _));
            }
            ArtTestUtil.Cleanup(dir);
        }

        public static void TestPersistenceAndPrefixClear()
        {
            string dir = ArtTestUtil.TempDir();
            string file = Path.Combine(dir, "index.json");
            using (var idx = new ArtIndex(file))
            {
                idx.MarkNegative("steam:9:logo");
                idx.MarkNegative("steam:9:hero");
                idx.MarkNegative("steam:99:hero");
                idx.SetMatch("mimesis", "2827200");
                idx.ClearNegative("steam:9:");
            }
            using (var again = new ArtIndex(file))
            {
                Assert.False(again.IsNegative("steam:9:logo"));
                Assert.True(again.IsNegative("steam:99:hero"));
                Assert.True(again.TryGetMatch("mimesis", out string id));
                Assert.Equal("2827200", id);
            }
            File.WriteAllText(file, "{\"schema\":999,\"search\":{\"x\":{\"appId\":\"1\",\"at\":1}}}");
            using (var fresh = new ArtIndex(file))
                Assert.False(fresh.TryGetMatch("x", out _), "unknown schema starts fresh");
            ArtTestUtil.Cleanup(dir);
        }
    }
}
