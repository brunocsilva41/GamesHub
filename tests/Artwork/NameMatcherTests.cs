using System.Collections.Generic;
using System.Linq;

namespace GamesHub.Tests
{
    public static class NameMatcherTests
    {
        private static List<MatchCandidate> C(params string[] idAndNames)
        {
            var l = new List<MatchCandidate>();
            for (int i = 0; i < idAndNames.Length; i += 2) l.Add(new MatchCandidate(idAndNames[i], idAndNames[i + 1]));
            return l;
        }

        private static string Match(string name, List<MatchCandidate> candidates)
            => NameMatcher.PickBest(name, candidates)?.AppId ?? "none";

        public static void TestNormalizeRemovesNoise()
        {
            Assert.Equal("lego marvel super heroes 2", NameMatcher.Normalize("LEGO Marvel Super Heroes 2 DirectX 11"));
            Assert.Equal("lego marvel super heroes 2", NameMatcher.Normalize("LEGO® Marvel Super Heroes 2"));
            Assert.Equal("dirt 5", NameMatcher.Normalize("DIRT 5 (Install Crack)"));
            Assert.Equal("pointblank", NameMatcher.Normalize("PointBlank.exe - Atalho"));
            Assert.Equal("hot wheels unleashed 2", NameMatcher.Normalize("Hot Wheels Unleashed 2.exe"));
            Assert.Equal("tom clancys rainbow six siege x", NameMatcher.Normalize("Tom Clancy's Rainbow Six® Siege X"));
            Assert.Equal("the witcher 3 wild hunt", NameMatcher.Normalize("The Witcher 3: Wild Hunt - Game of the Year Edition"));
            Assert.Equal("skyrim", NameMatcher.Normalize("Skyrim Special Edition"));
            Assert.Equal("fallout 4", NameMatcher.Normalize("Fallout 4 GOTY"));
            Assert.Equal("pokemon", NameMatcher.Normalize("Pokémon"));
            Assert.Equal("raft", NameMatcher.Normalize("Play Raft"));
            Assert.Equal("some game", NameMatcher.Normalize("Some Game dx12"));
            Assert.Equal("borderlands 3", NameMatcher.Normalize("Borderlands 3 (DX12) x64"));
            Assert.Equal("rock and roll", NameMatcher.Normalize("Rock & Roll"));
            Assert.Equal("", NameMatcher.Normalize(null));
        }

        public static void TestRomanNumerals()
        {
            Assert.Equal(3, NameMatcher.RomanToInt("iii"));
            Assert.Equal(4, NameMatcher.RomanToInt("IV"));
            Assert.Equal(6, NameMatcher.RomanToInt("vi"));
            Assert.Equal(14, NameMatcher.RomanToInt("xiv"));
            Assert.Equal(0, NameMatcher.RomanToInt("mix"));
            Assert.Equal(0, NameMatcher.RomanToInt("iiii"));
            Assert.Equal(0, NameMatcher.RomanToInt(""));
            Assert.Equal("III", NameMatcher.ToRoman(3));
            Assert.Equal("XIV", NameMatcher.ToRoman(14));
            Assert.Equal(NameMatcher.Normalize("Call of Duty - Black Ops 3"), NameMatcher.Normalize("Call of Duty®: Black Ops III"));
            Assert.Equal("final fantasy 7", NameMatcher.Normalize("Final Fantasy VII"));
            Assert.Equal("mega man x", NameMatcher.Normalize("Mega Man X"), "lone X stays a letter");
            Assert.Equal("i am bread", NameMatcher.Normalize("I Am Bread"), "lone I stays a word");
        }

        public static void TestSearchQueries()
        {
            Assert.Equal("call of duty black ops 3", NameMatcher.SearchQuery("Call of Duty - Black Ops 3"));
            Assert.Equal("call of duty black ops iii", NameMatcher.RomanQuery("Call of Duty - Black Ops 3"));
            Assert.Equal("", NameMatcher.RomanQuery("Lethal Company"));
            Assert.Equal("tom clancys rainbow six siege", NameMatcher.ShortQuery("Tom Clancy's Rainbow Six® Siege X"));
            Assert.Equal("", NameMatcher.ShortQuery("Lethal Company"));
        }

        public static void TestScoreRules()
        {
            Assert.Equal(1.0, NameMatcher.Score("point blank", "pointblank"), "spacing ignored");
            Assert.Equal(0.0, NameMatcher.Score("hot wheels unleashed 2", "hot wheels unleashed"), "sequel number must match");
            Assert.Equal(0.0, NameMatcher.Score("dirt 5", "dirt 4"));
            double subtitle = NameMatcher.Score("hot wheels unleashed 2", NameMatcher.Normalize("HOT WHEELS UNLEASHED™ 2 - Turbocharged"));
            Assert.True(subtitle >= NameMatcher.Threshold, "one-word subtitle accepted: " + subtitle);
            double story = NameMatcher.Score("league of legends", NameMatcher.Normalize("CONVERGENCE: A League of Legends Story™"));
            Assert.True(story < NameMatcher.Threshold, "spin-off rejected: " + story);
            Assert.True(NameMatcher.Score("mimesis", "mimesis something") < NameMatcher.Threshold, "single word needs exact");
        }

        public static void TestRealLibraryMatches()
        {
            Assert.Equal("647830", Match("LEGO Marvel Super Heroes 2 DirectX 11", C(
                "647830", "LEGO® Marvel Super Heroes 2", "731170", "LEGO® Marvel Super Heroes 2 - Runaways",
                "731180", "LEGO® Marvel Super Heroes 2 - Season Pass", "249130", "LEGO® MARVEL Super Heroes")));
            Assert.Equal("2051120", Match("Hot Wheels Unleashed 2", C(
                "2051120", "HOT WHEELS UNLEASHED™ 2 - Turbocharged", "2640210", "HOT WHEELS UNLEASHED™ 2 - Manga Free Pack",
                "1271700", "HOT WHEELS UNLEASHED™")));
            Assert.Equal("311210", Match("Call of Duty - Black Ops 3", C(
                "311210", "Call of Duty®: Black Ops III", "311211", "Call of Duty®: Black Ops III - Zombies Chronicles",
                "202970", "Call of Duty®: Black Ops II")));
            Assert.Equal("2567870", Match("Chained Together", C(
                "2567870", "Chained Together", "3000000", "Backrooms Chained Together", "3000001", "Chained Together Supporter Pack")));
            Assert.Equal("3097560", Match("Liars Bar", C("3097560", "Liar's Bar")));
            Assert.Equal("2827200", Match("MIMESIS", C("2827200", "MIMESIS")));
            Assert.Equal("1966720", Match("Lethal Company", C("1966720", "Lethal Company")));
            Assert.Equal("291550", Match("Brawlhalla", C(
                "291550", "Brawlhalla", "999001", "Brawlhalla - All Legends (Current and Future)", "999002", "Brawlhalla Soundtrack")));
            Assert.Equal("389730", Match("TEKKEN 7", C("389730", "TEKKEN 7", "999003", "TEKKEN 7 - DLC9: Negan")));
            Assert.Equal("1038250", Match("DIRT 5 (Install Crack)", C("1038250", "DIRT 5", "999004", "DIRT 5 - Year 1 Upgrade", "421020", "DiRT 4")));
            Assert.Equal("1221330", Match("PointBlank.exe - Atalho", C("1221330", "Pointblank")));
            Assert.Equal("359550", Match("Tom Clancy's Rainbow Six® Siege X", C(
                "359550", "Tom Clancy's Rainbow Six Siege", "1182510", "Tom Clancy's Rainbow Six® Siege - Y1 Operators")));
            Assert.Equal("648800", Match("Play Raft", C("648800", "Raft", "1955340", "Super Raft Boat Together")));
        }

        public static void TestNoFalseMatches()
        {
            Assert.Equal("none", Match("League of Legends", C(
                "1", "CONVERGENCE: A League of Legends Story™", "2", "Ruined King: A League of Legends Story™",
                "3", "The Mageseeker: A League of Legends Story™", "4", "Bandle Tale: A League of Legends Story")));
            Assert.Equal("none", Match("Hot Wheels Unleashed 2", C("1271700", "HOT WHEELS UNLEASHED™")));
            Assert.Equal("none", Match("Roblox Player", C("5", "Roblox Simulator")));
            Assert.Equal("none", Match("2XKO", new List<MatchCandidate>()));
            Assert.Equal("none", Match("Counter-Strike 2", C("9", "Counter-Strike 2 Soundtrack")), "soundtrack excluded");
        }

        public static void TestTieRules()
        {
            Assert.Equal("10", Match("Doom", C("10", "DOOM", "11", "Doom")), "exact duplicates: best-ranked wins");
            Assert.Equal("none", Match("Alpha Beta", C("20", "Alpha Beta Gamma", "21", "Alpha Beta Delta")), "ambiguous tie rejected");
            Assert.Equal("30", Match("Alpha Beta", C("31", "Alpha Beta Gamma", "30", "Alpha Beta")), "exact beats subtitle");
        }

        public static void TestNonGames()
        {
            foreach (string n in new[] { "Steam", "Epic Games Launcher", "Roblox Studio", "CB Servers Launcher",
                                         "SKlauncher", "Hydra", "plutonium", "Setup", "Uninstall Foo", "unins000" })
                Assert.True(NameMatcher.IsLikelyNonGame(n), n);
            Assert.True(NameMatcher.IsNonSteamPlatform("Riot"), "Riot platform skips Steam matching");
            // Games on non-Steam platforms are still games: they get SteamGridDB art.
            Assert.False(NameMatcher.IsLikelyNonGame("VALORANT"), "VALORANT");
            foreach (string n in new[] { "Lethal Company", "TEKKEN 7", "League of Legends", "Roblox Player", "DIRT 5 (Install Crack)", "LEGO Marvel Super Heroes 2 DirectX 11" })
                Assert.False(NameMatcher.IsLikelyNonGame(n), n);
        }
    }
}
