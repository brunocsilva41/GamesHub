using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GamesHub.Tests
{
    public static class LibraryServiceTests
    {
        private sealed class FakeArt : IArtworkService
        {
            public int Resolves;
            public event Action<string> ArtworkUpdated;
            public Artwork Resolve(Game game)
            {
                Interlocked.Increment(ref Resolves);
                ArtworkUpdated?.Invoke(game.Id); // recursion bait: must not trigger another Resolve
                return new Artwork { Header = "k/" + game.Id + "/header.jpg" };
            }
            public void Raise(string id) => ArtworkUpdated?.Invoke(id);
            public OpResult SetCustomImage(Game game, string kind, string sourceFile) => OpResult.Success("");
            public OpResult ClearCustomImage(Game game, string kind) => OpResult.Success("");
            public void Refresh(Game game) { }
            public Task<List<SteamSearchResult>> SearchSteamAsync(string query) => Task.FromResult(new List<SteamSearchResult>());
            public void ImportLegacy(string legacyHubDir) { }
        }

        private sealed class FakeExtra : IExtraSource
        {
            public string Name { get; set; }
            public List<Game> Games = new List<Game>();
            public List<Game> Scan() => Games;
        }

        private static bool WaitFor(Func<bool> cond, int ms = 5000)
        {
            for (int waited = 0; waited < ms; waited += 25) { if (cond()) return true; Thread.Sleep(25); }
            return cond();
        }

        public static void TestServiceEndToEnd()
        {
            string root = Path.Combine(Path.GetTempPath(), "GamesHubLibSvc-" + Guid.NewGuid().ToString("N"));
            string games = Path.Combine(root, "jogos"), trash = Path.Combine(root, "trash"), outside = Path.Combine(root, "outside");
            Directory.CreateDirectory(Path.Combine(games, "_hub"));
            Directory.CreateDirectory(outside);
            File.WriteAllText(Path.Combine(games, "Alpha Game.url"), LibShellLink.BuildUrlFile("steam://rungameid/10"));
            File.WriteAllText(Path.Combine(games, "Beta.exe"), "");
            File.WriteAllText(Path.Combine(games, "desktop.ini"), "");
            File.WriteAllText(Path.Combine(games, "_hub", "playlog.json"),
                "{\"X:\\\\old\\\\jogos\\\\Beta.exe\":\"2026-09-27T00:20:40.0000000-03:00\"}");

            var settings = new AppSettings { GamesDir = games, ImportSteam = false, ImportEpic = false, TrackPlaytime = false };
            var art = new FakeArt();
            var lib = new LibraryService(settings, art, Path.Combine(root, "library.json"), trash);
            int changed = 0;
            lib.Changed += () => Interlocked.Increment(ref changed);
            try
            {
                lib.Start();
                Assert.True(WaitFor(() => lib.GetGames().Count == 2), "two games scanned");
                Assert.True(WaitFor(() => changed > 0), "Changed raised");
                Thread.Sleep(400);
                Assert.Equal(2, art.Resolves, "one Resolve per game, no recursion");

                Game beta = lib.Get("folder:beta.exe");
                Assert.NotNull(beta);
                Assert.Equal(DateTimeOffset.Parse("2026-09-27T00:20:40-03:00").LocalDateTime, beta.LastPlayed, "legacy playlog");
                Assert.Equal("k/folder:alpha game.url/header.jpg", lib.Get("folder:alpha game.url").Art.Header);

                // ArtworkUpdated from another thread → only that game is re-resolved.
                Task.Run(() => art.Raise("folder:alpha game.url")).Wait();
                Assert.True(WaitFor(() => art.Resolves == 3), "re-resolve on ArtworkUpdated");
                Thread.Sleep(300);
                Assert.Equal(3, art.Resolves);

                // Snapshot copies can't mutate the library.
                lib.GetGames()[0].Name = "hacked";
                Assert.False(lib.GetGames().Any(g => g.Name == "hacked"), "GetGames returns copies");

                OpResult up = lib.Update("folder:alpha game.url", new GameEdit { Name = "Alfa", Favorite = true, Collections = new List<string> { "Coop", " coop " } });
                Assert.True(up.Ok, up.Message);
                Game a = lib.Get("folder:alpha game.url");
                Assert.Equal("Alfa", a.Name);
                Assert.True(a.Favorite);
                Assert.Equal("Coop", string.Join(",", lib.GetCollections()));
                Assert.False(lib.Update("folder:alpha game.url", new GameEdit { SteamAppId = "abc" }).Ok, "bad app id");
                Assert.False(lib.Update("nope", new GameEdit()).Ok, "unknown id");

                // Remove → trash (never deleted) → undo.
                Assert.False(lib.Remove("folder:" + Path.Combine(games, "Beta.exe")).Ok, "arbitrary paths rejected");
                OpResult rm = lib.Remove("folder:beta.exe");
                Assert.True(rm.Ok && rm.UndoToken != null, rm.Message);
                Assert.False(File.Exists(Path.Combine(games, "Beta.exe")));
                Assert.Equal(1, Directory.GetFiles(trash).Length);
                Assert.True(lib.Get("folder:beta.exe") == null, "removed from library");
                OpResult undo = lib.Undo(rm.UndoToken);
                Assert.True(undo.Ok, undo.Message);
                Assert.True(File.Exists(Path.Combine(games, "Beta.exe")));
                Assert.NotNull(lib.Get("folder:beta.exe"));
                Assert.False(lib.Undo(rm.UndoToken).Ok, "token is single-use");

                // Add.
                string ext = Path.Combine(outside, "Gamma.url");
                File.WriteAllText(ext, LibShellLink.BuildUrlFile("https://example.com/game"));
                OpResult add = lib.AddFromFile(ext, "");
                Assert.True(add.Ok, add.Message);
                Assert.Equal("folder:gamma.url", add.GameId);
                Assert.NotNull(lib.Get("folder:gamma.url"));
                Assert.True(lib.AddFromFile(Path.Combine(games, "Beta.exe"), "").Message.Contains("já está"), "inside games folder");
                Assert.False(lib.AddFromFile(Path.Combine(outside, "missing.exe"), "").Ok);
                Assert.True(lib.AddSteamApp("10", "").Message.Contains("já está"), "duplicate app id");
                Assert.False(lib.AddSteamApp("x1", "").Ok);

                // Extra sources (Riot/Hydra importers) respect settings.Import<Name> and are ignorable.
                var riot = new FakeExtra { Name = "riot" };
                riot.Games.Add(new Game { Id = "riot:valorant", Name = "VALORANT", Source = "riot", Platform = "Riot", InstallDir = outside });
                riot.Games.Add(new Game { Id = "riot:dup", Name = "Dup", Source = "riot", SteamAppId = "10" });
                lib.AddExtraSource(riot);
                lib.Rescan();
                Assert.NotNull(lib.Get("riot:valorant"));
                Assert.True(lib.Get("riot:dup") == null, "duplicate of a folder game is dropped");
                Assert.Equal(outside, lib.RevealPath("riot:valorant"));
                OpResult hide = lib.Remove("riot:valorant");
                Assert.True(hide.Ok && lib.Get("riot:valorant") == null, "imported games are ignored, not deleted");
                Assert.True(Directory.Exists(outside));
                Assert.True(lib.Undo(hide.UndoToken).Ok);
                Assert.NotNull(lib.Get("riot:valorant"));
                settings.ImportRiot = false;
                lib.Rescan();
                Assert.True(lib.Get("riot:valorant") == null, "ImportRiot=false");

                Assert.Equal(Path.Combine(games, "Beta.exe"), lib.RevealPath("folder:beta.exe"));
                Assert.Equal("", lib.RevealPath("nope"));
            }
            finally
            {
                lib.Dispose();
                Directory.Delete(root, true);
            }
        }
    }
}
