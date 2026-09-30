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
                         "steamGridDbKey", "steamGridDbKeySet", "trackPlaytime", "checkUpdates", "language",
                         "importRiot", "quickLaunchHotkey", "automationEnabled" })
                Assert.True(dto.ContainsKey(key), "missing key " + key);
            Assert.False(dto.ContainsKey("updateRepo"), "the update repository is not a setting");
        }

        public static void TestDtoRoundTripThroughJson()
        {
            var s = new AppSettings { GamesDir = @"D:\Jogos", OnLaunch = "none", ReduceMotion = true, Hotkey = "Ctrl+Shift+J" };
            string json = Json.Serialize(SettingsStore.ToDto(s));
            var back = new AppSettings();
            SettingsPatchResult r = SettingsStore.ApplyPatchTo(back, Json.Deserialize<Dictionary<string, object>>(json), AnyDir);
            Assert.Equal(0, r.Rejected.Count, "rejected");
            Assert.Equal(@"D:\Jogos", back.GamesDir);
            Assert.Equal("none", back.OnLaunch);
            Assert.True(back.ReduceMotion);
            Assert.Equal("Ctrl+Shift+J", back.Hotkey);
        }

        public static void TestUpdateRepoCannotBeChanged()
        {
            var s = new AppSettings();
            SettingsPatchResult r = SettingsStore.ApplyPatchTo(s, new Dictionary<string, object> { ["updateRepo"] = "evil/repo" }, AnyDir);
            Assert.Equal(0, r.Changed.Count);
            Assert.Equal(AppInfo.DefaultUpdateRepo, s.UpdateRepo);

            // Legacy settings.json with a custom repository: still loads, the value is ignored and the file gets rewritten.
            var legacy = new AppSettings();
            SettingsStore.FromDisk(legacy, new Dictionary<string, object> { ["updateRepo"] = "evil/repo", ["view"] = "list" }, out bool resave);
            Assert.True(resave);
            Assert.Equal("list", legacy.View);
            Assert.Equal(AppInfo.DefaultUpdateRepo, legacy.UpdateRepo);
            Assert.False(SettingsStore.ToDisk(legacy).ContainsKey("updateRepo"));
        }

        public static void TestSteamGridDbKeyIsNeverSentToTheUi()
        {
            var s = new AppSettings { SteamGridDbKey = "abc123SECRET" };
            Dictionary<string, object> dto = SettingsStore.ToDto(s);
            Assert.Equal("", dto["steamGridDbKey"]);
            Assert.Equal(true, dto["steamGridDbKeySet"]);
            Assert.False(Json.Serialize(dto).Contains("abc123SECRET"));
            Assert.Equal(false, SettingsStore.ToDto(new AppSettings())["steamGridDbKeySet"]);
            Assert.Equal("abc123SECRET", s.SteamGridDbKey, "in-memory value untouched");
        }

        public static void TestSteamGridDbKeyIsDpapiProtectedOnDisk()
        {
            var s = new AppSettings { SteamGridDbKey = "abc123SECRET" };
            Dictionary<string, object> disk = SettingsStore.ToDisk(s);
            string json = Json.Serialize(disk);
            Assert.False(json.Contains("abc123SECRET"), "plain key on disk");
            Assert.False(disk.ContainsKey("steamGridDbKey"));
            Assert.True(disk["steamGridDbKeyProtected"] is string blob && blob.Length > 20);

            var back = new AppSettings();
            SettingsStore.FromDisk(back, Json.Deserialize<Dictionary<string, object>>(json), out bool resave);
            Assert.Equal("abc123SECRET", back.SteamGridDbKey);
            Assert.False(resave);
            Assert.False(SettingsStore.ToDisk(new AppSettings()).ContainsKey("steamGridDbKeyProtected"), "no key, nothing stored");
        }

        public static void TestPlainSteamGridDbKeyIsMigrated()
        {
            var s = new AppSettings();
            SettingsStore.FromDisk(s, new Dictionary<string, object> { ["steamGridDbKey"] = "legacyPlainKey1" }, out bool resave);
            Assert.Equal("legacyPlainKey1", s.SteamGridDbKey);
            Assert.True(resave, "plain secret must be re-saved protected");

            // Undecryptable blob (other user/machine, corruption): ignored, never thrown.
            var t = new AppSettings();
            SettingsPatchResult r = SettingsStore.FromDisk(t, new Dictionary<string, object> { ["steamGridDbKeyProtected"] = "AAAA" }, out _);
            Assert.Equal("", t.SteamGridDbKey);
            Assert.Equal(0, r.Rejected.Count);
        }

        public static void TestSecretProtectorRoundTrip()
        {
            string blob = SecretProtector.Protect("hello");
            Assert.NotNull(blob);
            Assert.Equal("hello", SecretProtector.Unprotect(blob));
            Assert.False(blob == SecretProtector.Protect("hello"), "DPAPI output is salted");
            Assert.Equal(null, SecretProtector.Unprotect("not base64!"));
            Assert.Equal(null, SecretProtector.Unprotect(""));
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
                ["steamGridDbKey"] = "abc-def!",
                ["gamesDir"] = "relative\\path",
            }, AnyDir);
            Assert.Equal(6, r.Rejected.Count);
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

        public static void TestNormalizeDir()
        {
            Assert.Equal(@"C:\", SettingsStore.NormalizeDir(@"C:\"));
            Assert.Equal(@"C:\a\b", SettingsStore.NormalizeDir("\"C:\\a\\b\\\""));
            Assert.Equal(null, SettingsStore.NormalizeDir("a\\b"));
            Assert.Equal(null, SettingsStore.NormalizeDir("  "));
        }
    }
}
