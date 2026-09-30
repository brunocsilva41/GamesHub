using System;
using System.IO;

namespace GamesHub.Tests
{
    /// <summary>Paths built from third-party data (wiki text, Steam manifests, cache keys) must not escape.</summary>
    public static class IntegrationPathHardeningTests
    {
        private sealed class Folders : IPcgwFolders
        {
            public string Get(string key) => key == "appdata" ? @"C:\Users\Bob\AppData\Roaming" : null;
        }

        public static void TestPcgwExpandRejectsTraversal()
        {
            var f = new Folders();
            Assert.Equal(@"C:\Users\Bob\AppData\Roaming\Game\Saves", PcgwPaths.Expand(@"{{p|appdata}}\Game\Saves", f));
            Assert.Equal(@"C:\Users\Bob\AppData\Roaming\Game..v2", PcgwPaths.Expand(@"{{p|appdata}}\Game..v2", f), "dots inside a name");
            Assert.True(PcgwPaths.Expand(@"{{p|appdata}}\..\..\..\Windows\System32", f) == null, "parent traversal");
            Assert.True(PcgwPaths.Expand(@"{{p|appdata}}/../../x", f) == null, "forward-slash traversal");
            Assert.True(PcgwPaths.Expand(@"{{p|appdata}}\Game\C:\Windows", f) == null, "second drive");
            Assert.True(PcgwPaths.Expand(@"{{p|appdata}}\Game\save.dat:stream", f) == null, "alternate data stream");
        }

        public static void TestPcgwResolveRejectsTraversal()
        {
            var (path, exists) = PcgwPaths.Resolve(@"C:\Users\..\Windows\*");
            Assert.Equal("", path);
            Assert.False(exists);
            Assert.True(PcgwPaths.HasTraversal(@"\\server\share\..\x"), "UNC traversal");
            Assert.False(PcgwPaths.HasTraversal(@"\\server\share\x"), "plain UNC path");
            Assert.False(PcgwPaths.HasTraversal(@"C:\Games\*\Saves"), "wildcards are fine");
        }

        public static void TestSteamManifestInstallDirCannotEscape()
        {
            SteamManifestInfo ok = SteamManifests.ParseManifest(SteamKvParser.Parse("\"AppState\" { \"appid\" \"1\" \"installdir\" \"Game\" }"), @"F:\Lib");
            Assert.Equal(@"F:\Lib\steamapps\common\Game", ok.InstallDir);
            SteamManifestInfo bad = SteamManifests.ParseManifest(SteamKvParser.Parse("\"AppState\" { \"appid\" \"1\" \"installdir\" \"../../../Windows\" }"), @"F:\Lib");
            Assert.NotNull(bad, "the app is still reported");
            Assert.Equal("", bad.InstallDir);
        }

        public static void TestMetadataCacheKeepsFilesInsideItsFolder()
        {
            string root = Path.Combine(Path.GetTempPath(), "gh-meta-harden-" + Guid.NewGuid().ToString("N"));
            string dir = Path.Combine(root, "meta");
            Directory.CreateDirectory(dir);
            try
            {
                var cache = new MetadataCache(dir, null);
                cache.PutNegative("730");
                Assert.True(File.Exists(Path.Combine(dir, "730.json")), "normal ids are persisted");

                cache.PutNegative(@"..\escaped");
                Assert.False(File.Exists(Path.Combine(root, "escaped.json")), "nothing written outside the cache folder");
                MetadataCacheEntry mem = cache.Get(@"..\escaped");
                Assert.True(mem != null && mem.Negative, "still served from memory");
                Assert.True(new MetadataCache(dir, null).Get(@"..\escaped") == null, "never read from outside either");
            }
            finally { Directory.Delete(root, true); }
        }
    }
}
