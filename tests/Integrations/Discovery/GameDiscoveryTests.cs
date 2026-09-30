using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GamesHub.Tests
{
    public static class GameDiscoveryTests
    {
        private static string Names(IEnumerable<DiscoveredGame> list) => string.Join("|", list.Select(g => g.Name).OrderBy(n => n));

        public static void TestMainExeSkipsCrashHandlerAndUninstaller()
        {
            using (var fx = new DiscoveryFixture())
            {
                FolderSnapshot s = FolderSnapshot.Take(fx.UnityGame("Hollow Knight", "hollow_knight"), new ScanBudget());
                Assert.Equal("hollow_knight.exe", MainExePicker.Pick(s, new[] { "Hollow Knight" }).Name);
            }
        }

        public static void TestMainExePrefersUnrealRootStubOverEngineTools()
        {
            using (var fx = new DiscoveryFixture())
            {
                FolderSnapshot s = FolderSnapshot.Take(fx.UnrealGame("TEKKEN 7", "TEKKEN 7", "TekkenGame"), new ScanBudget());
                Assert.Equal("TEKKEN 7.exe", MainExePicker.Pick(s, new[] { "TEKKEN 7" }).Name);
            }
        }

        public static void TestMainExeUsesSizeAndNameAndPenalizesLaunchers()
        {
            using (var fx = new DiscoveryFixture())
            {
                FolderSnapshot s = FolderSnapshot.Take(fx.Tree("Space Game", "Launcher.exe:3000000", "SpaceGame.exe:2500000",
                    "SpaceGameServer.exe:4000000", "config.exe:10", "vc_redist.x64.exe:9000000"), new ScanBudget());
                Assert.Equal("SpaceGame.exe", MainExePicker.Pick(s, new[] { "Space Game" }).Name);
                Assert.Equal(null, MainExePicker.Pick(FolderSnapshot.Take(fx.Tree("Only helpers", "unins000.exe", "UnityCrashHandler64.exe"), new ScanBudget()), new string[0]));
            }
        }

        public static void TestDiscoverFindsGamesAndSkipsUtilities()
        {
            using (var fx = new DiscoveryFixture())
            {
                fx.UnityGame("Games\\Hollow Knight", "hollow_knight");
                fx.UnrealGame("Games\\TEKKEN 7", "TEKKEN 7", "TekkenGame");
                fx.Tree("Games\\Notepad Plus", "notepad++.exe:900000", "SciLexer.dll");
                fx.Tree("Games\\NVIDIA Corporation", "nvcontainer.exe", "PhysX3_x64.dll", "steam_api.dll", "xinput1_3.dll");
                List<DiscoveredGame> found = fx.Discovery(null, fx.GamesRoot()).Discover(new List<Game>());
                Assert.Equal("Hollow Knight|TEKKEN 7", Names(found));
                DiscoveredGame hk = found.First(g => g.Name == "Hollow Knight");
                Assert.Equal("folder", hk.Source);
                Assert.Equal(-1L, hk.SizeBytes);
                Assert.True(hk.Exe.EndsWith("hollow_knight.exe") && hk.Confidence >= GameEvidence.Preselect && hk.Confidence <= 1);
                Assert.True(hk.Reasons.Contains("Pasta de jogos") && hk.Reasons[0] == "Unity", string.Join(",", hk.Reasons));
                Assert.True(found.All(g => g.Confidence >= GameEvidence.Threshold));
            }
        }

        public static void TestKnownLibraryEntriesAreExcluded()
        {
            using (var fx = new DiscoveryFixture())
            {
                string hk = fx.UnityGame("Games\\Hollow Knight", "hollow_knight");
                string cup = fx.UnityGame("Games\\Cuphead", "Cuphead");
                string tk = fx.UnrealGame("Games\\TEKKEN 7", "TEKKEN 7", "TekkenGame");
                fx.UnityGame("Games\\Celeste", "Celeste");
                var known = new List<Game>
                {
                    new Game { Name = "HK (atalho)", Exe = Path.Combine(hk, "hollow_knight.exe") },                    // same exe
                    new Game { Name = "Algo", InstallDir = cup },                                                       // same folder
                    new Game { Name = "Tekken 7" },                                                                     // same normalized name
                    new Game { Name = "Pasta inteira", InstallDir = fx.Path_("Games") },                                // a container: ignored
                    new Game { Name = "Mod", Exe = Path.Combine(tk, "TekkenGame", "Binaries", "Win64", "Other.exe") },   // exe inside
                };
                List<DiscoveredGame> found = fx.Discovery(null, fx.GamesRoot()).Discover(known);
                Assert.Equal("Celeste", Names(found));
            }
        }

        public static void TestThresholdAndLocationPrior()
        {
            using (var fx = new DiscoveryFixture())
            {
                fx.Tree("Games\\Weak", "weak.exe", "xinput1_3.dll");                                   // 0.15 + 0.2 < 0.5
                fx.Tree("Games\\Classic", "classic.exe", "xinput1_3.dll", "steam_api.dll");            // 0.5 + 0.2
                fx.Tree("Apps\\Classic2", "classic2.exe", "xinput1_3.dll", "steam_api.dll");           // 0.5, no prior
                fx.Tree("Apps\\Tool", "tool.exe", "steam_api.dll");                                     // 0.35
                List<DiscoveredGame> found = fx.Discovery(null, fx.GamesRoot(), new ScanRoot(fx.Path_("Apps"), "programs"))
                    .Discover(new List<Game>());
                Assert.Equal("Classic|Classic2", Names(found));
                Assert.Equal(0.7, found.First(g => g.Name == "Classic").Confidence);
                Assert.Equal(0.5, found.First(g => g.Name == "Classic2").Confidence);
            }
        }

        public static void TestRegistryCandidateUsesDisplayNameSizeAndPublisher()
        {
            using (var fx = new DiscoveryFixture())
            {
                string dir = fx.Tree("Somewhere\\CoolGame", "cool.exe:5000", "cool_launcher.exe:9000", "fmod.dll");
                string driver = fx.Tree("Somewhere\\Drv", "drv.exe", "steam_api.dll", "UnityPlayer.dll");
                var registry = new List<RegistryApp>
                {
                    new RegistryApp { Name = "Cool Game v1.2.3", InstallLocation = dir, Publisher = "Devolver Digital", SizeBytes = 3L << 30,
                                      DisplayIcon = "\"" + Path.Combine(dir, "cool.exe") + "\",0" },
                    new RegistryApp { Name = "NVIDIA Graphics Driver 551.23", InstallLocation = driver, Publisher = "NVIDIA Corporation" },
                };
                List<DiscoveredGame> found = fx.Discovery(registry).Discover(new List<Game>());
                Assert.Equal(1, found.Count);
                DiscoveredGame g = found[0];
                Assert.Equal("Cool Game", g.Name);
                Assert.Equal("registry", g.Source);
                Assert.Equal(3L << 30, g.SizeBytes);
                Assert.Equal("Devolver Digital", g.Publisher);
                Assert.True(g.Exe.EndsWith("cool.exe"), g.Exe);
                Assert.Equal(0.6, g.Confidence); // FMOD 0.25 + publisher 0.25 + size 0.1
                Assert.True(g.Reasons.Contains("Registro do Windows") && g.Reasons.Contains("Editora de jogos"));
            }
        }

        public static void TestRegistryAndFolderAreMergedIntoOneCandidate()
        {
            using (var fx = new DiscoveryFixture())
            {
                string dir = fx.UnityGame("Games\\Raft", "Raft");
                var registry = new List<RegistryApp> { new RegistryApp { Name = "Raft", InstallLocation = dir, SizeBytes = 5L << 30 } };
                List<DiscoveredGame> found = fx.Discovery(registry, fx.GamesRoot()).Discover(new List<Game>());
                Assert.Equal(1, found.Count);
                Assert.Equal("registry", found[0].Source);
                Assert.True(found[0].Reasons.Contains("Pasta de jogos") && found[0].Reasons.Contains("Instalação grande"));
            }
        }

        public static void TestContainersAndReleaseNamesAndXbox()
        {
            using (var fx = new DiscoveryFixture())
            {
                fx.UnityGame("Games\\Indie Pack\\Game A", "GameA");
                fx.UnityGame("Games\\Indie Pack\\Game B", "GameB");
                fx.UnityGame("Games\\Lethal-Company-SteamRIP.com\\Lethal Company", "Lethal Company");
                string forza = fx.Tree("XboxGames\\Forza Horizon 5", "Content\\gamelaunchhelper.exe:5000", "Content\\ForzaHorizon5.exe:900000");
                File.WriteAllText(Path.Combine(forza, "Content", "MicrosoftGame.config"),
                    "<Game><ShellVisuals DefaultDisplayName=\"Forza Horizon 5\"/></Game>");
                fx.Tree("XboxGames\\GameSave", "x.exe");
                List<DiscoveredGame> found = fx.Discovery(null, fx.GamesRoot(), new ScanRoot(fx.Path_("XboxGames"), "xbox", false))
                    .Discover(new List<Game>());
                Assert.Equal("Forza Horizon 5|Game A|Game B|Lethal Company", Names(found));
                DiscoveredGame x = found.First(g => g.Name == "Forza Horizon 5");
                Assert.Equal("xbox", x.Source);
                Assert.True(x.Exe.EndsWith("Content\\gamelaunchhelper.exe"), x.Exe);
            }
        }

        public static void TestDiscoverNeverThrowsAndHonoursBudget()
        {
            using (var fx = new DiscoveryFixture())
            {
                fx.UnityGame("Games\\A", "A");
                var d = fx.Discovery(null, fx.GamesRoot(), new ScanRoot(fx.Path_("Missing"), "games"));
                d.MaxEntries = 1;
                Assert.Equal(0, d.Discover(new List<Game>()).Count);
                var broken = new GameDiscovery { Registry = () => throw new InvalidOperationException("boom"), Roots = () => new List<ScanRoot>() };
                Assert.Equal(0, broken.DiscoverAsync(null, default, null).Result.Count);
            }
        }

        public static void TestCancelledDiscoveryReturnsEmpty()
        {
            using (var fx = new DiscoveryFixture())
            using (var cts = new System.Threading.CancellationTokenSource())
            {
                fx.UnityGame("Games\\A", "A");
                cts.Cancel();
                Assert.Equal(0, fx.Discovery(null, fx.GamesRoot()).Discover(new List<Game>(), cts.Token).Count);
            }
        }
    }
}
