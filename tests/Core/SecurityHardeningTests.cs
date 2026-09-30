using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace GamesHub.Tests
{
    public static class LogCleanTests
    {
        public static void TestEscapesLineBreaksAndControlCharacters()
        {
            string s = Log.Clean("a\r\nFAKE 2020-01-01 ERROR forged\tb\u0007c\u2028d", "");
            Assert.False(s.Contains("\r") || s.Contains("\n") || s.Contains("\t") || s.Contains("\u0007") || s.Contains("\u2028"), "no raw control chars: " + s);
            Assert.Equal("a\\r\\nFAKE 2020-01-01 ERROR forged\\tb\\u0007c\\u2028d", s);
            Assert.Equal("", Log.Clean(null, ""));
        }

        public static void TestRedactsUserProfileAndSteamIds()
        {
            const string profile = @"C:\Users\Bob";
            Assert.Equal(@"open %USERPROFILE%\Documents\x.sav", Log.Clean(@"open c:\users\bob\Documents\x.sav", profile));
            Assert.Equal("%USERPROFILE%/AppData/y", Log.Clean("C:/Users/Bob/AppData/y", profile), "forward slashes");
            Assert.Equal("dir %USERPROFILE%", Log.Clean(@"dir C:\Users\Bob", profile), "at the end");
            string sibling = profile + @"by\x";   // another user's folder that merely starts with the same name
            Assert.Equal(sibling, Log.Clean(sibling, profile), "only a whole folder name");
            Assert.Equal(@"D:\Steam\userdata\<id>\730\remote", Log.Clean(@"D:\Steam\userdata\123456789\730\remote", profile));
            Assert.Equal("steam/userdata/<id>/x", Log.Clean("steam/userdata/42/x", profile));
        }

        public static void TestEscapeControlKeepsPersonalData()
        {
            // Used for native prompts: escapes, but shows the command exactly (no redaction).
            Assert.Equal(@"C:\Users\Bob\userdata\1\r", Log.EscapeControl(@"C:\Users\Bob\userdata\1" + "\r"));
        }
    }

    public static class JsonFileSafetyTests
    {
        private sealed class Doc { public string Name = ""; public int N; }

        private static string NewDir()
        {
            string d = Path.Combine(Path.GetTempPath(), "gameshub-json-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(d);
            return d;
        }

        public static void TestCorruptFileIsQuarantinedAndBackupUsed()
        {
            string dir = NewDir();
            try
            {
                string file = Path.Combine(dir, "settings.json");
                Json.Save(file, new Doc { Name = "first", N = 1 });
                Json.Save(file, new Doc { Name = "second", N = 2 });          // first → settings.json.bak
                Assert.True(File.Exists(file + ".bak"), "save keeps the previous version");
                Assert.Equal("first", Json.Load(file + ".bak", new Doc()).Name);
                Assert.False(File.Exists(file + ".tmp"), "temp file swapped in");

                File.WriteAllText(file, "{ \"Name\": \"bro", Encoding.UTF8);  // truncated by a crash
                Doc d = Json.Load(file, new Doc { Name = "default" });
                Assert.Equal("first", d.Name, "falls back to the backup");
                Assert.False(File.Exists(file), "corrupt file moved aside before any save");
                string[] corrupt = Directory.GetFiles(dir, "settings.json.corrupt-*");
                Assert.Equal(1, corrupt.Length);
                Assert.Equal("{ \"Name\": \"bro", File.ReadAllText(corrupt[0]), "corrupt content preserved");
            }
            finally { Directory.Delete(dir, true); }
        }

        public static void TestCorruptWithoutBackupReturnsFallback()
        {
            string dir = NewDir();
            try
            {
                string file = Path.Combine(dir, "library.json");
                File.WriteAllText(file, "   ");
                Assert.Equal("default", Json.Load(file, new Doc { Name = "default" }).Name, "empty file is corrupt too");
                Assert.Equal(1, Directory.GetFiles(dir, "library.json.corrupt-*").Length);
                Assert.Equal("default", Json.Load(Path.Combine(dir, "missing.json"), new Doc { Name = "default" }).Name);
                Assert.Equal(1, Directory.GetFiles(dir).Length, "a missing file is not quarantined");
            }
            finally { Directory.Delete(dir, true); }
        }

        public static void TestCacheFilesSkipBackup()
        {
            string dir = Path.Combine(AppPaths.CacheDir, "json-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                string file = Path.Combine(dir, "x.json");
                Json.Save(file, new Doc { N = 1 });
                Json.Save(file, new Doc { N = 2 });
                Assert.Equal(2, Json.Load(file, new Doc()).N);
                Assert.False(File.Exists(file + ".bak"), "rebuildable caches do not keep a backup");
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }

        public static void TestPageMessagesAreLengthLimited()
        {
            var ok = Json.DeserializeMessage("{\"type\":\"cmd\",\"args\":{\"msg\":\"" + new string('a', 100000) + "\"}}") as IDictionary<string, object>;
            Assert.Equal("cmd", Json.Str(ok, "type"));
            bool threw = false;
            try { Json.DeserializeMessage("\"" + new string('a', Json.MaxMessageLength) + "\""); }
            catch (ArgumentException) { threw = true; }
            Assert.True(threw, "over 4 MB is rejected");
            // Files keep the unlimited serializer (a large library.json must still load).
            string big = "\"" + new string('b', Json.MaxMessageLength + 10) + "\"";
            Assert.Equal(Json.MaxMessageLength + 10, ((string)Json.DeserializeObject(big)).Length);
        }
    }

    public static class HostHardeningTests
    {
        public static void TestDevToolsNeedsIsolationAndExplicitOptIn()
        {
            Assert.Equal(9333, WebViewEnv.DevToolsPort(true, "1", "9333"));
            Assert.Equal(0, WebViewEnv.DevToolsPort(false, "1", "9333"), "never for the real install");
            Assert.Equal(0, WebViewEnv.DevToolsPort(true, null, "9333"), "GAMESHUB_E2E missing");
            Assert.Equal(0, WebViewEnv.DevToolsPort(true, "true", "9333"), "only the exact opt-in");
            Assert.Equal(0, WebViewEnv.DevToolsPort(true, "1", "80"), "privileged port");
            Assert.Equal(0, WebViewEnv.DevToolsPort(true, "1", "x"));
            Assert.Equal("http://127.0.0.1:9333", WebViewEnv.DevToolsOrigin(9333));
        }

        public static void TestPipeNameIsPerUserAndSession()
        {
            string a = SingleInstance.PipeNameFor("S-1-5-21-1-2-3-1001", 1, "");
            Assert.Equal("GamesHub.Activate.S-1-5-21-1-2-3-1001.s1", a);
            Assert.True(a != SingleInstance.PipeNameFor("S-1-5-21-1-2-3-1002", 1, ""), "other user");
            Assert.True(a != SingleInstance.PipeNameFor("S-1-5-21-1-2-3-1001", 2, ""), "other session");
            Assert.Equal("GamesHub.Activate.S-1-5-21-1-2-3-1001.s1.abcd", SingleInstance.PipeNameFor("S-1-5-21-1-2-3-1001", 1, ".abcd"));
            Assert.Equal("GamesHub.Activate.a_b_c.s0", SingleInstance.PipeNameFor(@"a\b c", 0, null), "no path characters");
        }

        public static void TestAutostartRefreshOnlyForInstalledCopy()
        {
            string install = @"C:\Users\Bob\AppData\Local\Programs\GamesHub";
            Assert.True(AppController.IsInstalledCopy(install + @"\GamesHub.exe", install));
            Assert.True(AppController.IsInstalledCopy(install + @"\GamesHub.exe", "\"" + install.ToUpperInvariant() + "\\\""), "quotes, case, trailing slash");
            Assert.False(AppController.IsInstalledCopy(@"C:\Users\Bob\Downloads\GamesHub\GamesHub.exe", install), "portable copy");
            Assert.False(AppController.IsInstalledCopy(install + @"\sub\GamesHub.exe", install), "subfolder");
            Assert.False(AppController.IsInstalledCopy(install + @"\GamesHub.exe", null), "not installed");
            Assert.False(AppController.IsInstalledCopy(install + @"\GamesHub.exe", "  "));
        }

        public static void TestQuickLaunchLogTextIsLimited()
        {
            Assert.Equal("", QuickLaunchController.LimitLogText(null));
            Assert.Equal("abc", QuickLaunchController.LimitLogText("abc"));
            string s = QuickLaunchController.LimitLogText(new string('x', 5000));
            Assert.Equal(QuickLaunchController.MaxLogChars + 1, s.Length);
        }

        public static void TestQuickArtUrlRejectsTraversal()
        {
            Assert.True(QuickDto.ArtUrl(@"..\..\secret.jpg") == null, "parent");
            Assert.True(QuickDto.ArtUrl("steam-730/../../x.jpg") == null, "inner parent");
            Assert.True(QuickDto.ArtUrl("C:/Windows/x.jpg") == null, "drive");
            Assert.True(QuickDto.ArtUrl("a//b.jpg") == null, "empty segment");
            Assert.True(QuickDto.ArtUrl("") == null);
            string ok = QuickDto.ArtUrl("steam-730/header.jpg");
            Assert.True(ok != null && ok.StartsWith(QuickDto.ArtBase + "steam-730/header.jpg"), ok);
        }
    }
}
