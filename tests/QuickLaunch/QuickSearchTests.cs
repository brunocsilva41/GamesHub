using System;
using System.Collections.Generic;
using System.Linq;

namespace GamesHub.Tests
{
    public static class QuickSearchTests
    {
        private static readonly DateTime Now = new DateTime(2026, 9, 29, 12, 0, 0);

        private static Game G(string name, string platform = "PC", DateTime? last = null, long play = 0,
                              bool fav = false, bool hidden = false, bool running = false, params string[] cols)
            => new Game
            {
                Id = "folder:" + name.ToLowerInvariant(), Name = name, Platform = platform, LastPlayed = last,
                PlaySeconds = play, Favorite = fav, Hidden = hidden, Running = running, Collections = cols.ToList(),
            };

        private static List<QuickEntry> Lib(params Game[] games) => QuickSearch.BuildIndex(games);

        private static string Top(List<QuickEntry> idx, string q) => QuickSearch.Search(idx, q, Now).FirstOrDefault()?.Game.Name;

        private static List<string> Names(List<QuickEntry> idx, string q) => QuickSearch.Search(idx, q, Now).Select(h => h.Game.Name).ToList();

        public static void TestFoldStripsAccentsAndKeepsLength()
        {
            Assert.Equal("pokemon  edicao sao joao", QuickSearch.Fold("Pokémon: Edição São João"));
            Assert.Equal("Pokémon: Edição".Length, QuickSearch.Fold("Pokémon: Edição").Length);
            Assert.Equal("strase", QuickSearch.Fold("Straße"));
            Assert.Equal("a b", QuickSearch.FoldQuery("  Á -- b "));
        }

        public static void TestAccentInsensitiveBothWays()
        {
            var idx = Lib(G("Pokémon Legends"), G("Portal"));
            Assert.Equal("Pokémon Legends", Top(idx, "pokemon"));
            Assert.Equal("Pokémon Legends", Top(idx, "POKÉ"));
        }

        public static void TestInitialsMatch()
        {
            var idx = Lib(G("LEGO Marvel Super Heroes"), G("Limbo"), G("Lemmings"));
            Assert.Equal("LEGO Marvel Super Heroes", Top(idx, "lmsh"));
            Assert.Equal("LEGO Marvel Super Heroes", Top(idx, "lms"));
        }

        public static void TestInitialsWithRomanNumeralsAndDigits()
        {
            var idx = Lib(G("Grand Theft Auto V"), G("Counter-Strike 2"), G("Gears Tactics"));
            Assert.Equal("Grand Theft Auto V", Top(idx, "gtav"));
            Assert.Equal("Grand Theft Auto V", Top(idx, "gta5"));
            Assert.Equal("Grand Theft Auto V", Top(idx, "gta 5"));
            Assert.Equal("Counter-Strike 2", Top(idx, "cs2"));
        }

        public static void TestExactBeatsPrefixBeatsSubstring()
        {
            var idx = Lib(G("Portal 2"), G("Portal"), G("Supportal Deluxe"));
            List<string> r = Names(idx, "portal");
            Assert.Equal("Portal", r[0]);
            Assert.Equal("Portal 2", r[1]);
            Assert.Equal("Supportal Deluxe", r[2]);
        }

        public static void TestCompactPrefixIgnoresPunctuation()
        {
            var idx = Lib(G("Half-Life 2"), G("Hades"));
            Assert.Equal("Half-Life 2", Top(idx, "halflife"));
        }

        public static void TestTokenWordPrefixesAnyOrder()
        {
            var idx = Lib(G("The Witcher 3: Wild Hunt"), G("Hunt: Showdown"));
            Assert.Equal("The Witcher 3: Wild Hunt", Top(idx, "witch hun"));
            Assert.Equal("The Witcher 3: Wild Hunt", Top(idx, "wild witcher"));
        }

        public static void TestPlatformAndCollectionTokens()
        {
            var idx = Lib(G("Fortnite", "Epic"), G("Fall Guys", "Steam", cols: "Com amigos"), G("Forza Horizon 5", "Steam"));
            Assert.Equal("Fortnite", Top(idx, "for epic"));
            Assert.Equal("Fall Guys", Top(idx, "amigos"));
            Assert.True(Names(idx, "steam").Count == 2, "platform-only query lists that platform's games");
        }

        public static void TestSubsequenceFallbackAndNoMatch()
        {
            var idx = Lib(G("Hollow Knight"), G("Celeste"));
            Assert.Equal("Hollow Knight", Top(idx, "hlwkn"));
            Assert.Equal(0, QuickSearch.Search(idx, "zzzq", Now).Count);
        }

        public static void TestRecentAndPlaytimeBoostWithinTier()
        {
            var idx = Lib(G("Doom Eternal"), G("Doom 64", last: Now.AddDays(-1), play: 20 * 3600));
            Assert.Equal("Doom 64", Top(idx, "doom"));
            // ...but a boost never lifts a weak match over a much better one.
            var idx2 = Lib(G("Dota 2"), G("Don't Starve Together And More", last: Now.AddHours(-1), play: 900 * 3600, fav: true));
            Assert.Equal("Dota 2", Top(idx2, "dota"));
        }

        public static void TestEmptyQueryReturnsRecentWithRunningFirstAndNoHidden()
        {
            var idx = Lib(G("Old", last: Now.AddDays(-30)), G("New", last: Now.AddDays(-1)),
                          G("Running", running: true), G("Secret", hidden: true, last: Now));
            List<string> r = Names(idx, "   ");
            Assert.Equal("Running", r[0]);
            Assert.Equal("New", r[1]);
            Assert.Equal("Old", r[2]);
            Assert.False(r.Contains("Secret"), "hidden excluded from recents");
        }

        public static void TestHiddenGamesRankLower()
        {
            var idx = Lib(G("Minecraft", hidden: true), G("Minecraft Dungeons"));
            Assert.Equal("Minecraft Dungeons", Top(idx, "minecraft"));
            Assert.Equal(2, Names(idx, "minecraft").Count);
        }

        public static void TestHighlightsMapToOriginalName()
        {
            var idx = Lib(G("Half-Life 2"));
            QuickSearchHit h = QuickSearch.Search(idx, "halflife", Now)[0];
            // "Half" (0,4) and "Life" (5,4) — the hyphen is not highlighted.
            Assert.Equal(2, h.Highlights.Count);
            Assert.Equal(0, h.Highlights[0][0]); Assert.Equal(4, h.Highlights[0][1]);
            Assert.Equal(5, h.Highlights[1][0]); Assert.Equal(4, h.Highlights[1][1]);

            var idx2 = Lib(G("LEGO Marvel Super Heroes"));
            QuickSearchHit h2 = QuickSearch.Search(idx2, "lmsh", Now)[0];
            Assert.Equal(4, h2.Highlights.Count);
            Assert.Equal(5, h2.Highlights[1][0]);
        }

        public static void TestMergeRanges()
        {
            var m = QuickSearch.Merge(new List<int[]> { new[] { 5, 2 }, new[] { 0, 2 }, new[] { 2, 1 }, new[] { 6, 3 } });
            Assert.Equal(2, m.Count);
            Assert.Equal(0, m[0][0]); Assert.Equal(3, m[0][1]);
            Assert.Equal(5, m[1][0]); Assert.Equal(4, m[1][1]);
        }

        public static void TestEmptyNamesAndNullsAreSafe()
        {
            var idx = QuickSearch.BuildIndex(new[] { new Game { Name = "" }, null, new Game { Name = "!!!", Collections = null } });
            Assert.Equal(0, QuickSearch.Search(idx, "a", Now).Count);
            Assert.Equal(2, QuickSearch.Search(idx, "", Now).Count);
        }

        public static void TestArtUrlEscapesSegments()
        {
            Assert.Equal(null, QuickDto.ArtUrl(""));
            string u = QuickDto.ArtUrl("folder-meu jogo/header.jpg");
            Assert.True(u.StartsWith("https://art.gameshub.example/folder-meu%20jogo/header.jpg"), u);
        }

        public static void TestArtUrlNeverStatsOutsideArtCache()
        {
            System.IO.Directory.CreateDirectory(AppPaths.ArtDir);
            string outside = System.IO.Path.Combine(AppPaths.DataDir, "quick-escape-probe.txt");
            string inside = System.IO.Path.Combine(AppPaths.ArtDir, "quick-probe.jpg");
            System.IO.File.WriteAllText(outside, "x");
            System.IO.File.WriteAllText(inside, "x");
            try
            {
                Assert.True(QuickDto.ArtUrl("quick-probe.jpg").Contains("?v="), "file inside the art cache gets a stamp");
                string u = QuickDto.ArtUrl("../quick-escape-probe.txt");
                Assert.False(u.Contains("?v="), "'..' must not reach files outside the art cache: " + u);
                Assert.False(QuickDto.ArtUrl(outside).Contains("?v="), "rooted paths must not be stat'ed");
            }
            finally
            {
                System.IO.File.Delete(outside);
                System.IO.File.Delete(inside);
            }
        }
            public static void TestPerformanceTwoThousandGames()
        {
            var games = Enumerable.Range(0, 2000).Select(i => G("Jogo Número " + i + " Édition Spéciale", i % 2 == 0 ? "Steam" : "PC",
                                                                  last: i % 7 == 0 ? Now.AddDays(-i % 90) : (DateTime?)null, play: i * 97)).ToArray();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var idx = Lib(games);
            long build = sw.ElapsedMilliseconds;
            sw.Restart();
            foreach (string q in new[] { "j", "jn1", "numero 19", "edition", "spc", "zzz", "" }) QuickSearch.Search(idx, q, Now);
            long search = sw.ElapsedMilliseconds;
            Assert.True(build < 500, "index build too slow: " + build + "ms");
            Assert.True(search < 350, "7 searches too slow: " + search + "ms");
        }
    }
}
