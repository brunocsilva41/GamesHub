using System.Collections.Generic;
using System.Linq;

namespace GamesHub.Tests
{
    public static class MetadataParserTests
    {
        private static string J(IEnumerable<string> l) => string.Join("|", l);

        public static void TestMapsCounterStrike2()
        {
            var r = MetadataParser.Parse("730", MetadataFixtures.CounterStrike2);
            Assert.Equal(MetadataParseStatus.Ok, r.Status);
            GameInfo i = r.Info;
            Assert.Equal("730", i.AppId);
            Assert.Equal("Ação|Gratuitos para Jogar", J(i.Genres));
            // Noise (trading cards, remote play, accessibility, VAC, stats...) is dropped.
            Assert.Equal("Multijogador|Multiplataforma|Oficina Steam", J(i.Categories));
            Assert.True(i.ShortDescription.StartsWith("Há mais de duas décadas"), i.ShortDescription);
            Assert.Equal("21/ago./2012", i.ReleaseDate);
            Assert.Equal("Valve", J(i.Developers));
            Assert.Equal("Valve", J(i.Publishers));
            Assert.Equal(0, i.Metacritic);
            Assert.Equal("http://counter-strike.net/", i.Website);
            Assert.False(i.ControllerSupport);
        }

        public static void TestMapsLethalCompany()
        {
            GameInfo i = MetadataParser.Parse("1966720", MetadataFixtures.LethalCompany).Info;
            Assert.Equal("Ação|Aventura|Indie|Acesso Antecipado", J(i.Genres));
            Assert.Equal("Um jogador|Multijogador|Cooperativo|Coop online|Suporte parcial a controle", J(i.Categories));
            Assert.Equal("", i.Website, "null website");
            Assert.Equal("23/out./2023", i.ReleaseDate);
            Assert.Equal("Zeekerss", J(i.Developers));
            Assert.True(i.ControllerSupport, "partial controller via category 18");
        }

        public static void TestMapsTekken7MetacriticAndController()
        {
            GameInfo i = MetadataParser.Parse("389730", MetadataFixtures.Tekken7).Info;
            Assert.Equal(82, i.Metacritic);
            Assert.True(i.ControllerSupport);
            Assert.Equal("Ação|Esportes", J(i.Genres));
            Assert.Equal("Um jogador|Multijogador|PvP online|Tela dividida|Remote Play Together|Suporte total a controle|Conquistas|Nuvem Steam",
                J(i.Categories));
            Assert.Equal("BANDAI NAMCO Studios Inc.", J(i.Developers));
            Assert.Equal("BANDAI NAMCO Entertainment", J(i.Publishers));
            Assert.Equal("1/jun./2017", i.ReleaseDate);
        }

        public static void TestNegativeAndInvalidResponses()
        {
            Assert.Equal(MetadataParseStatus.NotFound, MetadataParser.Parse("1", MetadataFixtures.NotFound).Status);
            Assert.Equal(MetadataParseStatus.NotFound, MetadataParser.Parse("5", "{\"5\":{\"success\":true}}").Status);
            Assert.Equal(MetadataParseStatus.Invalid, MetadataParser.Parse("730", MetadataFixtures.NotFound).Status, "other appid");
            Assert.Equal(MetadataParseStatus.Invalid, MetadataParser.Parse("730", "<html>oops</html>").Status);
            Assert.Equal(MetadataParseStatus.Invalid, MetadataParser.Parse("730", "").Status);
            Assert.Equal(MetadataParseStatus.Invalid, MetadataParser.Parse("730", null).Status);
        }

        public static void TestMissingFieldsDefaults()
        {
            GameInfo i = MetadataParser.Parse("10", "{\"10\":{\"success\":true,\"data\":{\"name\":\"X\",\"metacritic\":null,\"release_date\":null}}}").Info;
            Assert.Equal(0, i.Genres.Count);
            Assert.Equal(0, i.Categories.Count);
            Assert.Equal("", i.ShortDescription);
            Assert.Equal("", i.ReleaseDate);
            Assert.Equal(0, i.Metacritic);
            Assert.False(i.ControllerSupport);
        }

        public static void TestStripHtml()
        {
            Assert.Equal("", MetadataParser.StripHtml(null));
            Assert.Equal("Tom & Jerry", MetadataParser.StripHtml("Tom &amp; Jerry"));
            Assert.Equal("Linha 1 Linha 2", MetadataParser.StripHtml("<b>Linha 1</b><br/>Linha 2"));
            Assert.Equal("Ação \"épica\" – já!", MetadataParser.StripHtml("<p>A&ccedil;&atilde;o &quot;&eacute;pica&quot; &ndash; j&#225;!</p>"));
            Assert.Equal("a b", MetadataParser.StripHtml("  a \r\n\t <img src=\"x.png\"> b "));
            Assert.Equal("Steam! Frequent", MetadataParser.StripHtml("Steam!<br>Frequent"));
        }

        public static void TestCategoryMapping()
        {
            Assert.Equal("", J(MetadataParser.MapCategories(new[] { 29, 41, 62, 68 })), "noise only");
            Assert.Equal("Suporte total a controle", J(MetadataParser.MapCategories(new int[0], "full")));
            Assert.Equal("Suporte parcial a controle", J(MetadataParser.MapCategories(new int[0], "partial")));
            Assert.Equal("Suporte total a controle", J(MetadataParser.MapCategories(new[] { 18 }, "full")), "full wins");
            Assert.Equal("PvP", J(MetadataParser.MapCategories(new[] { 49 })));
            Assert.Equal("PvP em LAN", J(MetadataParser.MapCategories(new[] { 49, 47 })));
            Assert.Equal("Coop local|Tela dividida", J(MetadataParser.MapCategories(new[] { 37, 39, 24 })));
            Assert.Equal("Oficina Steam", J(MetadataParser.MapCategories(new[] { 30, 51 })), "deduped");
        }

        public static void TestAllGenresSortedDistinct()
        {
            var infos = new List<GameInfo>
            {
                new GameInfo { Genres = { "Ação", "Indie" } },
                null,
                new GameInfo { Genres = { "Aventura", "ação", " ", "Esportes" } },
                new GameInfo { Genres = null },
                new GameInfo { Genres = { "Estratégia", "Acesso Antecipado" } },
            };
            Assert.Equal("Ação|Acesso Antecipado|Aventura|Esportes|Estratégia|Indie", J(MetadataService.AllGenres(infos)));
            Assert.Equal(0, MetadataService.AllGenres(null).Count);
        }

        public static void TestNormalizeAppId()
        {
            Assert.Equal("730", MetadataService.Normalize(" 730 "));
            Assert.Equal("730", MetadataService.Normalize("0730"));
            Assert.Equal(null, MetadataService.Normalize(""));
            Assert.Equal(null, MetadataService.Normalize("0"));
            Assert.Equal(null, MetadataService.Normalize("abc"));
            Assert.Equal(null, MetadataService.Normalize("../730"));
        }
    }
}
