using System;
using System.IO;
using System.Linq;

namespace GamesHub.Tests
{
    public static class GameEvidenceTests
    {
        private static Evidence Analyze(string dir) => GameEvidence.Analyze(FolderSnapshot.Take(dir, new ScanBudget()));

        public static void TestUnityFolderScoresHighWithReasons()
        {
            using (var fx = new DiscoveryFixture())
            {
                Evidence ev = Analyze(fx.UnityGame("Hollow Knight", "hollow_knight"));
                Assert.True(ev.Score >= GameEvidence.Preselect, "score " + ev.Score);
                Assert.Equal("Unity", ev.Reasons[0]);
                Assert.True(ev.Reasons.Contains("Pasta de dados Unity"));
            }
        }

        public static void TestUnrealFolderDetectedThroughBinariesAndPaks()
        {
            using (var fx = new DiscoveryFixture())
            {
                Evidence ev = Analyze(fx.UnrealGame("Tekken 7", "TEKKEN 7", "TekkenGame"));
                Assert.True(ev.Score >= GameEvidence.Preselect, "score " + ev.Score);
                Assert.True(ev.Reasons.Contains("Unreal (-Shipping.exe)"), string.Join(",", ev.Reasons));
                Assert.True(ev.Reasons.Contains("Unreal Engine"));
                Assert.True(ev.Reasons.Contains("Pacotes .pak"));
            }
        }

        public static void TestGodotAndStoreMarkers()
        {
            using (var fx = new DiscoveryFixture())
            {
                Evidence godot = Analyze(fx.Tree("Brotato", "Brotato.exe", "Brotato.pck"));
                Assert.True(godot.Reasons.Contains("Godot (.pck)"));
                Evidence gog = Analyze(fx.Tree("Witcher", "witcher3.exe", "goggame-1207664663.info", "galaxy64.dll"));
                Assert.True(gog.Score >= 0.8, "gog " + gog.Score);
                Assert.Equal("GOG", gog.Reasons[0]);
                Evidence egs = Analyze(fx.Tree("Alan", "AlanWake2.exe", ".egstore\\x.manifest", "EOSSDK-Win64-Shipping.dll"));
                Assert.True(egs.Reasons.Contains("Epic Games Store") && egs.Reasons.Contains("Epic Online Services"));
            }
        }

        public static void TestMiddlewareIsCappedAndUtilityScoresLow()
        {
            using (var fx = new DiscoveryFixture())
            {
                Evidence mw = Analyze(fx.Tree("Old", "game.exe", "steam_api.dll", "fmodex.dll", "binkw32.dll", "xinput1_3.dll", "d3dx9_43.dll", "OpenAL32.dll"));
                Assert.True(Math.Abs(mw.Score - 0.5) < 1e-9, "middleware capped at 0.5, got " + mw.Score);
                Evidence tool = Analyze(fx.Tree("Tool", "tool.exe", "Qt5Core.dll", "xinput1_4.dll"));
                Assert.True(tool.Score < GameEvidence.Threshold, "tool " + tool.Score);
            }
        }

        public static void TestElectronAppIsNegative()
        {
            using (var fx = new DiscoveryFixture())
            {
                Evidence ev = Analyze(fx.Tree("Chat", "Chat.exe", "libcef.dll", "resources\\app.asar", "steam_api64.dll"));
                Assert.True(ev.Score < 0, "electron " + ev.Score);
            }
        }

        public static void TestXboxConfigNameIsRead()
        {
            using (var fx = new DiscoveryFixture())
            {
                string dir = fx.Tree("Forza", "Content\\gamelaunchhelper.exe:50000", "Content\\ForzaHorizon5.exe:900000");
                File.WriteAllText(Path.Combine(dir, "Content", "MicrosoftGame.config"),
                    "<?xml version=\"1.0\"?><Game configVersion=\"1\"><Identity Name=\"Microsoft.Forza\"/>" +
                    "<ShellVisuals DefaultDisplayName=\"Forza Horizon 5\" PublisherDisplayName=\"Xbox Game Studios\"/></Game>");
                Evidence ev = Analyze(dir);
                Assert.True(ev.Xbox);
                Assert.Equal("Forza Horizon 5", ev.XboxName);
            }
        }

        public static void TestXboxConfigWithDtdIsRejectedSafely()
        {
            using (var fx = new DiscoveryFixture())
            {
                string dir = fx.Tree("Evil", "Content\\game.exe");
                string cfg = Path.Combine(dir, "Content", "MicrosoftGame.config");
                File.WriteAllText(cfg, "<?xml version=\"1.0\"?><!DOCTYPE g [<!ENTITY x \"boom\">]><Game><ShellVisuals DefaultDisplayName=\"&x;\"/></Game>");
                Assert.Equal("", GameEvidence.XboxDisplayName(cfg));
            }
        }

        public static void TestSnapshotSkipsRedistAndLauncherFolders()
        {
            using (var fx = new DiscoveryFixture())
            {
                string dir = fx.Tree("Pub", "_CommonRedist\\vcredist_x64.exe", "Ubisoft Game Launcher\\UbisoftConnect.exe", "Game\\game.exe");
                FolderSnapshot s = FolderSnapshot.Take(dir, new ScanBudget());
                Assert.Equal("game\\game.exe", string.Join("|", s.Exes.Select(e => e.Rel)));
                Assert.True(s.Dirs.Contains("_commonredist"), "skipped folders are still recorded");
            }
        }

        public static void TestSnapshotRespectsBudget()
        {
            using (var fx = new DiscoveryFixture())
            {
                string dir = fx.Tree("Big", Enumerable.Range(0, 50).Select(i => "f" + i + ".dll").ToArray());
                var budget = new ScanBudget(maxEntries: 10);
                FolderSnapshot s = FolderSnapshot.Take(dir, budget);
                Assert.True(s.Files.Count <= 10, "files " + s.Files.Count);
                Assert.True(budget.Exhausted);
            }
        }

        public static void TestMissingFolderIsNotReadable()
        {
            FolderSnapshot s = FolderSnapshot.Take(Path.Combine(Path.GetTempPath(), "gh-nope-" + Guid.NewGuid()), new ScanBudget());
            Assert.False(s.Readable);
            Assert.Equal(0.0, GameEvidence.Analyze(s).Score);
        }
    }
}
