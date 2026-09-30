using System;
using System.Diagnostics;
using System.IO;

namespace GamesHub.Tests
{
    public static class GameRootResolverTests
    {
        private static readonly IGameRootProbe NoRegistry = new DiskGameRootProbe(new string[0]);

        /// <summary>Temp tree: each entry is a relative path; a trailing '\' creates a folder, otherwise an empty file.</summary>
        private static string Tree(params string[] entries)
        {
            string root = Path.Combine(Path.GetTempPath(), "gh-root-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            foreach (string e in entries)
            {
                string p = Path.Combine(root, e);
                if (e.EndsWith("\\")) Directory.CreateDirectory(p);
                else { Directory.CreateDirectory(Path.GetDirectoryName(p)); File.WriteAllText(p, ""); }
            }
            return root;
        }

        private static void Cleanup(string root)
        {
            try { Directory.Delete(root, true); }
            catch (IOException) { /* best effort: temp folder */ }
            catch (UnauthorizedAccessException) { /* best effort: temp folder */ }
        }

        private static string R(string exe, string gamesDir = null, IGameRootProbe probe = null)
            => GameRootResolver.Resolve(exe, null, gamesDir, probe ?? NoRegistry);

        public static void TestUnrealProjectClimbsToGameRoot()
        {
            string t = Tree(@"Lib\DIRT5\Engine\Binaries\ThirdParty\", @"Lib\DIRT5\DIRT5\Binaries\Win64\DIRT5.exe",
                            @"Lib\DIRT5\DIRT5\Content\Paks\a.pak", @"Lib\DIRT5\DIRT5.exe",
                            @"Lib\DIRT5\DIRT5\Binaries\Win64\steam_appid.txt");
            try
            {
                string root = Path.Combine(t, @"Lib\DIRT5");
                Assert.Equal(root, R(Path.Combine(root, @"DIRT5\Binaries\Win64\DIRT5.exe")), "shipping exe");
                Assert.Equal(root, R(Path.Combine(root, "DIRT5.exe")), "root stub exe");
            }
            finally { Cleanup(t); }
        }

        public static void TestUnrealWithoutEvidenceStopsAtProject()
        {
            // Parent of the project holds nothing of the game (a plain collection folder): stop at the project.
            string t = Tree(@"Stuff\Proj\Binaries\Win64\Proj.exe", @"Stuff\Proj\Content\", @"Stuff\Other\readme.txt");
            try { Assert.Equal(Path.Combine(t, @"Stuff\Proj"), R(Path.Combine(t, @"Stuff\Proj\Binaries\Win64\Proj.exe"))); }
            finally { Cleanup(t); }
        }

        public static void TestUnityRootIsExeFolder()
        {
            string t = Tree(@"Raft\Raft.exe", @"Raft\Raft_Data\", @"Raft\UnityPlayer.dll", @"Raft\steam_appid.txt");
            try { Assert.Equal(Path.Combine(t, "Raft"), R(Path.Combine(t, @"Raft\Raft.exe"))); }
            finally { Cleanup(t); }
        }

        public static void TestInnoUninstallerRootWinsOverSubFolder()
        {
            // Root with unins000.exe; the exe lives in a sub-folder with an arbitrary name.
            string t = Tree(@"Pack\Some Game\unins000.exe", @"Pack\Some Game\unins000.dat", @"Pack\Some Game\Data\x.dat",
                            @"Pack\Some Game\SomeGame\game.exe");
            try { Assert.Equal(Path.Combine(t, @"Pack\Some Game"), R(Path.Combine(t, @"Pack\Some Game\SomeGame\game.exe"))); }
            finally { Cleanup(t); }
        }

        public static void TestLauncherHostingGamesIsNotSwallowed()
        {
            // A launcher with its own uninstaller keeps several games in sub-folders: each game stays its own root.
            string t = Tree(@"CB\unins000.exe", @"CB\cb.exe", @"CB\BO3\BlackOps3.exe", @"CB\BO2\t6mp.exe");
            try { Assert.Equal(Path.Combine(t, @"CB\BO3"), R(Path.Combine(t, @"CB\BO3\BlackOps3.exe"))); }
            finally { Cleanup(t); }
        }

        public static void TestBinX64Climb()
        {
            string t = Tree(@"Game A\bin\x64\gamea.exe", @"Game A\bin\x64\engine.dll", @"Game A\data\");
            try { Assert.Equal(Path.Combine(t, "Game A"), R(Path.Combine(t, @"Game A\bin\x64\gamea.exe"))); }
            finally { Cleanup(t); }
        }

        public static void TestGenericSubFolderNeedsEvidence()
        {
            string t = Tree(@"With\Launcher.exe", @"With\Retail\wow.exe", @"Without\Game\solo.exe");
            try
            {
                Assert.Equal(Path.Combine(t, "With"), R(Path.Combine(t, @"With\Retail\wow.exe")), "parent has the launcher exe");
                Assert.Equal(Path.Combine(t, @"Without\Game"), R(Path.Combine(t, @"Without\Game\solo.exe")), "parent is just a folder");
            }
            finally { Cleanup(t); }
        }

        public static void TestNeverClimbsIntoLibraryFolders()
        {
            string t = Tree(@"SteamLibrary\steamapps\common\Portal\bin\portal.exe", @"Games\bin\x.exe", @"Jogos\Solo\Binaries\Win64\s.exe",
                            @"Epic Games\Fortnite\unins000.exe");
            try
            {
                Assert.Equal(Path.Combine(t, @"SteamLibrary\steamapps\common\Portal"), R(Path.Combine(t, @"SteamLibrary\steamapps\common\Portal\bin\portal.exe")));
                Assert.Equal(Path.Combine(t, @"Games\bin"), R(Path.Combine(t, @"Games\bin\x.exe")), "never the Games container");
                Assert.Equal(Path.Combine(t, @"Jogos\Solo"), R(Path.Combine(t, @"Jogos\Solo\Binaries\Win64\s.exe")), "project directly in Jogos");
                Assert.Equal("", R(Path.Combine(t, @"SteamLibrary\steamapps\common\loose.exe")), "exe inside steamapps\\common");
            }
            finally { Cleanup(t); }
        }

        public static void TestNeverDriveRootOrLauncherFolder()
        {
            Assert.Equal("", R(@"C:\game.exe"), "drive root");
            Assert.Equal(@"Z:\bin", R(@"Z:\bin\game.exe"), "no climb to a drive root");
            Assert.Equal("", R(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"System32\notepad.exe")), "Windows");
            string t = Tree(@"Launchy\steam.exe", @"Launchy\bin\helper.exe", @"Hydra\Hydra.exe");
            try
            {
                Assert.Equal(Path.Combine(t, @"Launchy\bin"), R(Path.Combine(t, @"Launchy\bin\helper.exe")), "never climb into a launcher's folder");
                Assert.Equal("", R(Path.Combine(t, @"Launchy\steam.exe")), "the launcher's own folder");
                Assert.Equal("", R(Path.Combine(t, @"Hydra\Hydra.exe")), "Hydra folder");
            }
            finally { Cleanup(t); }
        }

        public static void TestLooseExeInGamesDirHasNoRoot()
        {
            string t = Tree(@"jogos\TEKKEN 7.exe", @"jogos\Sub\bin\x.exe");
            try
            {
                string games = Path.Combine(t, "jogos");
                Assert.Equal("", R(Path.Combine(games, "TEKKEN 7.exe"), games));
                Assert.Equal(Path.Combine(games, "Sub"), R(Path.Combine(games, @"Sub\bin\x.exe"), games), "never climbs to the games folder");
            }
            finally { Cleanup(t); }
        }

        public static void TestRegistryInstallLocationIsRoot()
        {
            string t = Tree(@"Pub\Title\Client\client.exe");
            try
            {
                string root = Path.Combine(t, @"Pub\Title");
                var probe = new DiskGameRootProbe(new[] { root + "\\" });
                Assert.Equal(root, R(Path.Combine(root, @"Client\client.exe"), null, probe), "registered location");
                Assert.Equal(Path.Combine(root, "Client"), R(Path.Combine(root, @"Client\client.exe")), "without registry: no evidence");
            }
            finally { Cleanup(t); }
        }

        public static void TestDeclaredInstallDirIsAuthoritative()
        {
            Assert.Equal(@"D:\Epic\Rocket League", GameRootResolver.Resolve(@"D:\Epic\Rocket League\Binaries\Win64\RL.exe", @"D:\Epic\Rocket League\", null, NoRegistry));
            Assert.Equal(@"Q:\Other", GameRootResolver.Resolve(@"Q:\Other\x.exe", @"Q:\Elsewhere", null, NoRegistry), "ignored when the exe is outside it");
            Assert.Equal(@"Q:\Other", GameRootResolver.Resolve(@"Q:\Other\x.exe", @"C:\", null, NoRegistry), "ignored when forbidden");
        }

        public static void TestMissingPathsUseNameRulesOnly()
        {
            Assert.Equal(@"Q:\Lib\CT\Chained Together", R(@"Q:\Lib\CT\Chained Together\ChainedTogether\Binaries\Win64\X-Win64-Shipping.exe"));
            Assert.Equal(@"Q:\Proj", R(@"Q:\Proj\Binaries\Win64\x.exe"), "project directly under a drive root");
            Assert.Equal(@"Q:\Tool", R(@"Q:\Tool\bin\x64\t.exe"));
            Assert.Equal(@"Q:\Tool\Game", R(@"Q:\Tool\Game\t.exe"), "generic names need evidence from the disk");
        }

        public static void TestFolderSourceUsesResolver()
        {
            string t = Tree(@"G\", @"Lib\RIDE 4\Engine\", @"Lib\RIDE 4\ride4.exe", @"Lib\RIDE 4\ride4\Binaries\Win64\ride4-Win64-Shipping.exe");
            try
            {
                string exe = Path.Combine(t, @"Lib\RIDE 4\ride4\Binaries\Win64\ride4-Win64-Shipping.exe");
                Game g = FolderSource.BuildGame(Path.Combine(t, @"G\RIDE 4.lnk"), new ShortcutInfo { Target = exe }, Path.Combine(t, "G"), NoRegistry);
                Assert.Equal(exe, g.Exe, "exe unchanged");
                Assert.Equal(Path.Combine(t, @"Lib\RIDE 4"), g.InstallDir);
            }
            finally { Cleanup(t); }
        }

        public static void TestMergeInheritsBroaderImportedInstallDir()
        {
            var folder = new Game { Id = "folder:rl.lnk", Source = "folder", Exe = @"D:\Epic\RL\Binaries\Win64\RL.exe", InstallDir = @"D:\Epic\RL\Binaries\Win64" };
            var epic = new Game { Id = "epic:Sugar", Source = "epic", InstallDir = @"D:\Epic\RL" };
            var r = LibraryMerge.Dedupe(new[] { folder }, new[] { epic });
            Assert.Equal(1, r.Count);
            Assert.Equal(@"D:\Epic\RL", r[0].InstallDir);
        }

        public static void TestFastWithWarmCache()
        {
            string t = Tree(@"Lib\G\Engine\", @"Lib\G\G.exe", @"Lib\G\G\Binaries\Win64\G-Win64-Shipping.exe", @"Lib\G\G\Content\Paks\p.pak");
            try
            {
                string exe = Path.Combine(t, @"Lib\G\G\Binaries\Win64\G-Win64-Shipping.exe");
                R(exe);
                var sw = Stopwatch.StartNew();
                long best = long.MaxValue;
                for (int i = 0; i < 20; i++)
                {
                    sw.Restart();
                    R(exe);
                    best = Math.Min(best, sw.ElapsedTicks);
                }
                double ms = best * 1000.0 / Stopwatch.Frequency;
                Assert.True(ms < 5, "resolve took " + ms.ToString("0.00") + " ms");
            }
            finally { Cleanup(t); }
        }
    }
}
