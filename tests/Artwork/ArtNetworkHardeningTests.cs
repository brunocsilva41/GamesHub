using System;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Net;

namespace GamesHub.Tests
{
    /// <summary>SteamGridDB response validation, image decode limits, bounded HTTP bodies and TLS selection.</summary>
    public static class ArtNetworkHardeningTests
    {
        public static void TestSgdbImagesOnlyFromItsCdn()
        {
            Assert.True(SteamGridDbClient.IsSgdbCdnUrl("https://cdn2.steamgriddb.com/grid/abc.png"), "what the API returns");
            Assert.True(SteamGridDbClient.IsSgdbCdnUrl("https://cdn2.steamgriddb.com/thumb/abc.jpg"));
            Assert.True(SteamGridDbClient.IsSgdbCdnUrl("https://CDN.SteamGridDB.com/x.png"), "other SGDB subdomain");
            Assert.False(SteamGridDbClient.IsSgdbCdnUrl("http://cdn2.steamgriddb.com/grid/abc.png"), "plain http");
            Assert.False(SteamGridDbClient.IsSgdbCdnUrl("https://steamgriddb.com.evil.com/x.png"), "suffix trick");
            Assert.False(SteamGridDbClient.IsSgdbCdnUrl("https://evilsteamgriddb.com/x.png"), "no dot before the domain");
            Assert.False(SteamGridDbClient.IsSgdbCdnUrl("https://cdn2.steamgriddb.com@evil.com/x.png"), "userinfo trick");
            Assert.False(SteamGridDbClient.IsSgdbCdnUrl("https://user:pw@cdn2.steamgriddb.com/x.png"), "credentials");
            Assert.False(SteamGridDbClient.IsSgdbCdnUrl("https://cdn2.steamgriddb.com:8443/x.png"), "non-default port");
            Assert.False(SteamGridDbClient.IsSgdbCdnUrl("https://192.168.0.1/x.png"), "IP");
            Assert.False(SteamGridDbClient.IsSgdbCdnUrl("file://cdn2.steamgriddb.com/x.png"));
            Assert.False(SteamGridDbClient.IsSgdbCdnUrl(""));
            Assert.False(SteamGridDbClient.IsSgdbCdnUrl(null));
        }

        public static void TestSgdbIdsMustBeNumeric()
        {
            Assert.True(SteamEndpoints.SgdbAssets("5249", ArtKind.Hero).Contains("heroes/game/5249?"));
            foreach (string bad in new[] { "", null, "../games/1", "1?x=2", "1#", "1/2", "12a", "-1", "１２" })
                Assert.True(SteamEndpoints.SgdbAssets(bad, ArtKind.Capsule) == null, "rejected id: " + bad);
            Assert.True(SteamEndpoints.SgdbBySteamId("730").EndsWith("games/steam/730"));
            Assert.True(SteamEndpoints.SgdbBySteamId("730/../x") == null);
        }

        public static void TestImageDimensionsFromHeader()
        {
            foreach (var fmt in new[] { ImageFormat.Png, ImageFormat.Jpeg, ImageFormat.Gif, ImageFormat.Bmp })
            {
                Assert.True(ImageFiles.TryReadDimensions(ArtTestUtil.ImageBytes(123, 45, fmt), out int w, out int h), "readable: " + fmt);
                Assert.Equal(123, w, fmt + " width");
                Assert.Equal(45, h, fmt + " height");
            }
            Assert.False(ImageFiles.TryReadDimensions(new byte[] { 0xFF, 0xD8, 0xFF, 0xD9, 0, 0, 0, 0 }, out _, out _), "JPEG without SOF");
            Assert.False(ImageFiles.TryReadDimensions(new byte[10], out _, out _), "not an image");
        }

        private static byte[] WithPngSize(byte[] png, uint w, uint h)
        {
            byte[] b = (byte[])png.Clone();
            b[16] = (byte)(w >> 24); b[17] = (byte)(w >> 16); b[18] = (byte)(w >> 8); b[19] = (byte)w;
            b[20] = (byte)(h >> 24); b[21] = (byte)(h >> 16); b[22] = (byte)(h >> 8); b[23] = (byte)h;
            return b;
        }

        public static void TestHugeDeclaredImagesAreNeverDecoded()
        {
            byte[] png = ArtTestUtil.ImageBytes(64, 64, ImageFormat.Png);
            Assert.True(ImageFiles.WithinDecodeLimits(png));
            Assert.False(ImageFiles.WithinDecodeLimits(WithPngSize(png, 8193, 100)), "side over 8192");
            Assert.False(ImageFiles.WithinDecodeLimits(WithPngSize(png, 8000, 8000)), "64 MP over 40 MP");
            Assert.True(ImageFiles.WithinDecodeLimits(WithPngSize(png, 8000, 5000)), "exactly 40 MP");
            Assert.False(ImageFiles.IsValidImage(WithPngSize(png, 65535, 65535), out _), "decompression bomb rejected before GDI+");

            string dir = ArtTestUtil.TempDir();
            try
            {
                bool threw = false;
                try { ImageFiles.SaveNormalized(WithPngSize(png, 60000, 60000), Path.Combine(dir, "custom-hero"), true); }
                catch (ArgumentException) { threw = true; }
                Assert.True(threw, "custom image with a huge header is refused (ArgumentException → 'imagem inválida')");
                Assert.False(File.Exists(Path.Combine(dir, "custom-hero.png")));
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch (Exception ex) { Console.WriteLine("cleanup: " + ex.Message); }
            }
        }

        public static void TestBoundedReadCapsWhileReading()
        {
            var data = new byte[100000];
            Assert.Equal(100000, BoundedRead.ReadAll(new MemoryStream(data), 100000).Length, "exactly at the cap");
            Assert.True(BoundedRead.ReadAll(new MemoryStream(data), 99999) == null, "one byte over");
            Assert.True(BoundedRead.ReadAllAsync(new MemoryStream(data), 1000).Result == null, "async");
            Assert.Equal(0, BoundedRead.ReadAll(null, 10).Length);

            // A gzip bomb (small on the wire, big once inflated) is cut at the decoded size.
            var packed = new MemoryStream();
            using (var gz = new GZipStream(packed, CompressionMode.Compress, true)) gz.Write(new byte[5 * 1024 * 1024], 0, 5 * 1024 * 1024);
            Assert.True(packed.Length < 64 * 1024, "compressed payload is small");
            packed.Position = 0;
            using (var inflate = new GZipStream(packed, CompressionMode.Decompress))
                Assert.True(BoundedRead.ReadAll(inflate, 1024 * 1024) == null, "inflated body over the cap");
        }

        public static void TestTlsPolicyDropsLegacyProtocols()
        {
            Assert.Equal(SecurityProtocolType.SystemDefault, TlsPolicy.Choose(SecurityProtocolType.SystemDefault), "OS default kept");
            var modern = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;
            Assert.Equal(modern, TlsPolicy.Choose(SecurityProtocolType.Ssl3 | SecurityProtocolType.Tls), "legacy runtime default replaced");
            Assert.Equal(modern, TlsPolicy.Choose(SecurityProtocolType.Ssl3 | SecurityProtocolType.Tls | SecurityProtocolType.Tls12), "old |= Tls12 result cleaned");

            SecurityProtocolType saved = ServicePointManager.SecurityProtocol;
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls | SecurityProtocolType.Tls11;
                TlsPolicy.Ensure();
                Assert.Equal(modern, ServicePointManager.SecurityProtocol);
            }
            finally { ServicePointManager.SecurityProtocol = saved; }
        }
    }
}
