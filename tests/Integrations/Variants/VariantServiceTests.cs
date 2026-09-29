// OWNER: VARIANTS agent.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GamesHub.Tests
{
    public static class VariantServiceTests
    {
        private static string TempStore()
        {
            string dir = Path.Combine(Path.GetTempPath(), "gameshub-variants-test", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "variants.json");
        }

        private static Game G(string id, string name, string platform = "PC", long play = 0, string appId = "")
            => new Game { Id = id, Name = name, Platform = platform, PlaySeconds = play, SteamAppId = appId };

        private static List<Game> Real() => new List<Game>
        {
            G("folder:2xko.lnk", "2XKO", "Riot"),
            G("folder:call of duty - black ops 3.lnk", "Call of Duty - Black Ops 3"),
            G("folder:cb servers launcher.lnk", "CB Servers Launcher"),
            G("folder:counter-strike 2.url", "Counter-Strike 2", "Steam", 0, "730"),
            G("folder:counter-strike.lnk", "Counter-Strike", "Steam", 0, "10"),
            G("folder:lego marvel super heroes 2.lnk", "LEGO Marvel Super Heroes 2", "PC", 100),
            G("folder:lego marvel super heroes 2 directx 11.lnk", "LEGO Marvel Super Heroes 2 DirectX 11", "PC", 500),
            G("folder:plutonium.exe", "plutonium"),
            G("folder:roblox player.lnk", "Roblox Player", "Roblox"),
            G("folder:roblox studio.lnk", "Roblox Studio", "Roblox"),
            G("folder:epic games launcher.lnk", "Epic Games Launcher", "Epic"),
        };

        public static void TestSuggestRealNames()
        {
            var svc = new VariantService(TempStore());
            var s = svc.Suggest(Real());
            Assert.Equal(1, s.Count, "only LEGO should be suggested");
            var g = s[0];
            Assert.Equal("folder:lego marvel super heroes 2.lnk", g.PrimaryId, "primary = unqualified entry even if less played");
            Assert.Equal(2, g.MemberIds.Count);
            Assert.Equal(g.PrimaryId, g.MemberIds[0], "primary first");
            Assert.Equal("Padrão", g.Labels[g.PrimaryId]);
            Assert.Equal("DirectX 11", g.Labels["folder:lego marvel super heroes 2 directx 11.lnk"]);
        }

        public static void TestSuggestDifferentPlatformsNotGrouped()
        {
            var svc = new VariantService(TempStore());
            var s = svc.Suggest(new List<Game> { G("steam:1", "Game", "Steam"), G("folder:game dx11.lnk", "Game DX11", "PC") });
            Assert.Equal(0, s.Count);
        }

        public static void TestSuggestBySteamAppIdAcrossPlatforms()
        {
            var svc = new VariantService(TempStore());
            var s = svc.Suggest(new List<Game>
            {
                G("steam:730", "Counter-Strike 2", "Steam", 1000, "730"),
                G("folder:cs2.url", "CS2 atalho", "PC", 0, "730"),
                G("steam:10", "Counter-Strike", "Steam", 0, "10"),
            });
            Assert.Equal(1, s.Count);
            Assert.True(s[0].MemberIds.Contains("steam:730") && s[0].MemberIds.Contains("folder:cs2.url"));
            Assert.Equal("steam:730", s[0].PrimaryId, "most played when nobody has a qualifier");
        }

        public static void TestSuggestBySameExe()
        {
            var svc = new VariantService(TempStore());
            var a = G("folder:a.lnk", "Alpha"); a.Exe = @"D:\Games\Alpha\bin\alpha.exe";
            var b = G("folder:b.lnk", "Alpha Tools"); b.Exe = @"d:\games\alpha\bin\ALPHA.exe";
            var r1 = G("folder:lol.lnk", "League of Legends"); r1.Exe = @"C:\Riot Games\Riot Client\RiotClientServices.exe";
            var r2 = G("folder:2xko.lnk", "2XKO"); r2.Exe = @"C:\Riot Games\Riot Client\RiotClientServices.exe";
            var s = svc.Suggest(new List<Game> { a, b, r1, r2 });
            Assert.Equal(1, s.Count, "launcher exe must not group unrelated games");
            Assert.Equal(2, s[0].MemberIds.Count);
        }

        public static void TestSuggestExcludesGroupedAndDismissed()
        {
            var svc = new VariantService(TempStore());
            var games = Real();
            games.Add(G("folder:x.lnk", "Xeno"));
            games.Add(G("folder:x vulkan.lnk", "Xeno Vulkan"));
            Assert.Equal(2, svc.Suggest(games).Count);
            Assert.True(svc.DismissSuggestion(new List<string> { "folder:x vulkan.lnk", "folder:x.lnk" }).Ok);
            Assert.Equal(1, svc.Suggest(games).Count);
            var lego = svc.Suggest(games)[0];
            Assert.True(svc.Group(lego.MemberIds, lego.PrimaryId).Ok);
            Assert.Equal(0, svc.Suggest(games).Count);
        }

        public static void TestGroupValidation()
        {
            var svc = new VariantService(TempStore());
            Assert.False(svc.Group(new List<string> { "a" }, "a").Ok);
            Assert.False(svc.Group(new List<string> { "a", "b" }, "c").Ok);
            Assert.True(svc.Group(new List<string> { "a", "b" }, "b").Ok);
            Assert.False(svc.Group(new List<string> { "b", "c" }, "c").Ok, "member already grouped");
            var g = svc.GetGroups().Single();
            Assert.Equal("b", g.PrimaryId);
            Assert.False(svc.SetLabel("zzz", "X").Ok);
            Assert.False(svc.SetPrimary(g.Id, "zzz").Ok);
            Assert.False(svc.Ungroup("nope").Ok);
            Assert.False(svc.SetLabel("a", new string('x', 41)).Ok);
            Assert.True(svc.SetLabel("a", "Um").Ok);
            Assert.False(svc.SetLabel("b", "um").Ok, "duplicate label");
            Assert.True(svc.Ungroup(g.Id).Ok);
            Assert.Equal(0, svc.GetGroups().Count);
        }

        public static void TestManualGroupBo3Plutonium()
        {
            string store = TempStore();
            var svc = new VariantService(store);
            string bo3 = "folder:call of duty - black ops 3.lnk", plu = "folder:plutonium.exe";
            Assert.True(svc.Group(new List<string> { bo3, plu }, bo3).Ok);
            Assert.True(svc.SetLabel(plu, "Plutonium").Ok);
            var games = Real();
            games.First(x => x.Id == plu).PlaySeconds = 3600;
            var applied = svc.Apply(games);
            Assert.Equal(games.Count - 1, applied.Count);
            var card = applied.Single(x => x.Id == bo3);
            Assert.Equal(2, card.Variants.Count);
            Assert.Equal(bo3, card.Variants[0].Id);
            Assert.Equal("Padrão", card.Variants[0].Label);
            Assert.Equal("Plutonium", card.Variants[1].Label);
            Assert.Equal(3600L, card.PlaySeconds);
            Assert.Equal(plu, svc.ResolveLaunchId(bo3, plu));
            Assert.Equal(bo3, svc.ResolveLaunchId(bo3, "folder:tekken 7.exe"), "foreign variant id ignored");
            Assert.Equal(bo3, svc.ResolveLaunchId(bo3, null));
        }

        public static void TestApplyAggregatesWithoutMutating()
        {
            var svc = new VariantService(TempStore());
            var a = G("a", "Game", "PC", 100); a.LastPlayed = new DateTime(2026, 1, 1); a.Collections.Add("RPG");
            a.Art.Header = "a/header.jpg";
            var b = G("b", "Game DX11", "PC", 50); b.LastPlayed = new DateTime(2026, 5, 1); b.Running = true; b.Favorite = true;
            b.Art.Hero = "b/hero.jpg";
            var c = G("c", "Other");
            var input = new List<Game> { b, c, a };
            Assert.True(svc.Group(new List<string> { "a", "b" }, "a").Ok);
            var outp = svc.Apply(input);

            Assert.Equal(2, outp.Count);
            Assert.Equal("c", outp[0].Id, "stable order: primary keeps its own position");
            Assert.Equal("a", outp[1].Id);
            Game card = outp[1];
            Assert.False(ReferenceEquals(card, a), "primary is cloned");
            Assert.True(ReferenceEquals(outp[0], c));
            Assert.Equal(150L, card.PlaySeconds);
            Assert.Equal(new DateTime(2026, 5, 1), card.LastPlayed.Value);
            Assert.True(card.Running && card.Favorite);
            Assert.Equal("RPG", card.Collections.Single());
            Assert.Equal("a/header.jpg", card.Art.Header);
            Assert.Equal("b/hero.jpg", card.Art.Hero);
            Assert.Equal("Padrão", card.Variants[0].Label);
            Assert.Equal("DirectX 11", card.Variants[1].Label);

            // input untouched
            Assert.Equal(3, input.Count);
            Assert.Equal(100L, a.PlaySeconds);
            Assert.False(a.Running || a.Favorite);
            Assert.Equal(0, a.Variants.Count);
            Assert.True(a.Art.Hero == null);
            card.Collections.Add("X");
            Assert.Equal(1, a.Collections.Count, "lists deep-copied");
        }

        public static void TestApplyMissingMembers()
        {
            var svc = new VariantService(TempStore());
            Assert.True(svc.Group(new List<string> { "a", "b", "c" }, "a").Ok);
            // only one member present -> no variants, game untouched
            var one = svc.Apply(new List<Game> { G("b", "B"), G("z", "Z") });
            Assert.Equal(2, one.Count);
            Assert.Equal(0, one[0].Variants.Count);
            // none present -> group kept
            svc.Apply(new List<Game> { G("z", "Z") });
            Assert.Equal(1, svc.GetGroups().Count);
            // primary missing -> first present member becomes the card
            var two = svc.Apply(new List<Game> { G("c", "C"), G("b", "B") });
            Assert.Equal(1, two.Count);
            Assert.Equal("b", two[0].Id);
            Assert.Equal(2, two[0].Variants.Count);
        }

        public static void TestSetPrimary()
        {
            var svc = new VariantService(TempStore());
            svc.Group(new List<string> { "a", "b" }, "a");
            string gid = svc.GetGroups()[0].Id;
            Assert.True(svc.SetPrimary(gid, "b").Ok);
            var outp = svc.Apply(new List<Game> { G("a", "A"), G("b", "B") });
            Assert.Equal("b", outp.Single().Id);
            Assert.Equal("b", outp.Single().Variants[0].Id);
        }

        public static void TestPersistenceRoundTrip()
        {
            string store = TempStore();
            var svc = new VariantService(store);
            svc.Group(new List<string> { "a", "b" }, "b");
            svc.SetLabel("a", "Vulkan");
            svc.DismissSuggestion(new List<string> { "y", "x" });
            Assert.True(File.Exists(store));
            Assert.False(File.Exists(store + ".tmp"));

            var again = new VariantService(store);
            var g = again.GetGroups().Single();
            Assert.Equal("b", g.PrimaryId);
            Assert.Equal("Vulkan", g.Labels["a"]);
            Assert.Equal(0, again.Suggest(new List<Game> { G("x", "Q"), G("y", "Q DX11") }).Count, "dismissal persisted");
        }

        public static void TestCorruptFileStartsEmpty()
        {
            string store = TempStore();
            File.WriteAllText(store, "{ not json");
            var svc = new VariantService(store);
            Assert.Equal(0, svc.GetGroups().Count);
            Assert.True(File.Exists(store + ".bad"));
        }
    }
}
