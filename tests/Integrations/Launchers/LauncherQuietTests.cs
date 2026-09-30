namespace GamesHub.Tests
{
    public static class LauncherQuietTests
    {
        public static void TestSteamLaunchDetection()
        {
            Assert.True(LauncherQuiet.IsSteamLaunch(new Game { LaunchTarget = "steam://rungameid/730" }), "steam uri");
            Assert.True(LauncherQuiet.IsSteamLaunch(new Game { LaunchTarget = "STEAM://run/570" }), "case-insensitive");
            Assert.False(LauncherQuiet.IsSteamLaunch(new Game { LaunchTarget = "com.epicgames.launcher://apps/Fortnite?action=launch&silent=true" }), "epic");
            Assert.False(LauncherQuiet.IsSteamLaunch(new Game { LaunchTarget = "" }), "empty");
            Assert.False(LauncherQuiet.IsSteamLaunch(new Game { LaunchTarget = null }), "null");
        }

        public static void TestLauncherProcessNames()
        {
            foreach (string n in new[] { "steam", "steamwebhelper", "EpicGamesLauncher", "RiotClientUx", "Battle.net", "EADesktop", "upc", "GalaxyClient", "Hydra" })
                Assert.True(LauncherQuiet.LauncherProcesses.Contains(n), n);
            Assert.True(LauncherQuiet.LauncherProcesses.Contains("STEAM"), "case-insensitive");
            Assert.False(LauncherQuiet.LauncherProcesses.Contains("cs2"), "games are never launchers");
            Assert.False(LauncherQuiet.LauncherProcesses.Contains("GamesHub"), "never ourselves");
        }
    }
}
