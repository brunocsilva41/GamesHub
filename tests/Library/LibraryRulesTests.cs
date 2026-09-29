// OWNER: LIB agent. Names, ids, platform detection, app id extraction, folder game building.
using System;

namespace GamesHub.Tests
{
    public static class LibraryRulesTests
    {
        public static void TestPlatformDetectionTable()
        {
            var table = new[]
            {
                ("steam://rungameid/730", "Steam"),
                ("C:\\Program Files (x86)\\Steam\\steam.exe -applaunch 730", "Steam"),
                ("C:\\Hydra\\Hydra.exe hydralauncher://run?objectId=123", "Steam"),
                ("com.epicgames.launcher://apps/fn%3Aabc%3AFortnite?action=launch", "Epic"),
                ("C:\\Program Files (x86)\\Epic Games\\Launcher\\Portal\\Binaries\\Win64\\EpicGamesLauncher.exe", "Epic"),
                ("C:\\Riot Games\\Riot Client\\RiotClientServices.exe --launch-product=league_of_legends", "Riot"),
                ("C:\\Users\\x\\AppData\\Local\\Roblox\\Versions\\v1\\RobloxPlayerLauncher.exe", "Roblox"),
                ("C:\\Games\\SKlauncher\\SKlauncher.exe", "Minecraft"),
                ("C:\\Program Files\\Java\\bin\\javaw.exe -jar mc.jar", "Minecraft"),
                ("battlenet://WoW", "Battle.net"),
                ("C:\\Program Files (x86)\\Battle.net\\Battle.net.exe --exec=\"launch Pro\"", "Battle.net"),
                ("origin2://game/launch?offerIds=1", "EA"),
                ("C:\\Program Files\\Electronic Arts\\EA Desktop\\EA Desktop\\EADesktop.exe", "EA"),
                ("uplay://launch/635/0", "Ubisoft"),
                ("C:\\Program Files (x86)\\Ubisoft\\Ubisoft Game Launcher\\UbisoftConnect.exe", "Ubisoft"),
                ("goggalaxy://openGameView/1207658924", "GOG"),
                ("C:\\Program Files (x86)\\GOG Galaxy\\GalaxyClient.exe /command=runGame", "GOG"),
                ("D:\\Games\\TEKKEN 7\\TEKKEN 7.exe", "PC"),
                ("", "PC"),
            };
            foreach (var (target, expected) in table)
                Assert.Equal(expected, GameRules.DetectPlatform(target), target);
        }

        public static void TestSteamAppIdExtraction()
        {
            Assert.Equal("730", GameRules.ExtractSteamAppId("steam://rungameid/730"));
            Assert.Equal("570", GameRules.ExtractSteamAppId("steam://run/570"));
            Assert.Equal("1245620", GameRules.ExtractSteamAppId("hydralauncher://run?objectId=1245620&shop=steam"));
            Assert.Equal("440", GameRules.ExtractSteamAppId("C:\\Steam\\steam.exe -applaunch 440 -novid"));
            Assert.Equal("", GameRules.ExtractSteamAppId("steam://rungameid/13442302123416158208"), "64-bit non-Steam shortcut id");
            Assert.Equal("", GameRules.ExtractSteamAppId("C:\\Games\\x.exe"));
        }

        public static void TestNormalizeAppId()
        {
            Assert.Equal("730", GameRules.NormalizeAppId(" 730 "));
            Assert.Equal("1091500", GameRules.NormalizeAppId("https://store.steampowered.com/app/1091500/Cyberpunk_2077/"));
            Assert.Equal("", GameRules.NormalizeAppId("abc"));
            Assert.Equal("", GameRules.NormalizeAppId("0"));
            Assert.Equal("", GameRules.NormalizeAppId("73a0"));
        }

        public static void TestEpicAppNameExtraction()
        {
            Assert.Equal("Fortnite", GameRules.ExtractEpicAppName("com.epicgames.launcher://apps/fn%3Aabc%3AFortnite?action=launch&silent=true"));
            Assert.Equal("Sugar", GameRules.ExtractEpicAppName("com.epicgames.launcher://apps/Sugar?action=launch"));
            Assert.Equal("", GameRules.ExtractEpicAppName("steam://rungameid/1"));
        }

        public static void TestNameCleanup()
        {
            Assert.Equal("PointBlank", GameRules.CleanName("PointBlank.exe - Atalho.lnk"));
            Assert.Equal("Hot Wheels Unleashed 2", GameRules.CleanName("Hot Wheels Unleashed 2.exe.lnk"));
            Assert.Equal("Game", GameRules.CleanName("Game - Shortcut.lnk"));
            Assert.Equal("TEKKEN 7", GameRules.CleanName("TEKKEN 7.exe"));
            Assert.Equal("Counter-Strike 2", GameRules.CleanName("Counter-Strike 2.url"));
            Assert.Equal("DIRT 5 (Install Crack)", GameRules.CleanName("DIRT 5 (Install Crack).lnk"));
        }

        public static void TestIdsAndSanitize()
        {
            Assert.Equal("folder:tekken 7.exe", GameRules.FolderId("TEKKEN 7.exe"));
            Assert.Equal("steam:730", GameRules.SteamId("730"));
            Assert.Equal("epic:Fortnite", GameRules.EpicId("Fortnite"));
            Assert.Equal("Tom Clancy's - Siege", GameRules.SanitizeFileName("Tom Clancy's: Siege"));
            Assert.Equal("AC-DC", GameRules.SanitizeFileName("AC/DC"));
            Assert.Equal("Novo jogo", GameRules.SanitizeFileName(" ?*<> "));
            Assert.Equal("Bad", GameRules.SanitizeFileName("Bad\"|?..."));
        }

        public static void TestLauncherAndGenericDirs()
        {
            Assert.True(GameRules.IsLauncherExe("C:\\Program Files (x86)\\Steam\\Steam.exe"));
            Assert.True(GameRules.IsLauncherExe("C:\\Riot Games\\Riot Client\\RiotClientServices.exe"));
            Assert.True(GameRules.IsLauncherExe("C:\\x\\SKlauncher-3.2.exe"));
            Assert.False(GameRules.IsLauncherExe("D:\\Games\\TEKKEN 7\\TEKKEN 7.exe"));
            string games = "C:\\Users\\x\\Desktop\\jogos";
            Assert.True(GameRules.IsGenericDir("C:\\", games));
            Assert.True(GameRules.IsGenericDir(games, games));
            Assert.True(GameRules.IsGenericDir(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), games));
            Assert.True(GameRules.IsGenericDir(Environment.GetFolderPath(Environment.SpecialFolder.Windows) + "\\System32", games));
            Assert.True(GameRules.IsGenericDir("F:\\SteamLibrary\\steamapps\\common", games));
            Assert.False(GameRules.IsGenericDir("D:\\Games\\TEKKEN 7", games));
        }

        public static void TestFolderGameFromLnk()
        {
            string games = "C:\\G";
            var lnk = new ShortcutInfo { Target = "D:\\Games\\Raft\\Raft.exe", Arguments = "" };
            Game g = FolderSource.BuildGame("C:\\G\\Play Raft.lnk", lnk, games);
            Assert.Equal("folder:play raft.lnk", g.Id);
            Assert.Equal("Play Raft", g.Name);
            Assert.Equal("PC", g.Platform);
            Assert.Equal("C:\\G\\Play Raft.lnk", g.LaunchTarget);
            Assert.Equal("D:\\Games\\Raft\\Raft.exe", g.Exe);
            Assert.Equal("D:\\Games\\Raft", g.InstallDir);

            var riot = new ShortcutInfo { Target = "C:\\Riot Games\\Riot Client\\RiotClientServices.exe", Arguments = "--launch-product=league_of_legends" };
            Game r = FolderSource.BuildGame("C:\\G\\League of Legends.lnk", riot, games);
            Assert.Equal("Riot", r.Platform);
            Assert.Equal("", r.Exe, "launcher is not the game");

            var url = new ShortcutInfo { Target = "steam://rungameid/730" };
            Game u = FolderSource.BuildGame("C:\\G\\Counter-Strike 2.url", url, games);
            Assert.Equal("steam://rungameid/730", u.LaunchTarget);
            Assert.Equal("730", u.SteamAppId);
            Assert.Equal("Steam", u.Platform);
            Assert.Equal("", u.InstallDir);

            Game e = FolderSource.BuildGame("C:\\G\\TEKKEN 7.exe", new ShortcutInfo { Target = "C:\\G\\TEKKEN 7.exe" }, games);
            Assert.Equal("C:\\G\\TEKKEN 7.exe", e.Exe);
            Assert.Equal("", e.InstallDir, "games folder is too generic for process matching");
        }
    }
}
