using System.Collections.Generic;

namespace GamesHub.Tests
{
    public static class SettingsStoreTests
    {
        private static bool AnyDir(string _) => true;

        public static void TestDtoContainsAllContractKeys()
        {
            Dictionary<string, object> dto = SettingsStore.ToDto(new AppSettings());
            foreach (string key in new[] { "gamesDir", "importSteam", "importEpic", "onLaunch", "startWithWindows", "startMinimized",
                         "closeToTray", "hotkeyEnabled", "hotkey", "sortBy", "view", "cardStyle", "reduceMotion", "autoArtwork",
                         "steamGridDbKey", "trackPlaytime", "checkUpdates", "updateRepo", "language",
                         "importRiot", "quickLaunchHotkey", "automationEnabled" })
                Assert.True(dto.ContainsKey(key), "missing key " + key);
        }

        public static void TestDtoRoundTripThroughJson()
        {
            var s = new AppSettings { GamesDir = @"D:\Jogos", OnLaunch = "none", ReduceMotion = true, Hotkey = "Ctrl+Shift+J", UpdateRepo = "owner/repo" };
            string json = Json.Serialize(SettingsStore.ToDto(s));
            var back = new AppSettings();
            SettingsPatchResult r = SettingsStore.ApplyPatchTo(back, Json.Deserialize<Dictionary<string, object>>(json), AnyDir);
            Assert.Equal(0, r.Rejected.Count, "rejected");
            Assert.Equal(@"D:\Jogos", back.GamesDir);
            Assert.Equal("none", back.OnLaunch);
            Assert.True(back.ReduceMotion);
            Assert.Equal("Ctrl+Shift+J", back.Hotkey);
            Assert.Equal("owner/repo", back.UpdateRepo);
        }

        public static void TestPatchReportsOnlyRealChanges()
        {
            var s = new AppSettings();
            SettingsPatchResult r = SettingsStore.ApplyPatchTo(s, new Dictionary<string, object>
            {
                ["view"] = "grid",          // same as default
                ["cardStyle"] = "PORTRAIT", // normalized
                ["unknownKey"] = 42,
            }, AnyDir);
            Assert.Equal(1, r.Changed.Count);
            Assert.True(r.Has("cardStyle"));
            Assert.Equal("portrait", s.CardStyle);
            Assert.Equal(0, r.Rejected.Count);
        }

        public static void TestPatchRejectsInvalidValues()
        {
            var s = new AppSettings();
            SettingsPatchResult r = SettingsStore.ApplyPatchTo(s, new Dictionary<string, object>
            {
                ["onLaunch"] = "explode",
                ["sortBy"] = 3,
                ["importSteam"] = "yes",
                ["hotkey"] = "G",
                ["updateRepo"] = "not a repo",
                ["steamGridDbKey"] = "abc-def!",
                ["gamesDir"] = "relative\\path",
            }, AnyDir);
            Assert.Equal(7, r.Rejected.Count);
            Assert.Equal(0, r.Changed.Count);
            Assert.Equal("tray", s.OnLaunch);
            Assert.Equal("Ctrl+Alt+G", s.Hotkey);
            Assert.True(s.ImportSteam);
        }

        public static void TestGamesDirMustExistAndIsNormalized()
        {
            var s = new AppSettings();
            SettingsPatchResult r = SettingsStore.ApplyPatchTo(s, new Dictionary<string, object> { ["gamesDir"] = @"C:\Nope" }, _ => false);
            Assert.True(r.Rejected.Contains("gamesDir"));
            r = SettingsStore.ApplyPatchTo(s, new Dictionary<string, object> { ["gamesDir"] = @"  C:\Jogos\  " }, AnyDir);
            Assert.True(r.Has("gamesDir"));
            Assert.Equal(@"C:\Jogos", s.GamesDir);
        }

        public static void TestHotkeyIsNormalized()
        {
            var s = new AppSettings();
            SettingsStore.ApplyPatchTo(s, new Dictionary<string, object> { ["hotkey"] = "alt + ctrl + k", ["quickLaunchHotkey"] = "shift+ctrl+space" }, AnyDir);
            Assert.Equal("Ctrl+Alt+K", s.Hotkey);
            Assert.Equal("Ctrl+Shift+Space", s.QuickLaunchHotkey);
        }

        public static void TestEmptyUpdateRepoAllowed()
        {
            var s = new AppSettings { UpdateRepo = "a/b" };
            SettingsPatchResult r = SettingsStore.ApplyPatchTo(s, new Dictionary<string, object> { ["updateRepo"] = "" }, AnyDir);
            Assert.True(r.Has("updateRepo"));
            Assert.Equal("", s.UpdateRepo);
        }

        public static void TestNormalizeDir()
        {
            Assert.Equal(@"C:\", SettingsStore.NormalizeDir(@"C:\"));
            Assert.Equal(@"C:\a\b", SettingsStore.NormalizeDir("\"C:\\a\\b\\\""));
            Assert.Equal(null, SettingsStore.NormalizeDir("a\\b"));
            Assert.Equal(null, SettingsStore.NormalizeDir("  "));
        }
    }
}
