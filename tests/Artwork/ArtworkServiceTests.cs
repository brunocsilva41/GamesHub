// OWNER: ART agent. Service-level tests with AutoArtwork off (no network), parsing, back-off and images.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;

namespace GamesHub.Tests
{
    public static class ArtworkServiceTests
    {
        private static ArtworkService Offline(string dir) => new ArtworkService(new AppSettings { AutoArtwork = false }, dir);

        public static void TestCustomImageLifecycle()
        {
            string dir = ArtTestUtil.TempDir();
            try
            {
                using (ArtworkService svc = Offline(dir))
                {
                    var raised = new List<string>();
                    svc.ArtworkUpdated += raised.Add;
                    var game = new Game { Id = "folder:x.lnk", Name = "X", SteamAppId = "10" };
                    ArtTestUtil.WriteImage(Path.Combine(dir, "steam-10", "hero.jpg"), ImageFormat.Jpeg);
                    Assert.Equal("steam-10/hero.jpg", svc.Resolve(game).Hero);

                    string src = Path.Combine(dir, "in.png");
                    ArtTestUtil.WriteImage(src, ImageFormat.Png, 64, 40);
                    OpResult r = svc.SetCustomImage(game, ArtKind.Hero, src);
                    Assert.True(r.Ok, r.Message);
                    string gk = ArtCache.GameKey(game.Id);
                    Assert.Equal(gk + "/custom-hero.jpg", svc.Resolve(game).Hero, "hero stored as jpg");
                    Assert.Equal(1, raised.Count);

                    Assert.True(svc.SetCustomImage(game, ArtKind.Logo, src).Ok);
                    Assert.Equal(gk + "/custom-logo.png", svc.Resolve(game).Logo, "logo keeps png");

                    Assert.False(svc.SetCustomImage(game, "banner", src).Ok, "invalid kind");
                    string junk = Path.Combine(dir, "junk.png");
                    File.WriteAllText(junk, "not an image");
                    Assert.False(svc.SetCustomImage(game, ArtKind.Hero, junk).Ok, "invalid image");

                    Assert.True(svc.ClearCustomImage(game, ArtKind.Hero).Ok);
                    Assert.Equal("steam-10/hero.jpg", svc.Resolve(game).Hero, "falls back to steam art");
                }
            }
            finally { ArtTestUtil.Cleanup(dir); }
        }

        public static void TestImportLegacyIsIdempotent()
        {
            string dir = ArtTestUtil.TempDir();
            string legacy = ArtTestUtil.TempDir();
            try
            {
                ArtTestUtil.WriteImage(Path.Combine(legacy, "covers", "123.jpg"), ImageFormat.Jpeg);
                File.WriteAllText(Path.Combine(legacy, "covers", "bad.jpg"), "x");
                File.WriteAllText(Path.Combine(legacy, "searchcache.json"), "{\"my game\": 456, \"other\": 0}");
                using (ArtworkService svc = Offline(dir))
                {
                    svc.ImportLegacy(legacy);
                    svc.ImportLegacy(legacy);
                    Assert.True(File.Exists(Path.Combine(dir, "steam-123", "header.jpg")));
                    var game = new Game { Id = "folder:my game.lnk", Name = "My Game" };
                    Assert.Equal("456", svc.GetMatchedSteamAppId(game));
                    Assert.Equal("", svc.GetMatchedSteamAppId(new Game { Id = "folder:other.lnk", Name = "Other" }));
                    Assert.Equal("123", svc.GetMatchedSteamAppId(new Game { Id = "steam:123", Name = "Z", SteamAppId = "123" }));
                    Assert.Equal("steam-123/header.jpg", svc.Resolve(new Game { Id = "steam:123", SteamAppId = "123" }).Header);
                    svc.ImportLegacy(Path.Combine(legacy, "missing"));
                }
            }
            finally { ArtTestUtil.Cleanup(dir); ArtTestUtil.Cleanup(legacy); }
        }

        public static void TestSteamJsonParsing()
        {
            var store = SteamClient.ParseStoreSearch(
                "{\"total\":2,\"items\":[{\"type\":\"app\",\"name\":\"TEKKEN 7\",\"id\":389730,\"tiny_image\":\"https://x/t.jpg\"}," +
                "{\"type\":\"sub\",\"name\":\"Bundle\",\"id\":1}]}");
            Assert.Equal(1, store.Count);
            Assert.Equal("389730", store[0].AppId);
            Assert.Equal("https://x/t.jpg", store[0].IconUrl);
            var community = SteamClient.ParseCommunitySearch("[{\"appid\":\"730\",\"name\":\"Counter-Strike 2\",\"icon\":\"https://i\"}]");
            Assert.Equal("730", community[0].AppId);
            Assert.Equal(0, SteamClient.ParseCommunitySearch("not json").Count);
            AppDetails d = SteamClient.ParseAppDetails(
                "{\"730\":{\"success\":true,\"data\":{\"name\":\"Counter-Strike 2\",\"header_image\":\"https://h/header.jpg?t=1\"}}}", "730");
            Assert.Equal("Counter-Strike 2", d.Name);
            Assert.Equal("https://h/header.jpg?t=1", d.HeaderImage);
            Assert.Equal(null, SteamClient.ParseAppDetails("{\"1\":{\"success\":false}}", "1"));
        }

        public static void TestNetworkBackoff()
        {
            DateTime now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var b = new NetworkBackoff(() => now);
            b.RecordFailure();
            b.RecordFailure();
            Assert.False(b.IsPaused, "two failures tolerated");
            Assert.True(b.RecordFailure(), "third failure pauses");
            Assert.True(b.IsPaused);
            now = now.AddSeconds(31);
            Assert.False(b.IsPaused, "pause over after 30 s");
            b.RecordFailure();
            Assert.True(b.IsPaused, "a failed probe pauses again");
            Assert.Equal(now.AddSeconds(60), b.PausedUntil, "pause doubles");
            b.RecordSuccess();
            Assert.False(b.IsPaused);
        }

        public static void TestImageValidationAndDownscale()
        {
            Assert.Equal(".png", ImageFiles.SniffExt(ArtTestUtil.ImageBytes(20, 20, ImageFormat.Png)));
            Assert.Equal(".jpg", ImageFiles.SniffExt(ArtTestUtil.ImageBytes(20, 20, ImageFormat.Jpeg)));
            Assert.True(ImageFiles.IsValidImage(ArtTestUtil.ImageBytes(20, 20, ImageFormat.Png), out _));
            Assert.False(ImageFiles.IsValidImage(ArtTestUtil.ImageBytes(8, 8, ImageFormat.Png), out _), "too small");
            byte[] html = System.Text.Encoding.UTF8.GetBytes("<html>404</html>");
            Assert.False(ImageFiles.IsValidImage(html, out _));
            byte[] truncated = ArtTestUtil.ImageBytes(64, 64, ImageFormat.Png);
            Array.Resize(ref truncated, 40);
            Assert.False(ImageFiles.IsValidImage(truncated, out _), "truncated png");

            string dir = ArtTestUtil.TempDir();
            try
            {
                string saved = ImageFiles.SaveNormalized(ArtTestUtil.ImageBytes(4000, 100, ImageFormat.Png), Path.Combine(dir, "custom-hero"), false);
                Assert.True(saved.EndsWith("custom-hero.jpg"));
                using (Image img = Image.FromFile(saved))
                {
                    Assert.Equal(3840, img.Width);
                    Assert.Equal(96, img.Height);
                }
            }
            finally { ArtTestUtil.Cleanup(dir); }
        }

        public static void TestIconCropFindsContent()
        {
            using (var bmp = new Bitmap(256, 256, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.FillRectangle(Brushes.Red, 104, 110, 48, 40);
                }
                Rectangle r = ImageFiles.ContentBounds(bmp);
                Assert.Equal(new Rectangle(104, 110, 48, 40), r);
                using (Bitmap sq = ImageFiles.CropToSquare(bmp, r))
                    Assert.Equal(48, sq.Width);
            }
            using (var blank = new Bitmap(32, 32, PixelFormat.Format32bppArgb))
                Assert.True(ImageFiles.ContentBounds(blank).IsEmpty, "blank icon");
        }

        public static void TestKeyedTasksDeduplicate()
        {
            var keyed = new KeyedTasks();
            int runs = 0;
            var gate = new TaskCompletionSource<bool>();
            Func<Task<int>> work = async () => { System.Threading.Interlocked.Increment(ref runs); await gate.Task; return 7; };
            Task<int> a = keyed.Run("steam:1", work);
            Task<int> b = keyed.Run("steam:1", work);
            Task<int> c = keyed.Run("steam:2", work);
            gate.SetResult(true);
            Task.WaitAll(a, b, c);
            Assert.True(ReferenceEquals(a, b), "same key shares the task");
            Assert.Equal(2, runs);
            Assert.Equal(7, b.Result);
        }
    }
}
