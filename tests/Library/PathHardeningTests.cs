using System;

namespace GamesHub.Tests
{
    /// <summary>Paths built from outside data (manifests, names) must stay inside their base folder.</summary>
    public static class PathHardeningTests
    {
        public static void TestSafePathCombineKeepsInsideBase()
        {
            Assert.Equal(@"C:\Base\a\b.txt", SafePath.Combine(@"C:\Base", "a", "b.txt"));
            Assert.Equal(@"C:\Base\x..y", SafePath.Combine(@"C:\Base", "x..y"), "dots inside a name are fine");
            Assert.True(SafePath.Combine(@"C:\Base", @"..\evil") == null, "parent traversal");
            Assert.True(SafePath.Combine(@"C:\Base", @"a\..\..\evil") == null, "nested traversal");
            Assert.True(SafePath.Combine(@"C:\Base", "../evil") == null, "forward-slash traversal");
            Assert.True(SafePath.Combine(@"C:\Base", @"C:\Windows") == null, "rooted segment");
            Assert.True(SafePath.Combine(@"C:\Base", @"\Windows") == null, "drive-rooted segment");
            Assert.True(SafePath.Combine(@"C:\Base", @"D:evil") == null, "drive-relative segment");
            Assert.True(SafePath.Combine(@"C:\Base", "a", null) == null, "null part");
            Assert.True(SafePath.Combine(@"C:\Base", "bad|name") == null, "invalid characters");
            Assert.True(SafePath.Combine(@"C:\Base", @"..\BaseEvil") == null, "sibling with the same prefix");
        }

        public static void TestSafePathIsInside()
        {
            Assert.True(SafePath.IsInside(@"C:\Base", @"C:\Base"));
            Assert.True(SafePath.IsInside(@"C:\Base\", @"c:\base\sub\f.txt"), "case-insensitive");
            Assert.False(SafePath.IsInside(@"C:\Base", @"C:\BaseEvil\f.txt"), "prefix of another folder");
            Assert.False(SafePath.IsInside(@"C:\Base", @"C:\Base\..\x"));
            Assert.False(SafePath.IsInside(@"C:\Base", ""));
        }

        private static string Acf(string dir) =>
            "\"AppState\"\n{\n\t\"appid\"\t\t\"730\"\n\t\"name\"\t\t\"CS2\"\n\t\"StateFlags\"\t\t\"4\"\n\t\"installdir\"\t\t\"" + dir + "\"\n}";

        public static void TestSteamManifestInstallDirCannotEscapeLibrary()
        {
            Assert.Equal(@"F:\Lib\steamapps\common\Counter-Strike", SteamSource.ParseAppManifest(Acf("Counter-Strike"), @"F:\Lib").InstallDir);
            Assert.Equal("", SteamSource.ParseAppManifest(Acf("../../../Windows"), @"F:\Lib").InstallDir, "traversal");
            Assert.Equal("", SteamSource.ParseAppManifest(Acf("C:/Windows"), @"F:\Lib").InstallDir, "rooted");
            Assert.NotNull(SteamSource.ParseAppManifest(Acf("../../../Windows"), @"F:\Lib"), "the game itself is still listed");
        }

        private static string EpicItem(string exe) =>
            "{ \"AppName\": \"Sugar\", \"DisplayName\": \"Rocket League\", \"InstallLocation\": \"D:\\\\Epic\\\\RL\","
            + " \"LaunchExecutable\": \"" + exe + "\", \"AppCategories\": [\"games\"] }";

        public static void TestEpicLaunchExecutableCannotEscapeInstallDir()
        {
            Assert.Equal(@"D:\Epic\RL\Binaries\RL.exe", EpicSource.ParseManifest(EpicItem("Binaries/RL.exe")).Exe);
            Assert.Equal("", EpicSource.ParseManifest(EpicItem("../../Windows/notepad.exe")).Exe, "traversal");
            Assert.Equal("", EpicSource.ParseManifest(EpicItem("C:/Windows/notepad.exe")).Exe, "rooted");
        }

        public static void TestUniquePathRejectsNonPlainNames()
        {
            Assert.Equal(@"C:\G\Game.lnk", GameRules.UniquePath(@"C:\G", "Game", ".lnk", _ => false));
            foreach (string bad in new[] { @"..\Game", @"sub\Game", "C:Game", "", ".." })
            {
                bool threw = false;
                try { GameRules.UniquePath(@"C:\G", bad, "", _ => false); }
                catch (ArgumentException) { threw = true; }
                Assert.True(threw, "expected rejection of '" + bad + "'");
            }
        }
    }
}
