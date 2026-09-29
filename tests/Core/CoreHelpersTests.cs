using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;

namespace GamesHub.Tests
{
    public static class HotkeyParserTests
    {
        public static void TestParsesCommonCombos()
        {
            Assert.True(HotkeyParser.TryParse("Ctrl+Alt+G", out Hotkey hk));
            Assert.Equal(HotkeyParser.ModControl | HotkeyParser.ModAlt, hk.Modifiers);
            Assert.Equal((uint)'G', hk.VirtualKey);
            Assert.Equal("Ctrl+Alt+G", hk.Display);

            Assert.True(HotkeyParser.TryParse("win+shift+f5", out hk));
            Assert.Equal(0x74u, hk.VirtualKey);
            Assert.Equal("Shift+Win+F5", hk.Display);

            Assert.True(HotkeyParser.TryParse("Control+Space", out hk));
            Assert.Equal(0x20u, hk.VirtualKey);
            Assert.Equal("Ctrl+Space", hk.Display);

            Assert.True(HotkeyParser.TryParse("F9", out hk), "bare function key allowed");
            Assert.True(HotkeyParser.TryParse("Alt+7", out hk));
            Assert.Equal((uint)'7', hk.VirtualKey);
        }

        public static void TestRejectsInvalid()
        {
            foreach (string bad in new[] { "", "  ", "G", "Ctrl", "Ctrl+", "Ctrl+Alt", "Ctrl+Ctrl+G", "Ctrl+G+H", "Ctrl+F25", "Ctrl+Banana", "Ctrl++G" })
                Assert.False(HotkeyParser.TryParse(bad, out _), "should reject '" + bad + "'");
        }
    }

    public static class WindowPlacementTests
    {
        private static readonly Size Min = new Size(900, 600);
        private static readonly Size Def = new Size(1240, 800);
        private static readonly List<Rectangle> OneScreen = new List<Rectangle> { new Rectangle(0, 0, 1920, 1040) };

        public static void TestEmptyBoundsCenterOnPrimary()
        {
            Rectangle r = WindowPlacement.Clamp(Rectangle.Empty, OneScreen, Min, Def);
            Assert.Equal(new Rectangle(340, 120, 1240, 800), r);
        }

        public static void TestVisibleBoundsKept()
        {
            var saved = new Rectangle(100, 50, 1000, 700);
            Assert.Equal(saved, WindowPlacement.Clamp(saved, OneScreen, Min, Def));
        }

        public static void TestOffscreenBoundsRecentered()
        {
            Rectangle r = WindowPlacement.Clamp(new Rectangle(5000, 3000, 1000, 700), OneScreen, Min, Def);
            Assert.Equal(new Rectangle(340, 120, 1240, 800), r);
        }

        public static void TestPartiallyVisibleMovedInside()
        {
            Rectangle r = WindowPlacement.Clamp(new Rectangle(1500, 800, 1000, 700), OneScreen, Min, Def);
            Assert.Equal(new Rectangle(920, 340, 1000, 700), r);
        }

        public static void TestSizeClampedToMinAndWorkArea()
        {
            Rectangle r = WindowPlacement.Clamp(new Rectangle(10, 10, 300, 200), OneScreen, Min, Def);
            Assert.Equal(new Size(900, 600), r.Size);
            r = WindowPlacement.Clamp(new Rectangle(0, 0, 4000, 3000), OneScreen, Min, Def);
            Assert.Equal(new Rectangle(0, 0, 1920, 1040), r);
        }

        public static void TestSecondMonitorWithNegativeCoords()
        {
            var screens = new List<Rectangle> { new Rectangle(0, 0, 1920, 1040), new Rectangle(-1280, 0, 1280, 984) };
            var saved = new Rectangle(-1200, 100, 1000, 700);
            Assert.Equal(saved, WindowPlacement.Clamp(saved, screens, Min, Def));
        }

        public static void TestHitTestBorder()
        {
            var client = new Size(1000, 800);
            Assert.Equal(1, WindowPlacement.HitTestBorder(new Point(500, 400), client, 5), "center");
            Assert.Equal(10, WindowPlacement.HitTestBorder(new Point(2, 400), client, 5), "left");
            Assert.Equal(11, WindowPlacement.HitTestBorder(new Point(998, 400), client, 5), "right");
            Assert.Equal(12, WindowPlacement.HitTestBorder(new Point(500, 1), client, 5), "top");
            Assert.Equal(15, WindowPlacement.HitTestBorder(new Point(500, 799), client, 5), "bottom");
            Assert.Equal(13, WindowPlacement.HitTestBorder(new Point(1, 1), client, 5), "top-left");
            Assert.Equal(13, WindowPlacement.HitTestBorder(new Point(10, 2), client, 5), "top-left along top");
            Assert.Equal(17, WindowPlacement.HitTestBorder(new Point(999, 790), client, 5), "bottom-right");
            Assert.Equal(1, WindowPlacement.HitTestBorder(new Point(2, 2), client, 0), "no border when maximized");
        }
    }

    public static class StartupArgsTests
    {
        public static void TestParse()
        {
            StartupArgs a = StartupArgs.Parse(new[] { "--minimized", "--launch", "steam:730", "--DEBUG" });
            Assert.True(a.Minimized);
            Assert.True(a.Debug);
            Assert.Equal("steam:730", a.LaunchId);
            Assert.False(a.IsPlain);
            Assert.True(StartupArgs.Parse(new string[0]).IsPlain);
            Assert.Equal(null, StartupArgs.Parse(new[] { "--launch" }).LaunchId);
        }

        public static void TestQuoteRoundTrip()
        {
            Assert.Equal("steam:730", StartupArgs.Quote("steam:730"));
            Assert.Equal("\"folder:my game.lnk\"", StartupArgs.Quote("folder:my game.lnk"));
            // Only backslashes that precede a quote (here: the closing one) are doubled.
            Assert.Equal("\"a\\ b\\\\\"", StartupArgs.Quote("a\\ b\\"));
            Assert.Equal("\"say \\\"hi\\\"\"", StartupArgs.Quote("say \"hi\""));
        }
    }

    public static class EventCoalescerTests
    {
        public static void TestBurstIsCoalesced()
        {
            int runs = 0;
            using (var c = new EventCoalescer(() => Interlocked.Increment(ref runs), 200))
            {
                for (int i = 0; i < 50; i++) c.Signal();
                Thread.Sleep(700);
                int n = Volatile.Read(ref runs);
                Assert.True(n >= 1 && n <= 2, "expected 1-2 runs, got " + n);
                c.Signal();
                Thread.Sleep(450);
                Assert.True(Volatile.Read(ref runs) == n + 1, "trailing signal must run");
            }
        }
    }
}
