using System;
using System.Collections.Generic;
using System.IO;

namespace GamesHub.Tests
{
    public static class PcgwPathsTests
    {
        private sealed class FakeFolders : IPcgwFolders
        {
            public readonly Dictionary<string, string> Map = new Dictionary<string, string>
            {
                ["appdata"] = @"C:\Users\Bob\AppData\Roaming",
                ["localappdata"] = @"C:\Users\Bob\AppData\Local",
                ["userprofile"] = @"C:\Users\Bob",
                ["userprofile\\documents"] = @"C:\Users\Bob\OneDrive\Documentos",
                ["userprofile\\appdata\\locallow"] = @"C:\Users\Bob\AppData\LocalLow",
                ["public"] = @"C:\Users\Public",
                ["programdata"] = @"C:\ProgramData",
                ["windir"] = @"C:\Windows",
                ["steam"] = @"C:\Program Files (x86)\Steam",
                ["username"] = "Bob",
            };
            public string Get(string key) => Map.TryGetValue(key, out string v) ? v : null;
        }

        public static void TestExpandKnownFolders()
        {
            var f = new FakeFolders();
            Assert.Equal(@"C:\Users\Bob\AppData\Local\TekkenGame\Saved\SaveGames",
                PcgwPaths.Expand(@"{{p|localappdata}}\TekkenGame\Saved\SaveGames\", f));
            Assert.Equal(@"C:\Users\Bob\AppData\Roaming\Foo", PcgwPaths.Expand(@"{{P|AppData}}/Foo/", f));
            Assert.Equal(@"C:\ProgramData\X\Bob", PcgwPaths.Expand(@"{{p|programdata}}\X\{{p|username}}", f));
            Assert.Equal(@"C:\Users\Public\Documents\G", PcgwPaths.Expand(@"{{p|public}}\\Documents\G", f));
        }

        public static void TestDocumentsUsesKnownFolderEvenWhenWrittenUnderUserprofile()
        {
            var f = new FakeFolders();
            Assert.Equal(@"C:\Users\Bob\OneDrive\Documentos\My Games\DIRT5",
                PcgwPaths.Expand(@"{{p|userprofile\Documents}}\My Games\DIRT5\", f));
            Assert.Equal(@"C:\Users\Bob\OneDrive\Documentos\My Games\DIRT5",
                PcgwPaths.Expand(@"{{p|userprofile}}\Documents\My Games\DIRT5\", f));
            Assert.Equal(@"C:\Users\Bob\AppData\LocalLow\ZeekerssRBLX\Lethal Company",
                PcgwPaths.Expand(@"{{p|userprofile}}\AppData\LocalLow\ZeekerssRBLX\Lethal Company\", f));
            // "Documents2" must not be treated as Documents
            Assert.Equal(@"C:\Users\Bob\Documents2", PcgwPaths.Expand(@"{{p|userprofile}}\Documents2", f));
        }

        public static void TestUnresolvableAndRegistry()
        {
            var f = new FakeFolders();
            Assert.True(PcgwPaths.Expand(@"{{p|game}}\save", f) == null, "game unknown → null");
            Assert.True(PcgwPaths.Expand(@"{{p|hkcu}}\Software\Foo", f) == null, "registry → null");
            Assert.True(PcgwPaths.IsRegistry(@"{{p|hklm}}\SOFTWARE\Foo"));
            Assert.True(PcgwPaths.Expand(@"{{p|osxhome}}/Library/Foo", f) == null);
            Assert.True(PcgwPaths.Expand(@"{{p|appdata}}\Foo {{note|x}}", f) == null, "leftover template → null");
            f.Map["game"] = @"D:\Games\Tekken 7\";
            Assert.Equal(@"D:\Games\Tekken 7\save", PcgwPaths.Expand(@"{{p|game}}\save", f));
        }

        public static void TestUidBecomesWildcard()
        {
            Assert.Equal(@"C:\Program Files (x86)\Steam\userdata\*\730\local\cfg",
                PcgwPaths.Expand(@"{{p|steam}}\userdata\{{p|uid}}\730\local\cfg\", new FakeFolders()));
        }

        public static void TestResolveUidSingleAndMultipleMatches()
        {
            string root = TempDir();
            try
            {
                string ud = Path.Combine(root, "userdata");
                Directory.CreateDirectory(Path.Combine(ud, "111", "730", "remote"));
                Directory.CreateDirectory(Path.Combine(ud, "222", "999"));
                var r = PcgwPaths.Resolve(Path.Combine(ud, @"*\730\remote"));
                Assert.Equal(Path.Combine(ud, @"111\730\remote"), r.path);
                Assert.True(r.exists);

                Directory.CreateDirectory(Path.Combine(ud, "333", "730", "remote"));
                Directory.SetLastWriteTimeUtc(Path.Combine(ud, @"111\730\remote"), DateTime.UtcNow.AddDays(-5));
                Directory.SetLastWriteTimeUtc(Path.Combine(ud, @"333\730\remote"), DateTime.UtcNow);
                r = PcgwPaths.Resolve(Path.Combine(ud, @"*\730\remote"));
                Assert.Equal(Path.Combine(ud, @"333\730\remote"), r.path, "most recent of several");

                r = PcgwPaths.Resolve(Path.Combine(ud, @"*\12345"));
                Assert.Equal("", r.path, "no match");
                Assert.False(r.exists);
            }
            finally { Directory.Delete(root, true); }
        }

        public static void TestResolveFileGlobReturnsFolder()
        {
            string root = TempDir();
            try
            {
                string save = Path.Combine(root, "save");
                Directory.CreateDirectory(save);
                var r = PcgwPaths.Resolve(Path.Combine(save, "*.sav"));
                Assert.Equal(save, r.path);
                Assert.False(r.exists, "folder exists but no matching files");
                File.WriteAllText(Path.Combine(save, "slot1.sav"), "x");
                r = PcgwPaths.Resolve(Path.Combine(save, "*.sav"));
                Assert.True(r.exists);
                Assert.Equal(("", false), PcgwPaths.Resolve(Path.Combine(root, @"missing\*.sav")));
            }
            finally { Directory.Delete(root, true); }
        }

        public static void TestToResolvedSetsKindRawAndExists()
        {
            string root = TempDir();
            try
            {
                var f = new FakeFolders();
                f.Map["appdata"] = root;
                Directory.CreateDirectory(Path.Combine(root, "Foo"));
                ResolvedPath rp = PcgwPaths.ToResolved(new PcgwRow { Kind = "save", Raw = @"{{p|appdata}}\Foo\" }, f);
                Assert.Equal("save", rp.Kind);
                Assert.Equal(@"{{p|appdata}}\Foo\", rp.Raw);
                Assert.Equal(Path.Combine(root, "Foo"), rp.Path);
                Assert.True(rp.Exists);
                rp = PcgwPaths.ToResolved(new PcgwRow { Kind = "config", Raw = @"{{p|hkcu}}\Software\Foo" }, f);
                Assert.Equal("", rp.Path);
                Assert.False(rp.Exists);
            }
            finally { Directory.Delete(root, true); }
        }

        public static void TestRealFoldersResolveStandardKeys()
        {
            var real = new PcgwFolders(new Game { Exe = @"D:\Games\X\bin\x.exe" });
            Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), real.Get("appdata"));
            Assert.Equal(@"D:\Games\X\bin", real.Get("game"));
            Assert.True(!string.IsNullOrEmpty(real.Get("userprofile\\appdata\\locallow")));
            Assert.True(!string.IsNullOrEmpty(real.Get("userprofile\\documents")));
            Assert.True(new PcgwFolders(new Game()).Get("game") == null);
            Assert.Equal(@"E:\G", new PcgwFolders(new Game { InstallDir = @"E:\G\", Exe = @"D:\x.exe" }).Get("game"));
        }

        public static void TestExactCaseRestoresCasing()
        {
            string root = TempDir();
            try
            {
                string d = Path.Combine(root, "SteamDir");
                Directory.CreateDirectory(d);
                Assert.True(PcgwFolders.ExactCase(d.ToLowerInvariant()).StartsWith(d.Substring(0, 3).ToUpperInvariant()));
                Assert.True(PcgwFolders.ExactCase(d.ToLowerInvariant()).EndsWith(@"\SteamDir"));
                Assert.Equal(@"z:\nope\x", PcgwFolders.ExactCase(@"z:\nope\x"));
            }
            finally { Directory.Delete(root, true); }
        }

        private static string TempDir()
        {
            string d = Path.Combine(Path.GetTempPath(), "gameshub-pcgw-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(d);
            return d;
        }
    }
}
