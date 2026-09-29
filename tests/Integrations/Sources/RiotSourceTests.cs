using System;
using System.Collections.Generic;
using System.IO;

namespace GamesHub.Tests
{
    public static class RiotSourceTests
    {
        private const string LolYaml =
            "auto_patching_enabled_by_player: false\n" +
            "dependencies:\n" +
            "    Direct X 9:\n" +
            "        hash: \"22a2\"\n" +
            "    vanguard: true\n" +
            "locale_data:\n" +
            "    available_locales:\n" +
            "    - \"ar_AE\"\n" +
            "    default_locale: \"pt_BR\"\n" +
            "patching_policy: \"manual\"\n" +
            "product_install_full_path: \"F:/Riot Games/League of Legends\"\r\n" +
            "product_install_root: 'F:/Riot Games/'\n" +
            "settings:\n" +
            "    create_shortcut: false\n" +
            "shortcut_name: \"League of Legends.lnk\" # trailing comment\n" +
            "should_repair: false # plain comment\n" +
            "empty_map: {}\n";

        public static void TestYamlReadsTopLevelScalars()
        {
            var y = FlatYaml.Parse(LolYaml);
            Assert.Equal("F:/Riot Games/League of Legends", y["product_install_full_path"]);
            Assert.Equal("F:/Riot Games/", y["product_install_root"]);
            Assert.Equal("League of Legends.lnk", y["shortcut_name"]);
            Assert.Equal("false", y["should_repair"]);
            Assert.Equal("manual", y["patching_policy"]);
        }

        public static void TestYamlIgnoresNestedAndEmpty()
        {
            var y = FlatYaml.Parse(LolYaml);
            Assert.False(y.ContainsKey("dependencies"), "nested map key");
            Assert.False(y.ContainsKey("hash"), "nested scalar");
            Assert.False(y.ContainsKey("default_locale"), "nested scalar 2");
            Assert.False(y.ContainsKey("create_shortcut"), "nested scalar 3");
            Assert.False(y.ContainsKey("empty_map"), "empty map");
            Assert.Equal(0, FlatYaml.Parse(null).Count);
            Assert.Equal(0, FlatYaml.Parse("garbage without key\n- item\n  indented: x\n").Count);
        }

        public static void TestYamlQuotesAndEscapes()
        {
            var y = FlatYaml.Parse("a: \"C:\\\\Games\\\\x \\\"y\\\"\"\nb: 'it''s'\n\"c d\": v: w\nurl: http://x/y\n");
            Assert.Equal("C:\\Games\\x \"y\"", y["a"]);
            Assert.Equal("it's", y["b"]);
            Assert.Equal("v: w", y["c d"]);
            Assert.Equal("http://x/y", y["url"]);
        }

        public static void TestProductDirSplit()
        {
            Assert.True(RiotCatalog.TrySplitProductDir("league_of_legends.live", out string p, out string l));
            Assert.Equal("league_of_legends", p);
            Assert.Equal("live", l);
            Assert.True(RiotCatalog.TrySplitProductDir("lion.live", out p, out l));
            Assert.Equal("lion", p);
            Assert.False(RiotCatalog.TrySplitProductDir("league_of_legends.live.game_patch", out p, out l), "sub-patch");
            Assert.False(RiotCatalog.TrySplitProductDir("Riot Client", out p, out l), "client dir");
            Assert.False(RiotCatalog.TrySplitProductDir("", out p, out l));
        }

        public static void TestProductNames()
        {
            Assert.Equal("League of Legends", RiotCatalog.DisplayName("league_of_legends", "live"));
            Assert.Equal("VALORANT", RiotCatalog.DisplayName("valorant", "live"));
            Assert.Equal("Legends of Runeterra", RiotCatalog.DisplayName("bacon", "live"));
            Assert.Equal("2XKO", RiotCatalog.DisplayName("lion", "live", "whatever.lnk"));
            Assert.Equal("League of Legends (PBE)", RiotCatalog.DisplayName("league_of_legends", "pbe"));
            Assert.Equal("Cool Game", RiotCatalog.DisplayName("new_thing", "live", "Cool Game.lnk"));
            Assert.Equal("New Thing", RiotCatalog.DisplayName("new_thing", "live"));
        }

        public static void TestIdsAndLaunchArgs()
        {
            Assert.Equal("riot:league_of_legends", RiotCatalog.GameId("league_of_legends", "live"));
            Assert.Equal("riot:league_of_legends.pbe", RiotCatalog.GameId("league_of_legends", "pbe"));
            Assert.Equal("--launch-product=lion --launch-patchline=live", RiotCatalog.LaunchArgs("lion", "live"));
            Assert.Equal(@"F:\Riot Games\2XKO\Live", RiotCatalog.NormalizePath("F:/Riot Games/2XKO/Live/"));
            Assert.Equal(@"C:\", RiotCatalog.NormalizePath("C:/"));
            Assert.Equal(@"X:\LoL\Game\League of Legends.exe", RiotCatalog.ExeCandidates("league_of_legends", @"X:\LoL")[0]);
            Assert.Equal(0, RiotCatalog.ExeCandidates("unknown", @"X:\Foo").Count);
        }

        public static void TestClientPathsOrder()
        {
            var d = Json.DeserializeObject(
                "{\"associated_client\":{\"F:/Riot Games/2XKO/Live/\":\"F:/RC/RiotClientServices.exe\"}," +
                "\"patchlines\":{\"K\":\"G:/Other/RiotClientServices.exe\"}," +
                "\"rc_default\":\"F:/RC/RiotClientServices.exe\",\"rc_live\":\"E:/Live/RiotClientServices.exe\"}")
                as IDictionary<string, object>;
            var list = RiotSource.ParseClientPaths(d);
            Assert.Equal(3, list.Count);
            Assert.Equal(@"E:\Live\RiotClientServices.exe", list[0]);
            Assert.Equal(@"F:\RC\RiotClientServices.exe", list[1]);
            Assert.Equal(@"G:\Other\RiotClientServices.exe", list[2]);
            Assert.Equal(0, RiotSource.ParseClientPaths(null).Count);
        }

        public static void TestScanFixture()
        {
            string root = Path.Combine(Path.GetTempPath(), "gameshub-test-riot-" + Guid.NewGuid().ToString("N"));
            try
            {
                string client = Path.Combine(root, "Client", "RiotClientServices.exe");
                string lol = Path.Combine(root, "Games", "League of Legends");
                string lion = Path.Combine(root, "Games", "2XKO", "Live");
                Directory.CreateDirectory(Path.GetDirectoryName(client));
                File.WriteAllText(client, "");
                Directory.CreateDirectory(Path.Combine(lol, "Game"));
                File.WriteAllText(Path.Combine(lol, "Game", "League of Legends.exe"), "");
                Directory.CreateDirectory(lion);                     // installed, but game exe missing
                string data = Path.Combine(root, "ProgramData");
                Directory.CreateDirectory(data);
                File.WriteAllText(Path.Combine(data, "RiotClientInstalls.json"),
                    "{\"rc_default\":\"" + client.Replace('\\', '/') + "\",\"rc_live\":\"Z:/missing/RiotClientServices.exe\"}");
                void Product(string dir, string installPath)
                {
                    string d = Path.Combine(data, "Metadata", dir);
                    Directory.CreateDirectory(d);
                    File.WriteAllText(Path.Combine(d, dir + ".product_settings.yaml"),
                        "product_install_full_path: \"" + installPath.Replace('\\', '/') + "/\"\nshortcut_name: \"x.lnk\"\n");
                }
                Product("league_of_legends.live", lol);
                Product("lion.live", lion);
                Product("valorant.live", Path.Combine(root, "Games", "VALORANT", "live"));   // not on disk
                Directory.CreateDirectory(Path.Combine(data, "Metadata", "bacon.live"));      // no yaml
                Directory.CreateDirectory(Path.Combine(data, "Metadata", "Riot Client"));

                var games = new RiotSource(data).Scan();
                games.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
                Assert.Equal(2, games.Count, "installed products");
                Game g = games[0];
                Assert.Equal("riot:league_of_legends", g.Id);
                Assert.Equal("League of Legends", g.Name);
                Assert.Equal("riot", g.Source);
                Assert.Equal("Riot", g.Platform);
                Assert.Equal(client, g.LaunchTarget, "falls back to rc_default when rc_live is missing");
                Assert.Equal("--launch-product=league_of_legends --launch-patchline=live", g.LaunchArgs);
                Assert.Equal(lol, g.InstallDir);
                Assert.Equal(Path.Combine(lol, "Game", "League of Legends.exe"), g.Exe);
                Assert.Equal("riot:lion", games[1].Id);
                Assert.Equal("2XKO", games[1].Name);
                Assert.Equal("", games[1].Exe, "missing exe stays empty");
                Assert.Equal(0, new RiotSource(Path.Combine(root, "nope")).Scan().Count);
            }
            finally
            {
                try { Directory.Delete(root, true); } catch (Exception ex) { Console.WriteLine("cleanup: " + ex.Message); }
            }
        }
    }
}
