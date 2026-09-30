using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace GamesHub.Tests
{
    /// <summary>What GamesHub hands to the shell (.url targets, batch arguments), local-path rules for manifest
    /// values, and the Authenticode helper.</summary>
    public static class LaunchHardeningTests
    {
        private const string UrlFile = @"C:\G\Game.url";

        public static void TestUrlLaunchesOnlyAllowedLauncherSchemes()
        {
            string[] allowed =
            {
                "steam://rungameid/730",
                "com.epicgames.launcher://apps/fn%3A4fe75bbc5a674f4f9b356b5c90567da5%3AFortnite?action=launch&silent=true",
                "riotclient://launch", "battlenet://Pro", "blizzard://WoW", "origin://launchgame/OFB-EAST:1",
                "origin2://game/launch?offerIds=1", "ealink://launchgame/1", "uplay://launch/635/0",
                "goggalaxy://openGameView/1", "hydralauncher://run?objectId=1", "STEAM://rungameid/1",
            };
            foreach (string u in allowed)
                Assert.Equal(u, GameLauncher.UrlLaunchTarget(u, UrlFile), "kept verbatim: " + u);
            Assert.Equal("steam://rungameid/730", GameLauncher.UrlLaunchTarget("  steam://rungameid/730 \r", UrlFile), "trimmed");

            string[] refused =
            {
                "https://example.com/x", "http://example.com", "file:///C:/Windows/System32/calc.exe", "file://server/share/x.exe",
                @"\\server\share\x.exe", "//server/share/x.exe", @"C:\Windows\System32\calc.exe", "search-ms:query=x&crumb=location:\\\\evil\\s",
                "ms-msdt:/id PCWDiagnostic", "javascript:alert(1)", "steam://run/1 --evil", "steam://run/1\"x", "", null, "steam:",
            };
            foreach (string u in refused)
                Assert.Equal(UrlFile, GameLauncher.UrlLaunchTarget(u, UrlFile), "falls back to the .url file: " + u);
        }

        public static void TestUrlGameStartInfo()
        {
            var bad = new Game { Source = "folder", Ext = ".url", FilePath = UrlFile, LaunchTarget = @"\\evil\share\payload.exe" };
            Assert.Equal(UrlFile, GameLauncher.BuildStartInfo(bad, null).FileName, "UNC target never launched raw");
            var web = new Game { Source = "folder", Ext = ".url", FilePath = UrlFile, LaunchTarget = "https://example.com" };
            Assert.Equal(UrlFile, GameLauncher.BuildStartInfo(web, null).FileName, "web link opened through the .url file (MotW/SmartScreen)");
            var ok = new Game { Source = "folder", Ext = ".url", FilePath = UrlFile, LaunchTarget = "steam://rungameid/730" };
            Assert.Equal("steam://rungameid/730", GameLauncher.BuildStartInfo(ok, null).FileName);
            var empty = new Game { Source = "folder", Ext = ".url", FilePath = UrlFile, LaunchTarget = "" };
            Assert.Equal(UrlFile, GameLauncher.BuildStartInfo(empty, null).FileName);
        }

        public static void TestBatchScriptsRefuseShellMetacharacters()
        {
            var bat = new ProcessStartInfo(@"C:\Games\start.bat");
            var cmd = new ProcessStartInfo("\"C:\\Games\\start.CMD\"");
            var exe = new ProcessStartInfo(@"C:\Games\game.exe");
            foreach (string evil in new[] { "-a & calc", "x|y", "<in", ">out", "a^b", "%PATH%", "\"q\"" })
            {
                string msg = GameLauncher.UnsafeBatchArgs(bat, evil);
                Assert.True(msg != null && msg.Contains(".bat/.cmd"), ".bat refuses: " + evil);
                Assert.True(GameLauncher.UnsafeBatchArgs(cmd, "", evil) != null, ".cmd refuses (any added arg): " + evil);
                Assert.True(GameLauncher.UnsafeBatchArgs(exe, evil) == null, ".exe unaffected: " + evil);
            }
            Assert.True(GameLauncher.UnsafeBatchArgs(bat, "-windowed -fps 60", "") == null, "plain args are fine");
            Assert.True(GameLauncher.UnsafeBatchArgs(bat, null) == null);
            Assert.True(GameLauncher.UnsafeBatchArgs(null, "&") == null);
        }

        public static void TestLocalAbsolutePaths()
        {
            Assert.True(SafePath.IsLocalAbsolute(@"C:\Games\X"));
            Assert.True(SafePath.IsLocalAbsolute("d:/Games/X"));
            Assert.False(SafePath.IsLocalAbsolute(@"\\server\share\X"), "UNC");
            Assert.False(SafePath.IsLocalAbsolute("//server/share/X"), "UNC with slashes");
            Assert.False(SafePath.IsLocalAbsolute(@"\\?\C:\X"), "device namespace");
            Assert.False(SafePath.IsLocalAbsolute(@"\\.\PhysicalDrive0"), "device");
            Assert.False(SafePath.IsLocalAbsolute("C:Games"), "drive-relative");
            Assert.False(SafePath.IsLocalAbsolute(@"Games\X"), "relative");
            Assert.False(SafePath.IsLocalAbsolute("C:\\a|b"), "invalid char");
            Assert.False(SafePath.IsLocalAbsolute(""));
            Assert.False(SafePath.IsLocalAbsolute(null));
        }

        public static void TestEpicUncInstallLocationIsUnknown()
        {
            Game g = EpicSource.ParseManifest("{ \"AppName\": \"Sugar\", \"DisplayName\": \"Rocket League\", \"CatalogNamespace\": \"ns\"," +
                                              " \"CatalogItemId\": \"id\", \"InstallLocation\": \"\\\\\\\\evil\\\\share\\\\RL\", \"LaunchExecutable\": \"RL.exe\" }");
            Assert.NotNull(g, "the game is still imported (it launches through the Epic URI)");
            Assert.Equal("", g.InstallDir, "UNC install folder dropped");
            Assert.Equal("", g.Exe, "no exe under an unknown folder");
            Game local = EpicSource.ParseManifest("{ \"AppName\": \"Sugar\", \"InstallLocation\": \"D:/Epic/RL/\", \"LaunchExecutable\": \"RL.exe\" }");
            Assert.Equal(@"D:\Epic\RL", local.InstallDir);
            Assert.Equal(@"D:\Epic\RL\RL.exe", local.Exe);
        }

        public static void TestInstallSizeNeverWalksUncPaths()
        {
            Assert.True(InstallPaths.IsForbiddenRoot(@"\\server\share\Games\Deep\Game"), "any UNC path");
            Assert.True(InstallPaths.IsForbiddenRoot("//server/share/Games/Game"), "UNC with slashes");
            Assert.True(InstallPaths.IsForbiddenRoot(@"\\?\C:\Games\Game"), "device path");
            Assert.False(InstallPaths.IsForbiddenRoot(@"D:\Jogos\TEKKEN 7"), "local game folder still allowed");
        }

        // ------------------------------------------------------------------ Authenticode

        public static void TestRiotSubjectMatching()
        {
            Assert.True(Authenticode.IsRiotSubject("CN=\"Riot Games, Inc.\", O=\"Riot Games, Inc.\", L=Los Angeles, S=California, C=US"));
            Assert.True(Authenticode.IsRiotSubject("CN=Riot Games Inc, O=Riot Games Inc, C=US"));
            Assert.True(Authenticode.IsRiotSubject("O=Riot Games, C=US"));
            Assert.False(Authenticode.IsRiotSubject("CN=Not Riot Games, O=Evil LLC"), "substring is not enough");
            Assert.False(Authenticode.IsRiotSubject("CN=\"Riot Games, Inc. Fan Club\", O=Evil"), "longer quoted value");
            Assert.False(Authenticode.IsRiotSubject("OU=Riot Games, CN=Evil"), "other attribute");
            Assert.False(Authenticode.IsRiotSubject(""));
            Assert.False(Authenticode.IsRiotSubject(null));
            var parts = Authenticode.SplitDn("CN=\"A, B\", O=C");
            Assert.Equal(2, parts.Count);
            Assert.Equal("A, B", parts[0].Value);
        }

        public static void TestAuthenticodeRejectsUnsignedAndTamperedFiles()
        {
            string dir = Path.Combine(Path.GetTempPath(), "gameshub-test-authenticode-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(dir);
                string unsigned = Path.Combine(dir, "RiotClientServices.exe");
                File.WriteAllBytes(unsigned, new byte[] { 0x4D, 0x5A, 0, 0 });
                Assert.False(Authenticode.IsSignedBy(unsigned, s => true), "unsigned file");
                Assert.False(Authenticode.IsRiotSigned(unsigned));
                Assert.False(Authenticode.IsSignedBy(Path.Combine(dir, "missing.exe"), s => true), "missing file");
                Assert.False(Authenticode.IsSignedBy(@"\\server\share\x.exe", s => true), "UNC never checked");

                // A file with an embedded Microsoft signature (dotnet.exe from the SDK that builds GamesHub).
                string signed = FindSignedExe();
                if (signed == null) { Console.WriteLine("  (no embedded-signed dotnet.exe found; signed-file checks skipped)"); return; }
                string copy = Path.Combine(dir, "copy.exe");
                File.Copy(signed, copy);
                Assert.True(Authenticode.IsSignedBy(copy, s => s.IndexOf("Microsoft", StringComparison.OrdinalIgnoreCase) >= 0), "valid Microsoft signature");
                Assert.False(Authenticode.IsRiotSigned(copy), "valid signature, but not Riot Games");

                byte[] bytes = File.ReadAllBytes(copy);
                bytes[bytes.Length / 2] ^= 0xFF;                                  // same size, new mtime
                File.WriteAllBytes(copy, bytes);
                File.SetLastWriteTimeUtc(copy, DateTime.UtcNow.AddMinutes(1));
                Assert.False(Authenticode.IsSignedBy(copy, s => true), "tampered file (cache invalidated by mtime)");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch (Exception ex) { Console.WriteLine("cleanup: " + ex.Message); }
            }
        }

        private static string FindSignedExe()
        {
            string pf = Environment.GetEnvironmentVariable("ProgramW6432") ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            return new[] { Path.Combine(pf, "dotnet", "dotnet.exe") }.FirstOrDefault(p => File.Exists(p) && Authenticode.IsSignedBy(p, s => true));
        }
    }
}
