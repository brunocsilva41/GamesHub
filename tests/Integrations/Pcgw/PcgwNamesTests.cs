using System.Collections.Generic;
using System.Linq;

namespace GamesHub.Tests
{
    public static class PcgwNamesTests
    {
        public static void TestNormalize()
        {
            Assert.Equal("tekken 7", PcgwNames.Normalize("TEKKEN™ 7"));
            Assert.Equal("hot wheels lets race ultimate speed", PcgwNames.Normalize("Hot Wheels Let's Race: Ultimate Speed"));
            Assert.Equal("pokemon and friends", PcgwNames.Normalize("Pokémon & Friends"));
            Assert.Equal("", PcgwNames.Normalize("  "));
            Assert.Equal("DIRT 5", PcgwNames.CleanForSearch("DIRT® 5 (DirectX 12)"));
        }

        public static void TestBestMatchExactFirst()
        {
            var titles = new[] { "Hot Wheels Unleashed", "Hot Wheels Unleashed 2: Turbocharged" };
            Assert.Equal("Hot Wheels Unleashed", PcgwNames.BestMatch("HOT WHEELS UNLEASHED™", titles));
            Assert.Equal("Hot Wheels Unleashed 2: Turbocharged", PcgwNames.BestMatch("Hot Wheels Unleashed 2", titles));
            Assert.Equal("DIRT 5", PcgwNames.BestMatch("Dirt 5", new[] { "DiRT 4", "DIRT 5" }));
        }

        public static void TestBestMatchIsConservative()
        {
            // ambiguous subtitle matches → none
            Assert.True(PcgwNames.BestMatch("Hot Wheels", new[] { "Hot Wheels: Beat That!", "Hot Wheels: Infinite Rush" }) == null);
            // one-word query never matches by prefix
            Assert.True(PcgwNames.BestMatch("Doom", new[] { "Doom: The Dark Ages" }) == null);
            // prefix without subtitle separator is not a match
            Assert.True(PcgwNames.BestMatch("Doom Eternal", new[] { "Doom Eternal Legacy Edition Something" }) == null);
            Assert.True(PcgwNames.BestMatch("League of Legends", new[] { "Legends of Runeterra" }) == null);
            Assert.True(PcgwNames.BestMatch("x", new string[0]) == null);
        }

        public static void TestBestMatchEditionSuffix()
        {
            Assert.Equal("The Witcher 3: Wild Hunt",
                PcgwNames.BestMatch("The Witcher 3: Wild Hunt - Game of the Year Edition", new[] { "The Witcher 3: Wild Hunt" }));
        }

        // Real snippets from data/manifest.yaml (ludusavi-manifest).
        private const string LethalCompany = @"Lethal Company:
  files:
    ""<home>/AppData/LocalLow/ZeekerssRBLX/Lethal Company"":
      tags:
        - save
      when:
        - os: windows
  id:
    lutris: lethal-company
  installDir:
    Lethal Company: {}
  launch:
    ""<base>/Lethal Company.exe"":
      - when:
          - bit: 64
            store: steam
  registry:
    HKEY_CURRENT_USER/Software/ZeekerssRBLX/Lethal Company:
      tags:
        - config
  steam:
    id: 1966720";

        private const string Dirt5 = @"DIRT 5:
  cloud:
    steam: true
  files:
    ""<root>/userdata/<storeUserId>/1038250/remote/user0/profile"":
      tags:
        - save
      when:
        - store: steam
    ""<winDocuments>/My Games/DIRT5/user0/profile"":
      tags:
        - config
      when:
        - os: windows
    ""<winLocalAppData>/Packages/CodemastersSoftwareCompan.DiRT5_4cfye3zbe1gaw/SystemAppData/wgs"":
      tags:
        - save
      when:
        - os: windows
          store: microsoft
    ""<xdgConfig>/dirt5"":
      tags:
        - save
    ""<base>/Saves/*.sav"": {}
  id:
    steamExtra:
      - 1234567
  steam:
    id: 1038250";

        public static void TestLudusaviEntryLethalCompany()
        {
            LudusaviEntry e = LudusaviManifest.ParseEntry(LethalCompany.Replace("\r", "").Split('\n'));
            Assert.Equal("Lethal Company", e.Title);
            Assert.Equal("1966720", e.SteamIds.Single());
            Assert.Equal(2, e.Rows.Count);
            PcgwRow save = e.Rows.Single(r => r.Kind == "save");
            Assert.Equal(@"{{p|userprofile}}\AppData\LocalLow\ZeekerssRBLX\Lethal Company", save.Raw);
            Assert.Equal(@"{{p|hkcu}}\Software\ZeekerssRBLX\Lethal Company", e.Rows.Single(r => r.Kind == "config").Raw);
        }

        public static void TestLudusaviEntryDirt5()
        {
            LudusaviEntry e = LudusaviManifest.ParseEntry(Dirt5.Replace("\r", "").Split('\n'));
            Assert.True(e.SteamIds.SequenceEqual(new[] { "1234567", "1038250" }));
            List<PcgwRow> rows = e.Rows;
            Assert.Equal(3, rows.Count, "microsoft store + xdg rows skipped");
            Assert.True(rows.Any(r => r.Platform == "Steam" && r.Kind == "save"
                                      && r.Raw == @"{{p|steam}}\userdata\{{p|uid}}\1038250\remote\user0\profile"));
            Assert.True(rows.Any(r => r.Kind == "config" && r.Raw == @"{{p|userprofile\Documents}}\My Games\DIRT5\user0\profile"));
            Assert.True(rows.Any(r => r.Kind == "save" && r.Raw == @"{{p|game}}\Saves\*.sav"), "untagged → save");
        }

        public static void TestLudusaviKeys()
        {
            Assert.Equal("\"! That Bastard\" Is", LudusaviManifest.UnquoteKey("\"\\\"! That Bastard\\\" Is\":"));
            Assert.Equal("It's", LudusaviManifest.UnquoteKey("'It''s':"));
            Assert.Equal("Anyway", LudusaviManifest.UnquoteKey("    Anyway: {}"));
            Assert.True(LudusaviManifest.PathToRaw("<root>/foo", false) == "{{p|root}}\\foo");
            Assert.True(LudusaviManifest.PathToRaw("<unknown>/foo", false) == null);
        }
    }
}
