// OWNER: INSTALL agent. Forbidden roots, recursive size, cache + invalidation, dedupe.
using System;
using System.IO;
using System.Threading.Tasks;

namespace GamesHub.Tests
{
    public static class InstallSizeTests
    {
        public static void TestForbiddenRoots()
        {
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            Assert.True(InstallPaths.IsForbiddenRoot(@"C:\"), "drive root");
            Assert.True(InstallPaths.IsForbiddenRoot(@"D:"), "bare drive");
            Assert.True(InstallPaths.IsForbiddenRoot(win), "windows");
            Assert.True(InstallPaths.IsForbiddenRoot(Path.Combine(win, "System32")), "system32");
            Assert.True(InstallPaths.IsForbiddenRoot(pf), "program files");
            Assert.True(InstallPaths.IsForbiddenRoot(profile), "profile");
            Assert.True(InstallPaths.IsForbiddenRoot(Path.GetDirectoryName(profile)), "users root");
            Assert.True(InstallPaths.IsForbiddenRoot(@"F:\SteamLibrary\steamapps\common"), "steam common");
            Assert.True(InstallPaths.IsForbiddenRoot(@"C:\Program Files\Epic Games\"), "epic root");
            Assert.True(InstallPaths.IsForbiddenRoot(@"\\server\share"), "unc share root");
            Assert.True(InstallPaths.IsForbiddenRoot(""), "empty");
            Assert.False(InstallPaths.IsForbiddenRoot(@"F:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive"), "game");
            Assert.False(InstallPaths.IsForbiddenRoot(Path.Combine(pf, "Epic Games", "Fortnite")), "epic game");
            Assert.False(InstallPaths.IsForbiddenRoot(@"D:\Jogos\TEKKEN 7"), "custom game");
        }

        public static void TestDirOfGame()
        {
            Assert.Equal(@"D:\G\Foo", InstallSizeService.DirOf(new Game { InstallDir = @"D:\G\Foo\" }));
            Assert.Equal(@"D:\G\Bar", InstallSizeService.DirOf(new Game { Exe = @"D:\G\Bar\bar.exe" }));
            Assert.Equal("", InstallSizeService.DirOf(new Game()));
        }

        public static void TestComputeSizeSkipsJunctionsAndCountsNested()
        {
            string d = InstallHealthTests.NewTempDir();
            try
            {
                File.WriteAllBytes(Path.Combine(d, "a.bin"), new byte[1000]);
                Directory.CreateDirectory(Path.Combine(d, "sub", "deep"));
                File.WriteAllBytes(Path.Combine(d, "sub", "b.bin"), new byte[234]);
                File.WriteAllBytes(Path.Combine(d, "sub", "deep", "c.bin"), new byte[66]);
                Assert.Equal(1300L, InstallSizeService.ComputeSize(d));

                // Junction pointing back at the tree must not be followed (no double counting / loops).
                string junction = Path.Combine(d, "loop");
                var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c mklink /J \"" + junction + "\" \"" + Path.Combine(d, "sub") + "\"")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
                using (var p = System.Diagnostics.Process.Start(psi)) { p.StandardOutput.ReadToEnd(); p.WaitForExit(); }
                Assert.True(Directory.Exists(junction), "junction created");
                Assert.Equal(1300L, InstallSizeService.ComputeSize(d), "junction skipped");
                Directory.Delete(junction, false); // removes the link only
            }
            finally { InstallHealthTests.Cleanup(d); }
        }

        public static void TestCacheHitAndInvalidation()
        {
            string d = InstallHealthTests.NewTempDir();
            string cacheDir = InstallHealthTests.NewTempDir();
            try
            {
                string game = Path.Combine(d, "MyGame");
                Directory.CreateDirectory(game);
                File.WriteAllBytes(Path.Combine(game, "a.bin"), new byte[500]);
                string cacheFile = Path.Combine(cacheDir, "sizes.json");
                var insp = new InstallInspector(cacheFile);
                var g = new Game { Id = "x", InstallDir = game };

                Assert.Equal(-1L, insp.GetCachedSizeBytes(g), "not cached yet");
                Task<long> t1 = insp.GetSizeBytesAsync(g), t2 = insp.GetSizeBytesAsync(g);
                Assert.Equal(500L, t1.Result);
                Assert.Equal(500L, t2.Result);
                Assert.Equal(500L, insp.GetCachedSizeBytes(g), "cached");
                Assert.True(File.Exists(cacheFile), "persisted");
                Assert.Equal(500L, new InstallInspector(cacheFile).GetCachedSizeBytes(g), "reloaded from disk");

                // Adding a top-level file changes the dir's LastWriteTime → cache invalid.
                File.WriteAllBytes(Path.Combine(game, "b.bin"), new byte[100]);
                Directory.SetLastWriteTimeUtc(game, DateTime.UtcNow.AddMinutes(1));
                Assert.Equal(-1L, insp.GetCachedSizeBytes(g), "invalidated");
                Assert.Equal(600L, insp.GetSizeBytesAsync(g).Result);
            }
            finally { InstallHealthTests.Cleanup(d); InstallHealthTests.Cleanup(cacheDir); }
        }

        public static void TestCacheExpiresAfterSevenDays()
        {
            string d = InstallHealthTests.NewTempDir();
            string cacheDir = InstallHealthTests.NewTempDir();
            try
            {
                string cacheFile = Path.Combine(cacheDir, "sizes.json");
                string key = InstallPaths.Key(d);
                var old = new System.Collections.Generic.Dictionary<string, SizeCacheEntry>
                {
                    [key] = new SizeCacheEntry { Dir = key, Bytes = 42, DirWriteUtcTicks = Directory.GetLastWriteTimeUtc(d).Ticks, ComputedUtcTicks = DateTime.UtcNow.AddDays(-8).Ticks }
                };
                Json.Save(cacheFile, old);
                Assert.Equal(-1L, new InstallSizeService(cacheFile).GetCached(d), "expired");
                old[key].ComputedUtcTicks = DateTime.UtcNow.AddDays(-1).Ticks;
                Json.Save(cacheFile, old);
                Assert.Equal(42L, new InstallSizeService(cacheFile).GetCached(d), "fresh");
            }
            finally { InstallHealthTests.Cleanup(d); InstallHealthTests.Cleanup(cacheDir); }
        }

        public static void TestForbiddenAndMissingDirsReturnMinusOne()
        {
            string cacheDir = InstallHealthTests.NewTempDir();
            try
            {
                var insp = new InstallInspector(Path.Combine(cacheDir, "sizes.json"));
                Assert.Equal(-1L, insp.GetSizeBytesAsync(new Game { InstallDir = @"C:\" }).Result);
                Assert.Equal(-1L, insp.GetSizeBytesAsync(new Game { Exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe") }).Result);
                Assert.Equal(-1L, insp.GetSizeBytesAsync(new Game { InstallDir = Path.Combine(cacheDir, "missing") }).Result);
                Assert.Equal(-1L, insp.GetSizeBytesAsync(new Game()).Result);
            }
            finally { InstallHealthTests.Cleanup(cacheDir); }
        }

        public static void TestGetDrivesListsSystemDrive()
        {
            var drives = new InstallInspector(Path.Combine(Path.GetTempPath(), "gameshub-install-tests", "unused.json")).GetDrives();
            string sys = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            Assert.True(drives.Exists(x => string.Equals(x.Name, sys, StringComparison.OrdinalIgnoreCase) && x.TotalBytes > 0 && x.FreeBytes >= 0), "system drive");
        }
    }
}
