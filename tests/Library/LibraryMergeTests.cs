using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GamesHub.Tests
{
    public static class LibraryMergeTests
    {
        private static Game Folder(string file, string appId = "", string exe = "", string dir = "") => new Game
        {
            Id = GameRules.FolderId(file), Name = GameRules.CleanName(file), Source = "folder", FilePath = "C:\\G\\" + file,
            Ext = Path.GetExtension(file), SteamAppId = appId, Exe = exe, InstallDir = dir,
            LaunchTarget = appId.Length > 0 ? "steam://rungameid/" + appId : "C:\\G\\" + file,
        };

        private static Game Steam(string appId, string dir) => new Game
        { Id = "steam:" + appId, Name = "S" + appId, Source = "steam", Platform = "Steam", SteamAppId = appId, InstallDir = dir };

        public static void TestDedupeBySteamAppIdKeepsFolderAndInheritsInstallDir()
        {
            var result = LibraryMerge.Dedupe(new[] { Folder("Counter-Strike 2.url", "730") },
                                             new[] { Steam("730", "F:\\Lib\\steamapps\\common\\CS"), Steam("570", "F:\\Lib\\steamapps\\common\\Dota") });
            Assert.Equal(2, result.Count);
            Game f = result.First(g => g.Id == "folder:counter-strike 2.url");
            Assert.Equal("F:\\Lib\\steamapps\\common\\CS", f.InstallDir);
            Assert.True(result.Any(g => g.Id == "steam:570"));
            Assert.False(result.Any(g => g.Id == "steam:730"));
        }

        public static void TestDedupeByExeUnderInstallDirAndEpic()
        {
            var epic = new Game { Id = "epic:Sugar", Source = "epic", InstallDir = "D:\\Epic\\Rocket League" };
            var folderExe = Folder("Rocket League.lnk", exe: "D:\\Epic\\Rocket League\\Binaries\\RL.exe", dir: "D:\\Epic\\Rocket League\\Binaries");
            Assert.Equal(1, LibraryMerge.Dedupe(new[] { folderExe }, new[] { epic }).Count);

            var folderUrl = Folder("RL.url");
            folderUrl.LaunchTarget = "com.epicgames.launcher://apps/ns%3Aid%3ASugar?action=launch";
            var r = LibraryMerge.Dedupe(new[] { folderUrl }, new[] { epic });
            Assert.Equal(1, r.Count);
            Assert.Equal("D:\\Epic\\Rocket League", r[0].InstallDir);

            Assert.Equal(2, LibraryMerge.Dedupe(new[] { Folder("Other.lnk", exe: "E:\\X\\x.exe", dir: "E:\\X") }, new[] { epic }).Count);
        }

        public static void TestOverrideMerge()
        {
            Game src = Folder("Game.lnk", "10");
            var meta = new GameMeta
            {
                NameOverride = "  Meu Jogo ", LaunchArgs = "-windowed", SteamAppIdOverride = "20", Favorite = true, Hidden = true,
                Collections = new List<string> { "RPG" }, LastPlayed = new DateTime(2026, 1, 2), PlaySeconds = 99, AddedAt = new DateTime(2025, 5, 5),
            };
            Game g = LibraryMerge.ApplyMeta(src, meta, DateTime.Now);
            Assert.Equal("Meu Jogo", g.Name);
            Assert.Equal("-windowed", g.LaunchArgs);
            Assert.Equal("20", g.SteamAppId);
            Assert.True(g.Favorite && g.Hidden);
            Assert.Equal("RPG", g.Collections.Single());
            Assert.Equal(99L, g.PlaySeconds);
            Assert.Equal(new DateTime(2025, 5, 5), g.AddedAt);
            Assert.Equal("Game", src.Name, "source untouched");

            Game plain = LibraryMerge.ApplyMeta(src, null, new DateTime(2024, 1, 1));
            Assert.Equal("Game", plain.Name);
            Assert.Equal("10", plain.SteamAppId);
            Assert.Equal(new DateTime(2024, 1, 1), plain.AddedAt);
            Assert.Equal(2, LibraryMerge.NormalizeCollections(new[] { " RPG", "rpg", "", "Coop" }).Count);
        }

        public static void TestLegacyPlaylogMapping()
        {
            var raw = new Dictionary<string, object>
            {
                ["C:\\Users\\x\\Desktop\\jogos\\League of Legends.lnk"] = "2026-09-29T04:28:53.6616435-03:00",
                ["C:\\Users\\x\\Desktop\\jogos\\TEKKEN 7.exe"] = "2026-09-27T00:20:40.4616459-03:00",
                ["C:\\bad"] = "not a date",
            };
            var map = LibraryStore.MapLegacyPlaylog(raw);
            Assert.Equal(2, map.Count);
            Assert.True(map.ContainsKey("folder:league of legends.lnk"));
            DateTime expected = DateTimeOffset.Parse("2026-09-27T00:20:40.4616459-03:00").LocalDateTime;
            Assert.Equal(expected, map["folder:tekken 7.exe"]);
        }

        public static void TestStoreRoundtrip()
        {
            string dir = TempDir();
            try
            {
                string file = Path.Combine(dir, "library.json");
                var s = new LibraryStore(file);
                s.Edit("folder:a.lnk", m => { m.NameOverride = "A"; m.PlaySeconds = 42; m.Collections.Add("Coop"); m.LastPlayed = new DateTime(2026, 3, 4, 5, 6, 7); });
                s.SetIgnored("steam:1", true);
                s.Dispose();
                var t = new LibraryStore(file);
                t.Load();
                GameMeta m2 = t.Get("folder:a.lnk");
                Assert.Equal("A", m2.NameOverride);
                Assert.Equal(42L, m2.PlaySeconds);
                Assert.Equal("Coop", m2.Collections.Single());
                Assert.Equal(new DateTime(2026, 3, 4, 5, 6, 7), m2.LastPlayed);
                Assert.True(t.IsIgnored("steam:1"));
                Assert.True(File.ReadAllText(file).Contains("\"version\":1"));
                t.Dispose();
            }
            finally { Directory.Delete(dir, true); }
        }

        public static void TestTrashUniqueNamingAndRestore()
        {
            string dir = TempDir();
            try
            {
                string games = Path.Combine(dir, "games"), trash = Path.Combine(dir, "trash");
                Directory.CreateDirectory(games);
                var now = new DateTime(2026, 9, 29, 10, 11, 12);
                string p1 = Path.Combine(games, "Game.lnk");
                File.WriteAllText(p1, "one");
                string t1 = ShortcutFactory.MoveToTrash(p1, trash, now);
                Assert.Equal("Game (20260929-101112).lnk", Path.GetFileName(t1));
                File.WriteAllText(p1, "two");
                string t2 = ShortcutFactory.MoveToTrash(p1, trash, now);
                Assert.Equal("Game (20260929-101112) (2).lnk", Path.GetFileName(t2));
                Assert.Equal("one", File.ReadAllText(t1), "first trashed file is never overwritten");

                Assert.True(ShortcutFactory.RestoreFromTrash(t2, p1));
                Assert.False(ShortcutFactory.RestoreFromTrash(t1, p1), "name taken → refuse");
                Assert.True(File.Exists(t1), "refused restore leaves the trashed file in place");
                Assert.Equal("two", File.ReadAllText(p1));
            }
            finally { Directory.Delete(dir, true); }
        }

        public static void TestCreateShortcutsInTempDir()
        {
            string dir = TempDir();
            try
            {
                string games = Path.Combine(dir, "games");
                string url = ShortcutFactory.CreateSteamUrl(games, "730", "Counter-Strike: 2");
                Assert.Equal("Counter-Strike - 2.url", Path.GetFileName(url));
                Assert.Equal("steam://rungameid/730", LibShellLink.ParseUrlFile(File.ReadAllText(url)).Target);
                string url2 = ShortcutFactory.CreateSteamUrl(games, "730", "Counter-Strike: 2");
                Assert.Equal("Counter-Strike - 2 (2).url", Path.GetFileName(url2));

                // .exe → .lnk via IShellLink, then read back.
                string exe = Path.Combine(dir, "fake game.exe");
                File.WriteAllText(exe, "");
                string lnk = ShortcutFactory.CreateFromFile(games, exe, "Fake Game");
                Assert.Equal("Fake Game.lnk", Path.GetFileName(lnk));
                ShortcutInfo info = LibShellLink.ReadLnk(lnk);
                Assert.True(string.Equals(exe, info.Target, StringComparison.OrdinalIgnoreCase), "target " + info.Target);
                Assert.True(string.Equals(dir, info.WorkingDir, StringComparison.OrdinalIgnoreCase), "wd " + info.WorkingDir);

                // .lnk source → args preserved.
                string src = Path.Combine(dir, "src.lnk");
                LibShellLink.WriteLnk(src, new ShortcutInfo { Target = exe, Arguments = "-windowed", WorkingDir = dir });
                string copy = ShortcutFactory.CreateFromFile(games, src, "Fake Game");
                Assert.Equal("Fake Game (2).lnk", Path.GetFileName(copy));
                Assert.Equal("-windowed", LibShellLink.ReadLnk(copy).Arguments);

                var folderGames = new FolderSource().Scan(games);
                Assert.Equal(4, folderGames.Count);
                Assert.True(folderGames.Any(g => g.Id == "folder:fake game.lnk" && string.Equals(g.Exe, exe, StringComparison.OrdinalIgnoreCase)));
            }
            finally { Directory.Delete(dir, true); }
        }

        public static void TestLaunchStartInfo()
        {
            var url = new Game { Source = "folder", Ext = ".url", FilePath = "C:\\G\\x.url", LaunchTarget = "steam://rungameid/1" };
            Assert.Equal("steam://rungameid/1", GameLauncher.BuildStartInfo(url, null).FileName);
            var imported = new Game { Source = "steam", LaunchTarget = "steam://rungameid/2", LaunchArgs = "-x" };
            Assert.Equal("steam://rungameid/2", GameLauncher.BuildStartInfo(imported, null).FileName);
            var lnk = new Game { Source = "folder", Ext = ".lnk", FilePath = "C:\\G\\y.lnk" };
            Assert.Equal("C:\\G\\y.lnk", GameLauncher.BuildStartInfo(lnk, null).FileName);
            var exe = new Game { Source = "folder", Ext = ".exe", FilePath = "C:\\G\\z.exe", LaunchArgs = "-a" };
            var psi = GameLauncher.BuildStartInfo(exe, null);
            Assert.Equal("-a", psi.Arguments);
            Assert.Equal("C:\\G", psi.WorkingDirectory);
        }

        public static void TestCloneCopiesAllFieldsDeeply()
        {
            var g = new Game
            {
                Id = "x", SizeBytes = 123, UpdatePending = true, Broken = true, BrokenReason = "gone",
                Genres = new List<string> { "RPG" }, Variants = new List<GameVariant> { new GameVariant { Id = "y", Label = "DX11" } },
                Collections = new List<string> { "C" }, Art = new Artwork { Hero = "h" }, LastPlayed = new DateTime(2026, 1, 1),
            };
            Game c = LibraryMerge.Clone(g);
            Assert.Equal(123L, c.SizeBytes);
            Assert.True(c.UpdatePending && c.Broken && c.BrokenReason == "gone");
            Assert.Equal("RPG", c.Genres.Single());
            Assert.Equal("DX11", c.Variants.Single().Label);
            Assert.Equal("h", c.Art.Hero);
            c.Genres.Add("x"); c.Variants[0].Label = "changed"; c.Art.Hero = "changed"; c.Collections.Clear();
            Assert.Equal(1, g.Genres.Count, "lists are copied");
            Assert.Equal("DX11", g.Variants[0].Label, "variants are copied");
            Assert.Equal("h", g.Art.Hero, "art is copied");
            Assert.Equal(1, g.Collections.Count);
            Assert.Equal(1, LibraryMerge.ApplyMeta(g, null, DateTime.Now).Variants.Count, "ApplyMeta keeps wave-2 fields");
        }

        public static void TestLaunchImportedExeWithSourceArgs()
        {
            var riot = new Game { Source = "riot", LaunchTarget = @"C:\Riot Games\Riot Client\RiotClientServices.exe", LaunchArgs = "-user" };
            var psi = GameLauncher.BuildStartInfo(riot, null, "--launch-product=league_of_legends");
            Assert.Equal("--launch-product=league_of_legends -user", psi.Arguments);
            Assert.Equal(@"C:\Riot Games\Riot Client", psi.WorkingDirectory);
            var hydra = new Game { Source = "hydra", LaunchTarget = "hydralauncher://run?objectId=1", LaunchArgs = "-x" };
            Assert.Equal("", GameLauncher.BuildStartInfo(hydra, null, "-y").Arguments, "URIs get nothing appended");
        }

        public static void TestDedupeExtraSourceByAppId()
        {
            var hydra = new Game { Id = "hydra:2567870", Source = "hydra", SteamAppId = "2567870", InstallDir = @"F:\Hydra\Chained" };
            var r = LibraryMerge.Dedupe(new[] { Folder("Chained Together.lnk", "2567870") }, new[] { hydra });
            Assert.Equal(1, r.Count);
            Assert.Equal(@"F:\Hydra\Chained", r[0].InstallDir);
        }

        public static void TestTrackTargets()
        {
            var games = new[]
            {
                new Game { Id = "a", InstallDir = "D:\\Games\\A" },
                new Game { Id = "b", Exe = "C:\\Riot Games\\Riot Client\\RiotClientServices.exe" },
                new Game { Id = "c", InstallDir = "C:\\" },
                new Game { Id = "d", Exe = "C:\\G\\d.exe" },
            };
            var t = LibraryService.BuildTrackTargets(games, "C:\\G");
            Assert.Equal("a,d", string.Join(",", t.Select(x => x.Id)));
            Assert.Equal("D:\\Games\\A\\", t[0].DirPrefix);

            var shared = new List<TrackTarget>
            {
                new TrackTarget { Id = "bo3", Exe = "E:\\BO3\\BlackOps3.exe", DirPrefix = "E:\\BO3\\" },
                new TrackTarget { Id = "cb", Exe = "E:\\BO3\\cb-launcher.exe", DirPrefix = "E:\\BO3\\" },
            };
            var hit = new HashSet<string>();
            PlayTracker.Match("E:\\BO3\\BlackOps3.exe", shared, hit);
            Assert.Equal("bo3", string.Join(",", hit), "exact exe wins over shared dir");
            hit.Clear();
            PlayTracker.Match("E:\\BO3\\bin\\helper.exe", shared, hit);
            Assert.Equal(2, hit.Count, "dir-only match hits both");
        }

        private static string TempDir()
        {
            string d = Path.Combine(Path.GetTempPath(), "GamesHubLibTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(d);
            return d;
        }
    }
}
