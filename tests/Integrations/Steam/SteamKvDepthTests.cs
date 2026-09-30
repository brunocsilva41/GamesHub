using System.Text;

namespace GamesHub.Tests
{
    public static class SteamKvDepthTests
    {
        public static void TestHugeNestingDoesNotOverflowTheStack()
        {
            // 100k anonymous '{' (would recurse 100k levels before the depth cap).
            SteamKv anon = SteamKvParser.Parse(new string('{', 100000) + "\"after\" \"1\"");
            Assert.NotNull(anon, "parsed without stack overflow");

            // 100k keyed levels: "k" { "k" { ... } } followed by a sibling that must still be read.
            var sb = new StringBuilder("\"root\" { ");
            for (int i = 0; i < 100000; i++) sb.Append("\"k\" { ");
            sb.Append("\"deep\" \"x\" ");
            for (int i = 0; i < 100000; i++) sb.Append("} ");
            sb.Append("\"sibling\" \"ok\" }");
            SteamKv doc = SteamKvParser.Parse(sb.ToString());
            SteamKv root = doc.Node("root");
            Assert.NotNull(root, "root read");
            Assert.Equal("ok", root.Str("sibling"), "parsing continues after the skipped deep block");

            int depth = 0;
            for (SteamKv n = root; n != null; n = n.Node("k")) depth++;
            Assert.True(depth <= SteamKvParser.MaxDepth + 1, "nesting kept up to the cap only (got " + depth + ")");
            Assert.True(depth >= SteamKvParser.MaxDepth, "levels under the cap are kept (got " + depth + ")");
        }

        public static void TestNormalNestingUnchanged()
        {
            SteamKv doc = SteamKvParser.Parse("\"a\" { \"b\" { \"c\" { \"v\" \"1\" } } \"w\" \"2\" }");
            Assert.Equal("1", doc.Node("a").Node("b").Node("c").Str("v"));
            Assert.Equal("2", doc.Node("a").Str("w"));
        }
    }
}
