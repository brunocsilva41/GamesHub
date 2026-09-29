using System.Collections.Generic;

namespace GamesHub.Tests
{
    public static class SteamGridDbMatchTests
    {
        private static MatchCandidate C(string id, string name) => new MatchCandidate(id, name);

        public static void TestNewestExactNameWins()
        {
            // Real SteamGridDB results for "PointBlank": the 1993 arcade game must lose to the 2008 PC FPS.
            var list = new List<MatchCandidate> { C("5305337", "Point Blank"), C("5305338", "Point Blank 2"), C("5445957", "Point Blank") };
            var released = new Dictionary<string, long> { ["5305337"] = 725846400, ["5305338"] = 883612800, ["5445957"] = 1199145600 };
            Assert.Equal("5445957", SteamGridDbClient.PreferNewestExact("PointBlank", list, released).AppId);
        }

        public static void TestSingleExactNameDefersToMatcher()
        {
            var list = new List<MatchCandidate> { C("1", "League of Legends"), C("2", "League of Legends: Wild Rift") };
            Assert.True(SteamGridDbClient.PreferNewestExact("League of Legends", list, new Dictionary<string, long>()) == null);
        }

        public static void TestSearchNamesDropsClientSuffix()
        {
            var names = new List<string>(SteamGridDbClient.SearchNames("Roblox Player"));
            Assert.Equal(2, names.Count);
            Assert.Equal("Roblox", names[1]);
        }
    }
}
