using System.Linq;

namespace GamesHub.Tests
{
    public static class LibraryParsingTests
    {
        public static void TestVdfNestedEscapesComments()
        {
            string text = "// header comment\n\"Root\"\n{\n  \"a\"  \"1\" // trailing\n  \"Path\" \"C:\\\\Games\\\\X \\\"Q\\\"\"\n" +
                          "  \"child\" { \"k\" \"v\" \"deep\" { \"z\" \"9\" } }\n  unquoted value\n  \"cond\" \"yes\" [$WIN32]\n}\n";
            VdfNode root = VdfParser.Parse(text).Node("root");
            Assert.NotNull(root, "root (case-insensitive)");
            Assert.Equal("1", root.Str("a"));
            Assert.Equal("C:\\Games\\X \"Q\"", root.Str("path"));
            Assert.Equal("v", root.Node("child").Str("k"));
            Assert.Equal("9", root.Node("child").Node("deep").Str("z"));
            Assert.Equal("value", root.Str("unquoted"));
            Assert.Equal("yes", root.Str("cond"));
        }

        public static void TestVdfMalformedIsTolerated()
        {
            VdfNode root = VdfParser.Parse("\"A\" { \"x\" \"1\" \"y\" { \"z\" \"unterminated");
            Assert.Equal("1", root.Node("A").Str("x"));
            Assert.NotNull(VdfParser.Parse(null));
            Assert.NotNull(VdfParser.Parse("}}}{"));
        }

        public static void TestLibraryFoldersNewAndOldFormat()
        {
            string modern = "\"libraryfolders\"\n{\n \"0\"\n {\n  \"path\" \"C:\\\\Program Files (x86)\\\\Steam\"\n  \"apps\" { \"228980\" \"1\" }\n }\n" +
                            " \"1\" { \"path\" \"F:\\\\SteamLibrary\" }\n}";
            var paths = SteamSource.ParseLibraryFolders(modern);
            Assert.Equal(2, paths.Count);
            Assert.Equal("F:\\SteamLibrary", paths[1]);
            var old = SteamSource.ParseLibraryFolders("\"LibraryFolders\" { \"TimeNextStatsReport\" \"123\" \"1\" \"D:\\\\Games\\\\Steam\" }");
            Assert.Equal(1, old.Count);
            Assert.Equal("D:\\Games\\Steam", old[0]);
        }

        private static string Acf(string appid, string name, string flags, string dir) =>
            "\"AppState\"\n{\n\t\"appid\"\t\t\"" + appid + "\"\n\t\"name\"\t\t\"" + name + "\"\n\t\"StateFlags\"\t\t\"" + flags +
            "\"\n\t\"installdir\"\t\t\"" + dir + "\"\n\t\"InstalledDepots\" { \"732\" { \"manifest\" \"1\" } }\n}";

        public static void TestAcfInstalledGame()
        {
            Game g = SteamSource.ParseAppManifest(Acf("730", "Counter-Strike 2", "4", "Counter-Strike Global Offensive"), "F:\\SteamLibrary");
            Assert.NotNull(g);
            Assert.Equal("steam:730", g.Id);
            Assert.Equal("Counter-Strike 2", g.Name);
            Assert.Equal("steam", g.Source);
            Assert.Equal("Steam", g.Platform);
            Assert.Equal("730", g.SteamAppId);
            Assert.Equal("steam://rungameid/730", g.LaunchTarget);
            Assert.Equal("F:\\SteamLibrary\\steamapps\\common\\Counter-Strike Global Offensive", g.InstallDir);
            Assert.NotNull(SteamSource.ParseAppManifest(Acf("730", "CS2", "1030", "x"), "C:\\L"), "flags with bit 4 set (updating)");
        }

        public static void TestAcfSkipsToolsAndUninstalled()
        {
            Assert.True(SteamSource.ParseAppManifest(Acf("228980", "Steamworks Common Redistributables", "4", "x"), "C:\\L") == null, "redist");
            Assert.True(SteamSource.ParseAppManifest(Acf("1493710", "Proton Experimental", "4", "x"), "C:\\L") == null, "proton");
            Assert.True(SteamSource.ParseAppManifest(Acf("1628350", "Steam Linux Runtime 3.0 (sniper)", "4", "x"), "C:\\L") == null, "runtime");
            Assert.True(SteamSource.ParseAppManifest(Acf("123", "Some Game", "2", "x"), "C:\\L") == null, "not installed");
            Assert.True(SteamSource.ParseAppManifest("garbage", "C:\\L") == null, "garbage");
        }

        private const string EpicItem = @"{
  ""FormatVersion"": 0, ""bIsIncompleteInstall"": false, ""DisplayName"": ""Fortnite"",
  ""InstallLocation"": ""C:\\Program Files\\Epic Games\\Fortnite"", ""LaunchExecutable"": ""FortniteGame/Binaries/Win64/FortniteLauncher.exe"",
  ""AppName"": ""Fortnite"", ""MainGameAppName"": ""Fortnite"", ""CatalogNamespace"": ""fn"",
  ""CatalogItemId"": ""4fe75bbc5a674f4f9b356b5c90567da5"", ""AppCategories"": [""public"", ""games"", ""applications""] }";

        public static void TestEpicManifest()
        {
            Game g = EpicSource.ParseManifest(EpicItem);
            Assert.NotNull(g);
            Assert.Equal("epic:Fortnite", g.Id);
            Assert.Equal("Fortnite", g.Name);
            Assert.Equal("Epic", g.Platform);
            Assert.Equal("epic", g.Source);
            Assert.Equal("com.epicgames.launcher://apps/fn%3A4fe75bbc5a674f4f9b356b5c90567da5%3AFortnite?action=launch&silent=true", g.LaunchTarget);
            Assert.Equal("C:\\Program Files\\Epic Games\\Fortnite", g.InstallDir);
            Assert.Equal("C:\\Program Files\\Epic Games\\Fortnite\\FortniteGame\\Binaries\\Win64\\FortniteLauncher.exe", g.Exe);
        }

        public static void TestEpicSkipsDlcAndIncomplete()
        {
            Assert.True(EpicSource.ParseManifest(EpicItem.Replace("\"bIsIncompleteInstall\": false", "\"bIsIncompleteInstall\": true")) == null, "incomplete");
            Assert.True(EpicSource.ParseManifest(EpicItem.Replace("\"MainGameAppName\": \"Fortnite\"", "\"MainGameAppName\": \"Other\"")) == null, "dlc by main app");
            Assert.True(EpicSource.ParseManifest(EpicItem.Replace("\"games\"", "\"addons\"")) == null, "addon category");
            Assert.True(EpicSource.ParseManifest("[1,2]") == null, "not an object");
        }

        public static void TestUrlFileParsing()
        {
            string content = "[{000214A0-0000-0000-C000-000000000046}]\r\nProp3=19,0\r\n[InternetShortcut]\r\nIDList=\r\nIconIndex=0\r\n" +
                             "URL=steam://rungameid/291550\r\nIconFile=C:\\Program Files (x86)\\Steam\\steam\\games\\x.ico\r\n";
            ShortcutInfo info = LibShellLink.ParseUrlFile(content);
            Assert.Equal("steam://rungameid/291550", info.Target);
            Assert.Equal("C:\\Program Files (x86)\\Steam\\steam\\games\\x.ico", info.IconPath);
            Assert.Equal("", LibShellLink.ParseUrlFile("[Other]\nURL=nope\n").Target, "URL outside section ignored");
            Assert.Equal("https://x.y", LibShellLink.ParseUrlFile(LibShellLink.BuildUrlFile("https://x.y")).Target, "roundtrip");
        }

        public static void TestSteamAppDetailsName()
        {
            Assert.Equal("Counter-Strike 2", ShortcutFactory.ParseAppDetailsName("{\"730\":{\"success\":true,\"data\":{\"name\":\"Counter-Strike 2\"}}}", "730"));
            Assert.Equal("", ShortcutFactory.ParseAppDetailsName("{\"1\":{\"success\":false}}", "1"));
        }
    }
}
