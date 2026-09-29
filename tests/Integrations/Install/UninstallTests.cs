// OWNER: INSTALL agent. UninstallString parsing, registry match scoring (pure), Steam/Epic routing.
// Never runs an uninstaller.
using System;
using System.Collections.Generic;

namespace GamesHub.Tests
{
    public static class UninstallTests
    {
        private static bool NoFiles(string p) => false;

        public static void TestParseQuoted()
        {
            ParsedCommand p = UninstallCommand.Parse("\"C:\\Program Files\\Foo Game\\unins000.exe\" /LOG", NoFiles);
            Assert.Equal("C:\\Program Files\\Foo Game\\unins000.exe", p.File);
            Assert.Equal("/LOG", p.Args);
            p = UninstallCommand.Parse("\"D:\\Games\\Bar\\uninstall.exe\"", NoFiles);
            Assert.Equal("D:\\Games\\Bar\\uninstall.exe", p.File);
            Assert.Equal("", p.Args);
        }

        public static void TestParseUnquotedWithSpaces()
        {
            ParsedCommand p = UninstallCommand.Parse("C:\\Program Files (x86)\\My Game\\uninst.exe /S=0 --mode x", NoFiles);
            Assert.Equal("C:\\Program Files (x86)\\My Game\\uninst.exe", p.File);
            Assert.Equal("/S=0 --mode x", p.Args);
            // Existing file wins even when it doesn't end with .exe.
            ParsedCommand q = UninstallCommand.Parse("C:\\My Game\\remove me.bat arg", f => f == "C:\\My Game\\remove me.bat");
            Assert.Equal("C:\\My Game\\remove me.bat", q.File);
            Assert.Equal("arg", q.Args);
            ParsedCommand whole = UninstallCommand.Parse("C:\\A B\\u n.exe", f => f == "C:\\A B\\u n.exe");
            Assert.Equal("C:\\A B\\u n.exe", whole.File);
            Assert.Equal("", whole.Args);
        }

        public static void TestParseMsiExec()
        {
            ParsedCommand p = UninstallCommand.Parse("MsiExec.exe /X{12345678-ABCD-1234-ABCD-1234567890AB}", NoFiles);
            Assert.Equal("MsiExec.exe", p.File);
            Assert.Equal("/X{12345678-ABCD-1234-ABCD-1234567890AB}", p.Args);
            ParsedCommand i = UninstallCommand.Parse("MsiExec.exe /I{12345678-ABCD-1234-ABCD-1234567890AB}", NoFiles);
            Assert.Equal("/X{12345678-ABCD-1234-ABCD-1234567890AB}", i.Args);
            ParsedCommand extra = UninstallCommand.Parse("msiexec /i {12345678-ABCD-1234-ABCD-1234567890AB} REBOOT=R", NoFiles);
            Assert.Equal("msiexec", extra.File);
            Assert.Equal("/X{12345678-ABCD-1234-ABCD-1234567890AB} REBOOT=R", extra.Args);
            Assert.False(extra.Args.ToLowerInvariant().Contains("quiet"));
        }

        public static void TestParseOddInputs()
        {
            Assert.Equal("", UninstallCommand.Parse("", NoFiles).File);
            Assert.Equal("rundll32.exe", UninstallCommand.Parse("rundll32.exe dfshim.dll,ShArpMaintain x.application", NoFiles).File);
            ParsedCommand unterminated = UninstallCommand.Parse("\"C:\\X\\u.exe", NoFiles);
            Assert.Equal("C:\\X\\u.exe", unterminated.File);
        }

        private static bool NeverForbidden(string d) => d.Length <= 3 || d.EndsWith("common", StringComparison.OrdinalIgnoreCase);

        private static UninstallEntry E(string name, string loc = "", string icon = "", string cmd = "u.exe", bool sys = false)
            => new UninstallEntry { DisplayName = name, InstallLocation = loc, DisplayIcon = icon, UninstallString = cmd, SystemComponent = sys };

        public static void TestScoring()
        {
            string dir = @"D:\Games\Hot Wheels Unleashed 2";
            Assert.Equal(100, Math.Min(100, UninstallMatcher.Score(E("Other", dir), dir, "X", NeverForbidden)), "exact location");
            Assert.True(UninstallMatcher.Score(E("Other", @"D:\Games\Hot Wheels Unleashed 2\"), dir + @"\bin\x64", "X", NeverForbidden) >= 90, "entry is parent");
            Assert.True(UninstallMatcher.Score(E("Other", dir + @"\Launcher"), dir, "X", NeverForbidden) >= 70, "entry is child");
            Assert.True(UninstallMatcher.Score(E("Other", "", "\"" + dir + "\\HWU2.exe\",0"), dir, "X", NeverForbidden) >= 80, "icon");
            Assert.True(UninstallMatcher.Score(E("Other", "", dir + "\\HWU2.exe,0"), dir, "X", NeverForbidden) >= 80, "icon unquoted");
            Assert.Equal(0, UninstallMatcher.Score(E("Other", @"D:\Games\Hot Wheels"), dir, "X", NeverForbidden), "sibling prefix is not inside");
            Assert.Equal(0, UninstallMatcher.Score(E("Other", @"D:\"), dir, "X", NeverForbidden), "forbidden entry location");
            Assert.Equal(0, UninstallMatcher.Score(E("Hot Wheels Unleashed 2", dir, cmd: ""), dir, "Hot Wheels Unleashed 2", NeverForbidden), "no uninstall string");
            Assert.Equal(0, UninstallMatcher.Score(E("Hot Wheels Unleashed 2", dir, sys: true), dir, "Hot Wheels Unleashed 2", NeverForbidden), "system component");
        }

        public static void TestNameScore()
        {
            Assert.Equal(70, UninstallMatcher.NameScore("TEKKEN™ 7", "TEKKEN 7"));
            Assert.Equal(70, UninstallMatcher.NameScore("Tom Clancy's Rainbow Six® Siege X", "Tom Clancy's Rainbow Six Siege X"));
            Assert.Equal(60, UninstallMatcher.NameScore("Liar's Bar version 1.2.3", "Liar's Bar"));
            Assert.Equal(60, UninstallMatcher.NameScore("Foo Game v2 x64", "Foo Game"));
            Assert.Equal(0, UninstallMatcher.NameScore("Steam Deck Tools", "Steam"));
            Assert.Equal(0, UninstallMatcher.NameScore("LEGO Marvel Super Heroes", "LEGO Marvel Super Heroes 2"));
            Assert.Equal(0, UninstallMatcher.NameScore("ab", "ab"));
        }

        public static void TestBestPrefersLocationOverName()
        {
            string dir = @"E:\Jogos\MIMESIS";
            var entries = new List<UninstallEntry>
            {
                E("MIMESIS", @"C:\Elsewhere\Mimesis"),
                E("Random Tool", dir),
                E("Unrelated", @"E:\Jogos\Other"),
            };
            Assert.Equal("Random Tool", UninstallMatcher.Best(entries, dir, "MIMESIS", NeverForbidden).DisplayName);
            Assert.Equal("MIMESIS", UninstallMatcher.Best(entries, "", "MIMESIS", NeverForbidden).DisplayName, "name only");
            Assert.True(UninstallMatcher.Best(entries, @"E:\Jogos\Nothing", "Nothing", NeverForbidden) == null, "no match");
            // A forbidden game dir (e.g. games folder / drive root) disables location matching.
            Assert.True(UninstallMatcher.Best(new List<UninstallEntry> { E("X", @"E:\Tools") }, @"E:\", "Y", NeverForbidden) == null);
        }

        public static void TestSteamAndEpicRouting()
        {
            var insp = new InstallInspector(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gameshub-install-tests", "unused.json"));
            UninstallInfo s = insp.FindUninstaller(new Game { Id = "steam:730", Source = "steam", Platform = "Steam", Name = "CS2" });
            Assert.Equal("steam", s.Method);
            Assert.Equal("steam://uninstall/730", s.Command);
            UninstallInfo sf = insp.FindUninstaller(new Game { Id = "folder:cs.url", Source = "folder", Platform = "Steam", SteamAppId = "730", Name = "CS2" });
            Assert.Equal("steam", sf.Method);
            UninstallInfo e = insp.FindUninstaller(new Game { Id = "epic:Fortnite", Source = "epic", Platform = "Epic", Name = "Fortnite" });
            Assert.Equal("epic", e.Method);
            Assert.Equal("com.epicgames.launcher://store/library", e.Command);
        }

        public static void TestGameDirIgnoresLauncherExes()
        {
            Assert.Equal("", InstallInspector.GameDirForMatching(new Game { Name = "2XKO", Exe = @"F:\Riot Games\Riot Client\RiotClientServices.exe" }), "riot client");
            Assert.Equal("", InstallInspector.GameDirForMatching(new Game { Name = "Liars Bar", Exe = @"C:\Users\x\AppData\Local\Programs\Hydra\Hydra.exe" }), "hydra");
            Assert.Equal(@"C:\Program Files (x86)\Steam", InstallInspector.GameDirForMatching(new Game { Name = "Steam", Exe = @"C:\Program Files (x86)\Steam\steam.exe" }), "launcher itself");
            Assert.Equal(@"D:\Games\DIRT 5", InstallInspector.GameDirForMatching(new Game { Name = "DIRT 5", Exe = @"D:\Games\DIRT 5\game_release.exe" }));
            Assert.Equal(@"F:\Riot Games\VALORANT", InstallInspector.GameDirForMatching(new Game { Name = "VALORANT", InstallDir = @"F:\Riot Games\VALORANT\", Exe = @"F:\Riot Games\Riot Client\RiotClientServices.exe" }));
        }

        public static void TestRunNoneFailsWithoutRunningAnything()
        {
            var insp = new InstallInspector(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gameshub-install-tests", "unused.json"));
            OpResult r = insp.RunUninstaller(new UninstallInfo());
            Assert.False(r.Ok);
            Assert.True(r.Message.Contains("desinstalador"), r.Message);
            OpResult bad = insp.RunUninstaller(new UninstallInfo { Method = "steam", Command = "https://evil.example" });
            Assert.False(bad.Ok, "non-steam uri rejected");
        }
    }
}
