using System;
using System.IO;

namespace GamesHub.Tests
{
    public static class PcgwHardeningTests
    {
        private sealed class Folders : IPcgwFolders
        {
            public string Get(string key) => key == "appdata" ? @"C:\Users\Bob\AppData\Roaming" : null;
        }

        public static void TestOnlyTemplateRootedLocations()
        {
            Assert.True(PcgwWikitext.LooksLikePath(@"{{p|appdata}}\Foo"));
            Assert.True(PcgwWikitext.LooksLikePath(@"{{P|AppData}}/Foo"));
            Assert.True(PcgwWikitext.LooksLikePath(@"{{path|appdata}}\Foo"));
            Assert.False(PcgwWikitext.LooksLikePath(@"C:\Windows\System32"), "literal absolute path");
            Assert.False(PcgwWikitext.LooksLikePath(@"\\server\share\x"), "UNC");
            Assert.False(PcgwWikitext.LooksLikePath(@"{{note|x}} {{p|appdata}}\Foo"), "template must come first");
            Assert.False(PcgwWikitext.LooksLikePath(@"Foo\{{p|appdata}}"), "template not at the start");

            Assert.True(PcgwPaths.Expand(@"C:\Windows\System32", new Folders()) == null, "Expand refuses literal paths too");
            Assert.Equal(@"C:\Users\Bob\AppData\Roaming\Foo", PcgwPaths.Expand(@"{{p|appdata}}\Foo", new Folders()));

            var rows = PcgwWikitext.ExtractRows("{{Game data/saves|Windows|C:\\Windows\\System32|{{p|appdata}}\\Foo}}");
            Assert.Equal(1, rows.Count);
            Assert.Equal(@"{{p|appdata}}\Foo", rows[0].Raw);
        }

        public static void TestWildcardSegmentsAreLimited()
        {
            string root = Path.Combine(Path.GetTempPath(), "gameshub-pcgw-" + Guid.NewGuid().ToString("N"));
            try
            {
                string deep = Path.Combine(root, "a", "b", "c", "d");
                Directory.CreateDirectory(deep);
                var three = PcgwPaths.Resolve(Path.Combine(root, @"*\*\*\d"));
                Assert.True(three.exists, "three wildcard segments resolve");
                Assert.Equal(deep, three.path);
                var four = PcgwPaths.Resolve(Path.Combine(root, @"*\*\*\*"));
                Assert.Equal(("", false), four, "four wildcard segments are refused");
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        public static void TestManyCandidatesStayBounded()
        {
            string root = Path.Combine(Path.GetTempPath(), "gameshub-pcgw-" + Guid.NewGuid().ToString("N"));
            try
            {
                // 20 × 20 = 400 candidate folders at the second level; only the last one holds the target.
                for (int i = 0; i < 20; i++)
                    for (int j = 0; j < 20; j++)
                        Directory.CreateDirectory(Path.Combine(root, "p" + i.ToString("00"), "q" + j.ToString("00")));
                Directory.CreateDirectory(Path.Combine(root, "p00", "q00", "save"));
                var r = PcgwPaths.Resolve(Path.Combine(root, @"*\*\save"));
                Assert.True(r.exists, "an early candidate still resolves");
                Assert.Equal(256, PcgwPaths.MaxCandidates);
                Assert.Equal(3, PcgwPaths.MaxWildcardSegments);
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }
    }
}
