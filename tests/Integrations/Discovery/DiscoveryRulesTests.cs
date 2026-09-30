using System.Collections.Generic;
using System.Linq;

namespace GamesHub.Tests
{
    public static class DiscoveryRulesTests
    {
        public static void TestNotAGameCatchesUtilitiesRuntimesAndLaunchers()
        {
            foreach (string s in new[] { "Microsoft Visual C++ 2015-2022 Redistributable (x64)", "NVIDIA Graphics Driver 551.23", "Steam",
                                         "Epic Games Launcher", "Ubisoft Connect", "Battle.net", "GOG GALAXY", "Riot Vanguard", "Discord",
                                         "EA app", "Unity Hub", "Unreal Engine", "Python 3.12.1 (64-bit)", "7-Zip 23.01 (x64)",
                                         "Windows SDK AddOn", "Microsoft Edge WebView2 Runtime", "EasyAntiCheat", "Ubisoft" })
                Assert.True(DiscoveryRules.IsNotAGame(s), s);
            foreach (string s in new[] { "Hollow Knight", "Driver: San Francisco", "Mirror's Edge", "Medal of Honor: Vanguard",
                                         "Chrome Hounds", "Legendary", "Steamworld Dig", "Ubisoft Game", "Zoombinis", "Update Game" })
                Assert.False(DiscoveryRules.IsNotAGame(s), s);
        }

        public static void TestPublishers()
        {
            Assert.True(DiscoveryRules.IsGamePublisher("CD PROJEKT RED"));
            Assert.True(DiscoveryRules.IsGamePublisher("Ubisoft Entertainment"));
            Assert.False(DiscoveryRules.IsGamePublisher("Notepad++ Team"));
            Assert.True(DiscoveryRules.IsGameishPublisher("Tiny Indie Studio"));
            Assert.True(DiscoveryRules.IsUtilityPublisher("NVIDIA Corporation"));
            Assert.True(DiscoveryRules.IsUtilityPublisher("Microsoft Corporation"));
            Assert.False(DiscoveryRules.IsUtilityPublisher("Microsoft Studios"));
        }

        public static void TestHelperExecutables()
        {
            foreach (string s in new[] { "unins000.exe", "UnityCrashHandler64.exe", "vc_redist.x64.exe", "vcredist_x86.exe", "DXSETUP.exe",
                                         "CrashReportClient.exe", "UE4PrereqSetup_x64.exe", "EasyAntiCheat_EOS_Setup.exe", "start_protected_game.exe",
                                         "BsSndRpt.exe", "QtWebEngineProcess.exe", "GameUpdater.exe", "UnrealCEFSubProcess.exe", "EpicWebHelper.exe",
                                         "dotNetFx40_Full_setup.exe", "oalinst.exe", "python.exe" })
                Assert.True(DiscoveryRules.IsHelperExe(s), s);
            foreach (string s in new[] { "hollow_knight.exe", "gamelaunchhelper.exe", "TEKKEN 7.exe", "Cuphead.exe", "GitarooMan.exe", "Nodebuster.exe" })
                Assert.False(DiscoveryRules.IsHelperExe(s), s);
        }

        public static void TestNamesAreNormalizedAndCleaned()
        {
            Assert.Equal("the witcher 3 wild hunt", DiscoveryRules.NormalizeName("The Witcher® 3: Wild Hunt"));
            Assert.Equal("pokemon", DiscoveryRules.NormalizeName("Pokémon"));
            Assert.Equal("Cool Game", DiscoveryRules.CleanDisplayName("Cool Game v1.2.3"));
            Assert.Equal("Tool", DiscoveryRules.CleanDisplayName("Tool (64-bit)"));
            Assert.Equal("TEKKEN 7", DiscoveryRules.CleanDisplayName("TEKKEN 7"));
            Assert.Equal("Lethal Company", DiscoveryRules.CleanFolderName("Lethal-Company-SteamRIP.com", out bool noisy));
            Assert.True(noisy);
            Assert.Equal("Hollow Knight", DiscoveryRules.CleanFolderName("Hollow Knight", out noisy));
            Assert.False(noisy);
            Assert.Equal("Dark Souls III", DiscoveryRules.CleanFolderName("Dark Souls III [FitGirl Repack]", out noisy));
        }

        public static void TestNameSimilarity()
        {
            Assert.Equal(1.0, DiscoveryRules.NameSimilarity("hollow_knight", "Hollow Knight"));
            Assert.True(DiscoveryRules.NameSimilarity("TekkenGame-Win64-Shipping", "TEKKEN 7") < 1);
            Assert.True(DiscoveryRules.NameSimilarity("ForzaHorizon5", "Forza Horizon 5") == 1);
            Assert.Equal(0.0, DiscoveryRules.NameSimilarity("", "x"));
        }

        public static void TestRegistryIconExe()
        {
            Assert.Equal(@"C:\G\game.exe", DiscoveryRegistry.IconExe(@"C:\G\game.exe,0"));
            Assert.Equal(@"C:\G\game.exe", DiscoveryRegistry.IconExe("\"C:\\G\\game.exe\",-101"));
            Assert.Equal("", DiscoveryRegistry.IconExe(@"C:\G\icon.ico"));
            Assert.Equal("", DiscoveryRegistry.IconExe(""));
        }

        public static void TestForbiddenRoots()
        {
            Assert.True(DiscoveryRoots.IsForbidden(DiscoveryRoots.Key(@"C:\")));
            Assert.True(DiscoveryRoots.IsForbidden(DiscoveryRoots.Key(System.Environment.GetFolderPath(System.Environment.SpecialFolder.Windows) + @"\System32")));
            Assert.True(DiscoveryRoots.IsForbidden(DiscoveryRoots.Key(@"D:\SteamLibrary\steamapps\common\Portal")));
            Assert.True(DiscoveryRoots.IsForbidden(DiscoveryRoots.Key(@"D:\Program Files")));
            Assert.False(DiscoveryRoots.IsForbidden(DiscoveryRoots.Key(@"D:\Jogos\Celeste")));
            Assert.Equal("", DiscoveryRoots.Key(@"\\server\share\game"));
        }

        public static void TestMergeKeepsBestAndCombinesReasons()
        {
            var merged = GameDiscovery.Merge(new List<DiscoveredGame>
            {
                new DiscoveredGame { Name = "raft", Exe = @"C:\G\Raft\Raft.exe", InstallDir = @"C:\G\Raft", Source = "folder", Confidence = 0.9, Reasons = { "Unity" } },
                new DiscoveredGame { Name = "Raft", Exe = @"c:\g\raft\raft.exe", InstallDir = @"C:\G\Raft", Source = "registry", Confidence = 0.6,
                                     Reasons = { "Registro do Windows" }, SizeBytes = 42, Publisher = "Redbeet" },
            });
            Assert.Equal(1, merged.Count);
            Assert.Equal("Raft", merged[0].Name);
            Assert.Equal("registry", merged[0].Source);
            Assert.Equal(0.9, merged[0].Confidence);
            Assert.Equal(42L, merged[0].SizeBytes);
            Assert.Equal("Unity|Registro do Windows", string.Join("|", merged[0].Reasons));
            Assert.Equal("Redbeet", merged[0].Publisher);
        }

        public static void TestSessionAcceptsOnlyLastDiscoveryResults()
        {
            var s = new DiscoverySession();
            s.Remember(new[] { new DiscoveredGame { Name = "Celeste", Exe = @"D:\Jogos\Celeste\Celeste.exe" } });
            var items = new List<object>
            {
                new Dictionary<string, object> { ["exe"] = @"d:\jogos\celeste\CELESTE.exe", ["name"] = "  Celeste\u0007 (PC)  " },
                new Dictionary<string, object> { ["exe"] = @"D:\Jogos\Celeste\Celeste.exe", ["name"] = "dup" },
                new Dictionary<string, object> { ["exe"] = @"C:\Windows\System32\cmd.exe", ["name"] = "x" },
                "garbage",
            };
            List<DiscoverySession.Item> r = s.Validate(items, _ => true);
            Assert.Equal(2, r.Count);
            Assert.Equal(@"D:\Jogos\Celeste\Celeste.exe", r[0].Exe);
            Assert.Equal("Celeste (PC)", r[0].Name);
            Assert.Equal(null, r[0].Error);
            Assert.True(r[1].Error != null && r[1].Error.Contains("última busca"));
            Assert.Equal("Celeste", s.Validate(new List<object> { new Dictionary<string, object> { ["exe"] = @"D:\Jogos\Celeste\Celeste.exe", ["name"] = " " } }, _ => true)[0].Name);
            Assert.True(s.Validate(new List<object> { new Dictionary<string, object> { ["exe"] = @"D:\Jogos\Celeste\Celeste.exe" } }, _ => false)[0].Error.Contains("não existe"));
            Assert.Equal(0, s.Validate("nope").Count);
            Assert.Equal(0, s.Validate(null).Count);
            s.Remember(new DiscoveredGame[0]);
            Assert.True(s.Validate(items.Take(1).ToList(), _ => true)[0].Error != null, "a new discovery replaces the allowlist");
            string longName = new string('a', 500);
            s.Remember(new[] { new DiscoveredGame { Name = "A", Exe = @"D:\A\a.exe" } });
            Assert.Equal(DiscoverySession.MaxNameLength, s.Validate(new List<object> { new Dictionary<string, object> { ["exe"] = @"D:\A\a.exe", ["name"] = longName } }, _ => true)[0].Name.Length);
        }
    }
}
