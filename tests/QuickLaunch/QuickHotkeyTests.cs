namespace GamesHub.Tests
{
    public static class QuickHotkeyTests
    {
        public static void TestParsesDefault()
        {
            Assert.True(QuickHotkey.TryParse("Ctrl+Shift+Space", out QuickHotkey h));
            Assert.Equal(QuickHotkey.ModControl | QuickHotkey.ModShift, h.Modifiers);
            Assert.Equal(0x20u, h.VirtualKey);
            Assert.Equal("Ctrl+Shift+Space", h.Display);
        }

        public static void TestNormalizesOrderCaseSpacingAndAliases()
        {
            Assert.Equal("Ctrl+Alt+Shift+Win+K", QuickHotkey.Normalize(" win + shift+alt +control+k "));
            Assert.Equal("Ctrl+Space", QuickHotkey.Normalize("ctrl+espaço"));
            Assert.Equal("Alt+Up", QuickHotkey.Normalize("Alt+ArrowUp"));
            Assert.Equal("Ctrl+PageDown", QuickHotkey.Normalize("Ctrl+PgDn"));
            Assert.Equal("Win+Delete", QuickHotkey.Normalize("Meta+Del"));
            Assert.Equal("Ctrl+Num5", QuickHotkey.Normalize("Ctrl+Numpad5"));
            Assert.Equal("Shift+Enter", QuickHotkey.Normalize("Shift+Return"));
        }

        public static void TestDigitsLettersAndFunctionKeys()
        {
            Assert.True(QuickHotkey.TryParse("Alt+7", out QuickHotkey d)); Assert.Equal((uint)'7', d.VirtualKey);
            Assert.True(QuickHotkey.TryParse("ctrl+g", out QuickHotkey g)); Assert.Equal((uint)'G', g.VirtualKey);
            Assert.True(QuickHotkey.TryParse("F1", out QuickHotkey f1)); Assert.Equal(0x70u, f1.VirtualKey);
            Assert.True(QuickHotkey.TryParse("Shift+F24", out QuickHotkey f24)); Assert.Equal(0x87u, f24.VirtualKey);
            Assert.Equal("F13", QuickHotkey.Normalize("f13"));
            Assert.True(QuickHotkey.TryParse("Ctrl+Num0", out QuickHotkey n0)); Assert.Equal(0x60u, n0.VirtualKey);
            Assert.True(QuickHotkey.TryParse("Win+Left", out QuickHotkey l)); Assert.Equal(0x25u, l.VirtualKey);
        }

        public static void TestRejectsInvalid()
        {
            foreach (string bad in new[] { null, "", "   ", "Ctrl", "Ctrl+Shift", "G", "Space", "Ctrl+Ctrl+G", "Ctrl+G+H",
                                           "Ctrl+", "+G", "Ctrl++", "Ctrl+F25", "Ctrl+F0", "Ctrl+F01", "Ctrl+Esc", "Ctrl+Backspace",
                                           "Ctrl+Á", "Ctrl+Num10", "Hyper+G", "Ctrl+Shift+Spacee" })
                Assert.Equal(null, QuickHotkey.Normalize(bad), "should reject '" + bad + "'");
        }

        public static void TestFormatRoundTrips()
        {
            foreach (string s in new[] { "Ctrl+Shift+Space", "Alt+F4", "F9", "Ctrl+Alt+Shift+Win+Z", "Win+Num9", "Ctrl+PrintScreen", "Alt+Tab" })
            {
                Assert.True(QuickHotkey.TryParse(s, out QuickHotkey h), s);
                Assert.Equal(s, h.Display);
                Assert.Equal(s, QuickHotkey.Format(h.Modifiers, h.KeyName));
            }
        }
    }
}
