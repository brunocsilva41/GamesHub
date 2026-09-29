using System;
using System.IO;

namespace GamesHub.Tests
{
    public static class SteamLocalDataTests
    {
        // ------------------------------------------------------------------ VDF parser

        public static void TestKvEscapesCommentsNestedUnquoted()
        {
            string text = "\uFEFF// header\n\"Root\"\n{\n  \"a\"  \"1\" // trailing\n  \"Path\" \"C:\\\\Games\\\\X \\\"Q\\\"\"\n" +
                          "  \"child\" { \"k\" \"v\" \"deep\" { \"z\" \"9\" } }\n  unquoted value\n  \"cond\" \"yes\" [$WIN32]\n" +
                          "  \"tab\" \"a\\tb\" \"odd\" \"x\\qy\"\n}\n";
            SteamKv root = SteamKvParser.Parse(text).Node("root");
            Assert.NotNull(root, "root (case-insensitive)");
            Assert.Equal("1", root.Str("A"));
            Assert.Equal("C:\\Games\\X \"Q\"", root.Str("path"));
            Assert.Equal("v", root.Node("child").Str("k"));
            Assert.Equal("9", root.Path("child", "deep").Str("z"));
            Assert.Equal("value", root.Str("unquoted"));
            Assert.Equal("yes", root.Str("cond"));
            Assert.Equal("a\tb", root.Str("tab"));
            Assert.Equal("x\\qy", root.Str("odd"), "unknown escape kept");
        }

        public static void TestKvMalformedIsTolerated()
        {
            SteamKv root = SteamKvParser.Parse("\"A\" { \"x\" \"1\" \"y\" { \"z\" \"unterminated");
            Assert.Equal("1", root.Node("A").Str("x"));
            Assert.NotNull(SteamKvParser.Parse((string)null));
            Assert.NotNull(SteamKvParser.Parse("}}}{"));
            Assert.Equal("2", SteamKvParser.Parse("} \"k\" \"2\"").Str("k"), "stray close at top level ignored");
        }

        public static void TestKvParsePathSkipsOtherBlocks()
        {
            string text = "\"S\" { \"apps\" { \"1\" { \"x\" \"wrong\" } } \"Software\" { \"apps\" { \"10\" { \"Playtime\" \"5\" } } } }";
            SteamKv n = SteamKvParser.ParsePath(new StringReader(text), "s", "software", "apps");
            Assert.NotNull(n);
            Assert.Equal("5", n.Node("10").Str("Playtime"));
            Assert.True(n.Node("1") == null);
            Assert.True(SteamKvParser.ParsePath(new StringReader(text), "S", "missing") == null);
            Assert.True(SteamKvParser.ParsePath(new StringReader(text), "S", "apps", "2") == null);
        }

        // ------------------------------------------------------------------ manifests

        public static void TestUpdatePendingDecisions()
        {
            Assert.False(SteamManifests.IsUpdatePending(4, 100, 100, 0, 7, 7), "fully installed, up to date");
            Assert.False(SteamManifests.IsUpdatePending(4, 0, 0, 0), "no counters");
            Assert.False(SteamManifests.IsUpdatePending(4 | 64, 0, 0, 0), "running game is not an update");
            Assert.True(SteamManifests.IsUpdatePending(6, 0, 0, 0), "UpdateRequired bit");
            Assert.True(SteamManifests.IsUpdatePending(4 | 512, 0, 0, 0), "UpdatePaused");
            Assert.True(SteamManifests.IsUpdatePending(4 | 1024, 0, 0, 0), "UpdateStarted");
            Assert.True(SteamManifests.IsUpdatePending(4 | 0x100000, 0, 0, 0), "Downloading");
            Assert.True(SteamManifests.IsUpdatePending(4, 2460309632, 0, 0), "bytes left to download");
            Assert.True(SteamManifests.IsUpdatePending(4, 0, 0, 0, 100, 200), "target build differs");
            Assert.False(SteamManifests.IsUpdatePending(4, 0, 0, 0, 100, 0), "no target build");
            Assert.True(SteamManifests.IsUpdatePending(0, 0, 0, 12), "failed update, not fully installed");
            Assert.False(SteamManifests.IsUpdatePending(4, 0, 0, 12), "old failure but fully installed");
        }

        public static void TestLibraryFoldersBothFormats()
        {
            var modern = SteamKvParser.Parse("\"libraryfolders\" { \"0\" { \"path\" \"C:\\\\Steam\" } \"1\" { \"path\" \"F:\\\\SteamLibrary\" } }");
            var libs = SteamManifests.ParseLibraryFolders(modern, "C:/Steam");
            Assert.Equal(2, libs.Count, "root deduplicated");
            Assert.Equal("F:\\SteamLibrary", libs[1]);
            var old = SteamKvParser.Parse("\"LibraryFolders\" { \"TimeNextStatsReport\" \"123\" \"1\" \"D:\\\\Games\\\\Steam\" }");
            libs = SteamManifests.ParseLibraryFolders(old, "C:\\Steam");
            Assert.Equal(2, libs.Count);
            Assert.Equal("D:\\Games\\Steam", libs[1]);
        }

        public static void TestParseManifest()
        {
            var doc = SteamKvParser.Parse("\"AppState\" { \"appid\" \"359550\" \"name\" \"R6\" \"StateFlags\" \"6\" \"installdir\" \"Rainbow Six\"" +
                                          " \"SizeOnDisk\" \"51163848375\" \"buildid\" \"1\" \"TargetBuildID\" \"2\" }");
            SteamManifestInfo m = SteamManifests.ParseManifest(doc, "F:\\SteamLibrary");
            Assert.Equal("359550", m.AppId);
            Assert.Equal("F:\\SteamLibrary\\steamapps\\common\\Rainbow Six", m.InstallDir);
            Assert.Equal(51163848375L, m.SizeOnDisk);
            Assert.True(m.UpdatePending);
            Assert.True(SteamManifests.ParseManifest(SteamKvParser.Parse("\"AppState\" { \"appid\" \"abc\" }"), "X") == null);
            var noSize = SteamManifests.ParseManifest(SteamKvParser.Parse("\"AppState\" { \"appid\" \"1\" }"), "X");
            Assert.Equal(-1L, noSize.SizeOnDisk);
        }

        // ------------------------------------------------------------------ users

        public static void TestSteamIdConversion()
        {
            Assert.Equal("1095338417", SteamUsers.AccountIdFromSteamId64("76561199055604145"));
            Assert.Equal("1", SteamUsers.AccountIdFromSteamId64("76561197960265729"));
            Assert.Equal("", SteamUsers.AccountIdFromSteamId64("123"));
            Assert.Equal("", SteamUsers.AccountIdFromSteamId64("abc"));
            Assert.Equal("", SteamUsers.AccountIdFromSteamId64(null));
        }

        public static void TestPickActiveUser()
        {
            string mr = "\"users\" { \"76561197960265729\" { \"MostRecent\" \"0\" \"Timestamp\" \"900\" }" +
                        " \"76561197960265730\" { \"MostRecent\" \"1\" \"Timestamp\" \"100\" } }";
            Assert.Equal("76561197960265730", SteamUsers.PickActiveUser(SteamKvParser.Parse(mr)), "MostRecent wins");
            string ts = "\"users\" { \"76561197960265729\" { \"Timestamp\" \"100\" } \"76561197960265730\" { \"Timestamp\" \"900\" } }";
            Assert.Equal("76561197960265730", SteamUsers.PickActiveUser(SteamKvParser.Parse(ts)), "highest Timestamp");
            Assert.Equal("", SteamUsers.PickActiveUser(SteamKvParser.Parse("\"users\" { }")));
            Assert.Equal("", SteamUsers.PickActiveUser(null));
        }

        public static void TestLocalConfigExtraction()
        {
            string lc = "\"UserLocalConfigStore\"\n{\n \"friends\" { \"apps\" { \"730\" { \"Playtime\" \"999999\" } } }\n" +
                        " \"Software\"\n {\n  \"valve\"\n  {\n   \"Steam\"\n   {\n    \"cloud\" { \"x\" \"y\" }\n" +
                        "    \"apps\"\n    {\n     \"730\" { \"LastPlayed\" \"1700000000\" \"Playtime\" \"1234\" \"Playtime2wks\" \"60\" \"cloud\" { \"q\" \"1\" } }\n" +
                        "     \"480\" { \"LastPlayed\" \"0\" \"Playtime\" \"0\" }\n     \"218\" { \"LastPlayed\" \"1638328173\" }\n" +
                        "     \"notanid\" { \"Playtime\" \"5\" }\n    }\n   }\n  }\n }\n}\n";
            var stats = SteamUsers.ReadPlayStats(new StringReader(lc));
            Assert.Equal(2, stats.Count);
            Assert.Equal(1234L, stats["730"].PlaytimeMinutes);
            Assert.Equal(1700000000L, stats["730"].LastPlayedUnix);
            Assert.Equal(0L, stats["218"].PlaytimeMinutes);
            Assert.False(stats.ContainsKey("480"), "never played");
        }

        public static void TestLastPlayedConversion()
        {
            DateTime? d = SteamUsers.FromUnix(1700000000);
            Assert.NotNull(d);
            Assert.Equal(new DateTime(2023, 11, 14, 22, 13, 20, DateTimeKind.Utc).ToLocalTime(), d.Value);
            Assert.Equal(DateTimeKind.Local, d.Value.Kind);
            Assert.True(SteamUsers.FromUnix(0) == null);
            Assert.True(SteamUsers.FromUnix(-5) == null);
            Assert.True(SteamUsers.FromUnix(long.MaxValue) == null);
        }

        public static void TestUris()
        {
            var s = new SteamLocalData();
            Assert.Equal("steam://validate/730", s.ValidateUri("730"));
            Assert.Equal("steam://uninstall/730", s.UninstallUri(" 730 "));
            Assert.Equal("", s.ValidateUri("73a"));
            Assert.Equal("", s.UninstallUri(""));
            Assert.Equal("", s.UninstallUri(null));
        }

        // ------------------------------------------------------------------ end to end (temp Steam tree)

        public static void TestLoadFromTempSteamTree()
        {
            string root = Path.Combine(Path.GetTempPath(), "gh-steamdata-" + Guid.NewGuid().ToString("N"));
            try
            {
                string lib2 = Path.Combine(root, "lib2");
                Write(root, "steamapps\\libraryfolders.vdf",
                    "\"libraryfolders\" { \"0\" { \"path\" \"" + Esc(root) + "\" } \"1\" { \"path\" \"" + Esc(lib2) + "\" } }");
                Write(root, "steamapps\\appmanifest_730.acf",
                    "\"AppState\" { \"appid\" \"730\" \"name\" \"CS2\" \"StateFlags\" \"4\" \"installdir\" \"Counter-Strike Global Offensive\" \"SizeOnDisk\" \"100\" \"LastPlayed\" \"1600000000\" }");
                Write(lib2, "steamapps\\appmanifest_359550.acf",
                    "\"AppState\" { \"appid\" \"359550\" \"name\" \"R6\" \"StateFlags\" \"6\" \"installdir\" \"R6\" \"SizeOnDisk\" \"200\" \"LastPlayed\" \"1500000000\" }");
                Write(root, "config\\loginusers.vdf",
                    "\"users\" { \"76561197960265738\" { \"MostRecent\" \"0\" } \"76561197960265739\" { \"MostRecent\" \"1\" } }");
                // account 11 is active; account 10 must be ignored
                Write(root, "userdata\\10\\config\\localconfig.vdf", LocalConfig("730", "1", "1"));
                Write(root, "userdata\\11\\config\\localconfig.vdf", LocalConfig("730", "90", "1700000000") );

                var data = new SteamLocalData(root);
                var r = data.Load();
                Assert.Equal(2, r.Count);
                Assert.Equal(90L, r["730"].PlaytimeMinutes);
                Assert.Equal(SteamUsers.FromUnix(1700000000), r["730"].LastPlayed);
                Assert.Equal(Path.Combine(root, "steamapps\\common\\Counter-Strike Global Offensive"), r["730"].InstallDir);
                Assert.False(r["730"].UpdatePending);
                Assert.True(r["359550"].UpdatePending);
                Assert.Equal(200L, r["359550"].SizeOnDisk);
                Assert.Equal(SteamUsers.FromUnix(1500000000), r["359550"].LastPlayed, "manifest LastPlayed fallback");

                // Fresh copy each call: mutations don't leak into the cache.
                r["730"].PlaytimeMinutes = -1;
                Assert.Equal(90L, data.Load()["730"].PlaytimeMinutes);

                // Cache invalidates when a file changes (mtime bumped).
                string lc = Path.Combine(root, "userdata\\11\\config\\localconfig.vdf");
                File.WriteAllText(lc, LocalConfig("730", "120", "1700000000"));
                File.SetLastWriteTimeUtc(lc, DateTime.UtcNow.AddMinutes(5));
                Assert.Equal(120L, data.Load()["730"].PlaytimeMinutes);

                // Active account without userdata → most recently modified localconfig.
                Write(root, "config\\loginusers.vdf", "\"users\" { \"76561197960265800\" { \"MostRecent\" \"1\" } }");
                File.SetLastWriteTimeUtc(Path.Combine(root, "userdata\\10\\config\\localconfig.vdf"), DateTime.UtcNow.AddMinutes(10));
                Assert.Equal(1L, data.Load()["730"].PlaytimeMinutes);
            }
            finally
            {
                try { Directory.Delete(root, true); } catch (Exception ex) { Console.WriteLine("cleanup failed: " + ex.Message); }
            }
        }

        public static void TestLoadMissingRootReturnsEmpty()
        {
            var data = new SteamLocalData(Path.Combine(Path.GetTempPath(), "gh-steamdata-missing-" + Guid.NewGuid().ToString("N")));
            Assert.Equal(0, data.Load().Count);
        }

        private static string LocalConfig(string appId, string minutes, string last) =>
            "\"UserLocalConfigStore\" { \"Software\" { \"Valve\" { \"Steam\" { \"apps\" { \"" + appId +
            "\" { \"LastPlayed\" \"" + last + "\" \"Playtime\" \"" + minutes + "\" } } } } } }";

        private static string Esc(string p) => p.Replace("\\", "\\\\");

        private static void Write(string baseDir, string rel, string text)
        {
            string f = Path.Combine(baseDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(f));
            File.WriteAllText(f, text);
        }
    }
}
