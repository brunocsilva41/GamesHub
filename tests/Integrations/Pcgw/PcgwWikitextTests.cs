// OWNER: PCGW agent. Wikitext row extraction + API JSON parsing (fixtures modeled on real PCGW pages).
using System.Collections.Generic;
using System.Linq;

namespace GamesHub.Tests
{
    public static class PcgwWikitextTests
    {
        // Modeled on the TEKKEN 7 / Lethal Company / DIRT 5 pages: multiple rows and paths, refs with nested
        // templates (pipes inside), comments, non-Windows platforms, registry config.
        public const string Fixture = @"
==Game data==
===Configuration file(s) location===
{{Game data|
{{Game data/config|Windows|{{p|localappdata}}\TekkenGame\Saved\Config\WindowsNoEditor\<ref>{{Refcheck|user=Garrett|date=2020-05-04|comment=Checked on v4.20}}</ref>}}
{{Game data/config|Windows|{{p|hkcu}}\Software\ZeekerssRBLX\Lethal Company\}}
{{Game data/config|Steam Play (Linux)|{{p|linuxsteamprefix}}/drive_c/users/steamuser/AppData/Local/TekkenGame/Saved/Config/}}
{{Game data/config|Microsoft Store|}}
}}

===Save game data location===
{{Game data|
{{Game data/saves|Windows|{{p|localappdata}}\TekkenGame\Saved\SaveGames\|{{p|userprofile}}\AppData\LocalLow\ZeekerssRBLX\Lethal Company\<ref name=""ll""/>}}
<!-- {{Game data/saves|Windows|{{p|game}}\OldSaves\}} -->
{{Game data/saves|Windows|{{p|userprofile\Documents}}\My Games\DIRT5\user0\profile\<br>{{p|game}}\save\*.sav}}
{{game data/saves|Steam|{{p|steam}}\userdata\{{p|uid}}\389730\remote\}}
{{Game data/saves|OS X|{{p|osxhome}}/Library/Application Support/Tekken/}}
{{Game data/saves|Windows|}}
{{Game data/saves|Windows|See [[#Cloud]] notes}}
}}
";

        public static void TestExtractsWindowsAndSteamRowsOnly()
        {
            List<PcgwRow> rows = PcgwWikitext.ExtractRows(Fixture);
            Assert.Equal(2, rows.Count(r => r.Kind == "config"), "config rows");
            Assert.Equal(5, rows.Count(r => r.Kind == "save"), "save rows");
            Assert.True(rows.All(r => r.Platform == "Windows" || r.Platform == "Steam"));
            Assert.Equal(1, rows.Count(r => r.Platform == "Steam"), "steam rows");
        }

        public static void TestStripsRefsAndComments()
        {
            List<PcgwRow> rows = PcgwWikitext.ExtractRows(Fixture);
            Assert.Equal(@"{{p|localappdata}}\TekkenGame\Saved\Config\WindowsNoEditor\", rows[0].Raw);
            Assert.True(rows.Any(r => r.Raw == @"{{p|userprofile}}\AppData\LocalLow\ZeekerssRBLX\Lethal Company\"), "self-closing ref removed");
            Assert.False(rows.Any(r => r.Raw.Contains("OldSaves")), "commented row ignored");
            Assert.False(rows.Any(r => r.Raw.Contains("<ref") || r.Raw.Contains("Refcheck")));
        }

        public static void TestMultiplePathsPerRowAndBr()
        {
            List<PcgwRow> saves = PcgwWikitext.ExtractRows(Fixture).Where(r => r.Kind == "save").ToList();
            Assert.Equal(@"{{p|localappdata}}\TekkenGame\Saved\SaveGames\", saves[0].Raw);
            Assert.True(saves.Any(r => r.Raw == @"{{p|userprofile\Documents}}\My Games\DIRT5\user0\profile\"));
            Assert.True(saves.Any(r => r.Raw == @"{{p|game}}\save\*.sav"));
            Assert.True(saves.Any(r => r.Raw == @"{{p|steam}}\userdata\{{p|uid}}\389730\remote\" && r.Platform == "Steam"));
        }

        public static void TestUnterminatedTemplateDoesNotThrow()
        {
            List<PcgwRow> rows = PcgwWikitext.ExtractRows(@"{{Game data/saves|Windows|{{p|appdata}}\Foo\");
            Assert.Equal(0, rows.Count);
            Assert.Equal(0, PcgwWikitext.ExtractRows(null).Count);
        }

        public static void TestSplitTopLevelKeepsNestedPipes()
        {
            List<string> parts = PcgwWikitext.SplitTopLevel(@"Game data/saves|Windows|{{p|appdata}}\A {{note|x|y}}|[[Link|text]]");
            Assert.Equal(4, parts.Count);
            Assert.Equal(@"{{p|appdata}}\A {{note|x|y}}", parts[2]);
        }

        public static void TestParseCargoResponse()
        {
            string json = "{\"cargoquery\":[{\"title\":{\"Page\":\"Lethal Company\",\"Steam AppID\":\"1966720\"}}]}";
            Assert.Equal("Lethal Company", PcgwApi.ParseCargoPages(json).Single());
            Assert.Equal(0, PcgwApi.ParseCargoPages("{\"cargoquery\":[]}").Count);
            Assert.Equal(0, PcgwApi.ParseCargoPages("{\"error\":{\"code\":\"x\"}}").Count);
        }

        public static void TestParseOpenSearchAndWikitextResponses()
        {
            string os = "[\"dirt 5\",[\"DIRT 5\",\"DiRT 4\"],[\"\",\"\"],[\"https://www.pcgamingwiki.com/wiki/DIRT_5\",\"x\"]]";
            Assert.Equal("DIRT 5", PcgwApi.ParseOpenSearch(os)[0]);
            string parse = "{\"parse\":{\"title\":\"Tekken 7\",\"pageid\":1,\"wikitext\":{\"*\":\"{{Game data/saves|Windows|{{p|appdata}}\\\\X}}\"}}}";
            var t = PcgwApi.ParseWikitext(parse);
            Assert.Equal("Tekken 7", t.Item1);
            Assert.Equal(@"{{p|appdata}}\X", PcgwWikitext.ExtractRows(t.Item2).Single().Raw);
            Assert.True(PcgwApi.ParseWikitext("{\"error\":{\"code\":\"missingtitle\"}}") == null);
        }

        public static void TestUrls()
        {
            Assert.Equal("https://www.pcgamingwiki.com/wiki/Hot_Wheels_Unleashed_2:_Turbocharged",
                PcgwApi.WikiUrl("Hot Wheels Unleashed 2: Turbocharged"));
            var svc = new PcgwService(new AppSettings());
            Assert.Equal("https://www.pcgamingwiki.com/api/appid.php?appid=730", svc.PageUrl(new Game { Name = "CS2" }, "730"));
            Assert.Equal("https://www.pcgamingwiki.com/api/appid.php?appid=10", svc.PageUrl(new Game { SteamAppId = "10" }, ""));
            Assert.Equal("https://www.pcgamingwiki.com/w/index.php?search=DIRT%205",
                svc.PageUrl(new Game { Name = "DIRT® 5 (DirectX 12)" }, null));
        }

        public static void TestDisabledReturnsNotFound()
        {
            var svc = new PcgwService(new AppSettings { PcgwEnabled = false });
            PcgwInfo info = svc.GetInfoAsync(new Game { Name = "DIRT 5" }, "1038250").Result;
            Assert.False(info.Found);
        }
    }
}
