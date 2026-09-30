// Never runs an uninstaller: the runner and the native prompt are fakes.
using System;
using System.Collections.Generic;

namespace GamesHub.Tests
{
    public static class UninstallGateTests
    {
        private static bool NeverForbidden(string d) => d.Length <= 3;

        private static UninstallEntry E(string name, string loc = "", string icon = "")
            => new UninstallEntry { DisplayName = name, InstallLocation = loc, DisplayIcon = icon, UninstallString = "u.exe" };

        public static void TestRunnableEntryNeedsFolderOrIconMatch()
        {
            string dir = @"E:\Jogos\MIMESIS";
            var nameOnly = new List<UninstallEntry> { E("MIMESIS", @"C:\Elsewhere\Mimesis"), E("MIMESIS") };
            Assert.True(UninstallMatcher.BestToRun(nameOnly, dir, "MIMESIS", NeverForbidden) == null, "same name elsewhere is never run");
            Assert.True(UninstallMatcher.BestToRun(nameOnly, "", "MIMESIS", NeverForbidden) == null, "name-only without a folder");
            Assert.True(UninstallMatcher.Best(nameOnly, "", "MIMESIS", NeverForbidden) != null, "Best (display ranking) unchanged");

            Assert.Equal("Tool", UninstallMatcher.BestToRun(new List<UninstallEntry> { E("Tool", dir) }, dir, "MIMESIS", NeverForbidden)?.DisplayName, "same folder");
            Assert.Equal("Sub", UninstallMatcher.BestToRun(new List<UninstallEntry> { E("Sub", dir + @"\Launcher") }, dir, "X", NeverForbidden)?.DisplayName, "entry inside the game (75)");
            Assert.Equal("Icon", UninstallMatcher.BestToRun(new List<UninstallEntry> { E("Icon", "", "\"" + dir + "\\game.exe\",0") }, dir, "X", NeverForbidden)?.DisplayName, "icon");
            Assert.Equal(75, UninstallMatcher.MinRunScore);
        }

        public static void TestRegistryUninstallRequiresNativeConfirmation()
        {
            var offer = new UninstallInfo { Method = "registry", Command = "\"C:\\G\\unins000.exe\" /SILENT", DisplayName = "G" };
            int ran = 0;
            OpResult declined = UninstallGate.Run("G", offer, o => false, o => { ran++; return OpResult.Success("x"); });
            Assert.False(declined.Ok);
            Assert.Equal("A desinstalação foi cancelada.", declined.Message);
            Assert.Equal(0, ran, "declined → nothing runs");

            OpResult noPrompt = UninstallGate.Run("G", offer, null, o => { ran++; return OpResult.Success("x"); });
            Assert.False(noPrompt.Ok, "no prompt available → declined");

            UninstallInfo seen = null;
            OpResult ok = UninstallGate.Run("G", offer, o => true, o => { seen = o; ran++; return OpResult.Success("x"); });
            Assert.True(ok.Ok);
            Assert.Equal(1, ran);
            Assert.True(ReferenceEquals(offer, seen), "exactly the offered entry runs");
        }

        public static void TestLaunchersAndMissingOffers()
        {
            int prompts = 0;
            var steam = new UninstallInfo { Method = "steam", Command = "steam://uninstall/730", DisplayName = "CS2" };
            Assert.True(UninstallGate.Check("CS2", steam, o => { prompts++; return false; }) == null, "Steam asks by itself");
            Assert.Equal(0, prompts);
            Assert.False(UninstallGate.NeedsNativeConfirmation(steam));

            OpResult none = UninstallGate.Check("X", null, o => true);
            Assert.True(none != null && !none.Ok && none.Message.Contains("novamente"), "no offer shown → no fresh search");
            OpResult empty = UninstallGate.Check("X", new UninstallInfo(), o => true);
            Assert.True(empty != null && !empty.Ok);
        }

        public static void TestOffersAreCopiesPerGame()
        {
            var offers = new UninstallOffers();
            var info = new UninstallInfo { Method = "registry", Command = "a.exe", DisplayName = "A" };
            offers.Remember("g1", info);
            info.Command = "evil.exe";                                   // later mutation does not leak in
            Assert.Equal("a.exe", offers.Get("g1").Command);
            offers.Get("g1").Command = "evil.exe";
            Assert.Equal("a.exe", offers.Get("g1").Command, "handed-out copies");
            Assert.True(offers.Get("g2") == null);
            offers.Remember("g1", new UninstallInfo());                  // now "none"
            Assert.True(offers.Get("g1") == null);
            offers.Remember("g1", info);
            offers.Forget("g1");
            Assert.True(offers.Get("g1") == null);
        }

        public static void TestDescribeShowsProgramAndArguments()
        {
            var offer = new UninstallInfo { Method = "registry", Command = "x", DisplayName = "Foo\nBar" };
            string text = UninstallGate.Describe(offer, new ParsedCommand { File = @"C:\G\unins000.exe", Args = "/LOG\r\n/X" });
            Assert.True(text.Contains(@"C:\G\unins000.exe"), text);
            Assert.True(text.Contains(@"/LOG\r\n/X"), "escaped: " + text);
            Assert.True(text.Contains(@"Foo\nBar"), "display name escaped: " + text);
            Assert.True(UninstallGate.Describe(offer, new ParsedCommand { File = "u.exe" }).Contains("(nenhum)"));
        }
    }
}
