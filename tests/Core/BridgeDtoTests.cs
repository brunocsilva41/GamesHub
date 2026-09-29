using System;
using System.Collections;
using System.Collections.Generic;

namespace GamesHub.Tests
{
    public static class BridgeDtoTests
    {
        private static long? Stamp(string rel) => rel.StartsWith("missing") ? (long?)null : 42;

        public static void TestArtUrl()
        {
            Assert.Equal("https://art.gameshub.example/steam-730/hero.jpg?v=42", BridgeDto.ArtUrl("steam-730/hero.jpg", Stamp));
            Assert.Equal("https://art.gameshub.example/folder-my%20game/icon.png?v=42", BridgeDto.ArtUrl("folder-my game\\icon.png", Stamp));
            Assert.Equal(null, BridgeDto.ArtUrl("missing/hero.jpg", Stamp));
            Assert.Equal(null, BridgeDto.ArtUrl("", Stamp));
            Assert.Equal(null, BridgeDto.ArtUrl(null, Stamp));
            Assert.Equal(null, BridgeDto.ArtUrl("../secret.txt", Stamp));
            Assert.Equal(null, BridgeDto.ArtUrl("C:/x.jpg", Stamp));
        }

        public static void TestGameMapping()
        {
            var g = new Game
            {
                Id = "steam:730", Name = "Counter-Strike 2", Source = "steam", Platform = "Steam", Favorite = true,
                Collections = new List<string> { "FPS" }, PlaySeconds = 3600,
                LastPlayed = new DateTime(2024, 5, 1, 12, 30, 0, DateTimeKind.Utc),
                AddedAt = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc),
                Art = new Artwork { Header = "steam-730/header.jpg", Hero = "missing/hero.jpg" },
                Variants = new List<GameVariant> { new GameVariant { Id = "folder:cs2-dx11.lnk", Label = "DirectX 11" } },
            };
            Dictionary<string, object> d = BridgeDto.Game(g, Stamp);
            Assert.Equal("steam:730", d["id"]);
            Assert.Equal(true, d["favorite"]);
            Assert.Equal(3600L, d["playSeconds"]);
            Assert.Equal("2024-05-01T12:30:00.000Z", d["lastPlayed"]);
            Assert.Equal("2024-01-02T03:04:05.000Z", d["addedAt"]);
            Assert.Equal(-1L, d["sizeBytes"]);
            var art = (Dictionary<string, object>)d["art"];
            Assert.Equal("https://art.gameshub.example/steam-730/header.jpg?v=42", art["header"]);
            Assert.Equal(null, art["hero"]);
            Assert.Equal(null, art["logo"]);

            // Must serialize to the camelCase shape the UI expects.
            string json = Json.Serialize(d);
            Assert.True(json.Contains("\"lastPlayed\":\"2024-05-01T12:30:00.000Z\""), json);
            Assert.True(json.Contains("\"variants\":[{\"id\":\"folder:cs2-dx11.lnk\",\"label\":\"DirectX 11\"}]"), json);
        }

        public static void TestNullLastPlayed()
        {
            Dictionary<string, object> d = BridgeDto.Game(new Game { Id = "x", Name = "X" }, Stamp);
            Assert.Equal(null, d["lastPlayed"]);
        }

        public static void TestReplyShape()
        {
            Dictionary<string, object> ok = BridgeDto.Reply(17, true, null, null);
            Assert.Equal("reply", ok["type"]);
            Assert.Equal(17, ok["id"]);
            Assert.True(ok.ContainsKey("data") && !ok.ContainsKey("error"));

            Dictionary<string, object> err = BridgeDto.Reply(18, false, null, "Falhou");
            Assert.Equal(false, err["ok"]);
            Assert.Equal("Falhou", err["error"]);
        }

        public static void TestOpResultData()
        {
            Dictionary<string, object> d = BridgeDto.OpResultData(OpResult.Success("Removido", "folder:a.lnk", "tok1"));
            Assert.Equal("Removido", d["message"]);
            Assert.Equal("tok1", d["undoToken"]);
            Assert.Equal("folder:a.lnk", d["gameId"]);
            Assert.Equal(false, BridgeDto.OpResultEntry(OpResult.Fail("x"))["ok"]);
        }

        public static void TestParseEdit()
        {
            var raw = (IDictionary<string, object>)Json.DeserializeObject(
                "{\"name\":\"Novo\",\"favorite\":true,\"collections\":[\"A\",\" a \",\"\",\"B\"],\"hidden\":\"nope\"}");
            GameEdit e = BridgeDto.ParseEdit(raw);
            Assert.Equal("Novo", e.Name);
            Assert.Equal(true, e.Favorite);
            Assert.Equal(null, e.Hidden);
            Assert.Equal(null, e.LaunchArgs);
            Assert.Equal(2, e.Collections.Count);
            Assert.Equal("A", e.Collections[0]);
            Assert.Equal("B", e.Collections[1]);

            bool threw = false;
            try { BridgeDto.ParseEdit(new Dictionary<string, object> { ["steamAppId"] = "12a" }); }
            catch (BridgeException) { threw = true; }
            Assert.True(threw, "non-numeric steamAppId must be rejected");
        }

        public static void TestRecentGames()
        {
            var games = new List<Game>
            {
                new Game { Id = "a", Name = "A", LastPlayed = new DateTime(2024, 1, 1) },
                new Game { Id = "b", Name = "B", LastPlayed = new DateTime(2024, 3, 1) },
                new Game { Id = "c", Name = "C" },
                new Game { Id = "d", Name = "D", LastPlayed = new DateTime(2024, 4, 1), Hidden = true },
                new Game { Id = "e", Name = "E", LastPlayed = new DateTime(2024, 2, 1) },
            };
            List<Game> r = BridgeDto.RecentGames(games, 2);
            Assert.Equal(2, r.Count);
            Assert.Equal("b", r[0].Id);
            Assert.Equal("e", r[1].Id);
        }

        public static void TestAppUriAndExternalUrls()
        {
            Assert.True(BridgeDto.IsAppUri("https://app.gameshub.example/index.html"));
            Assert.False(BridgeDto.IsAppUri("https://app.gameshub.example.evil.com/"));
            Assert.False(BridgeDto.IsAppUri("http://app.gameshub.example/"));
            Assert.False(BridgeDto.IsAppUri("file:///C:/x.html"));
            Assert.True(ShellActions.IsSafeExternalUrl("https://store.steampowered.com/app/730"));
            Assert.False(ShellActions.IsSafeExternalUrl("http://example.com"));
            Assert.False(ShellActions.IsSafeExternalUrl("file:///C:/Windows/notepad.exe"));
            Assert.False(ShellActions.IsSafeExternalUrl("https://user:pw@example.com"));
        }
    }
}
