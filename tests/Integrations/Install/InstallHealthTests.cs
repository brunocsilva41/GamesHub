using System;
using System.IO;

namespace GamesHub.Tests
{
    public static class InstallHealthTests
    {
        internal static string NewTempDir()
        {
            string d = Path.Combine(Path.GetTempPath(), "gameshub-install-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(d);
            return d;
        }

        internal static void Cleanup(string d)
        {
            try { Directory.Delete(d, true); } catch (Exception ex) { Console.WriteLine("cleanup failed: " + ex.Message); }
        }

        internal static char MissingDriveLetter()
        {
            var used = new System.Collections.Generic.HashSet<char>();
            foreach (DriveInfo di in DriveInfo.GetDrives()) used.Add(char.ToUpperInvariant(di.Name[0]));
            for (char c = 'Z'; c >= 'D'; c--) if (!used.Contains(c)) return c;
            throw new Exception("no free drive letter");
        }

        private static void CreateLnk(string lnk, string target)
        {
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            dynamic shell = Activator.CreateInstance(t);
            dynamic sc = shell.CreateShortcut(lnk);
            sc.TargetPath = target;
            sc.Save();
        }

        private static Game FolderGame(string file) => new Game
        { Id = "folder:" + Path.GetFileName(file).ToLowerInvariant(), Name = Path.GetFileNameWithoutExtension(file), FilePath = file, Ext = Path.GetExtension(file).ToLowerInvariant(), LaunchTarget = file };

        public static void TestMissingShortcutFileIsBroken()
        {
            string d = NewTempDir();
            try
            {
                InstallHealth h = InstallHealthCheck.Check(FolderGame(Path.Combine(d, "Gone.lnk")));
                Assert.True(h.Broken);
                Assert.True(h.Reason.Contains("atalho"), h.Reason);
                Assert.True(InstallHealthCheck.Check(FolderGame(Path.Combine(d, "Gone.exe"))).Reason.Contains("executável"));
            }
            finally { Cleanup(d); }
        }

        public static void TestExeGamePresentIsOk()
        {
            string d = NewTempDir();
            try
            {
                string exe = Path.Combine(d, "Game.exe");
                File.WriteAllText(exe, "x");
                Assert.False(InstallHealthCheck.Check(FolderGame(exe)).Broken);
            }
            finally { Cleanup(d); }
        }

        public static void TestLnkTargetExistsAndMissing()
        {
            string d = NewTempDir();
            try
            {
                string exe = Path.Combine(d, "Real Game.exe");
                File.WriteAllText(exe, "x");
                string lnk = Path.Combine(d, "Real.lnk");
                CreateLnk(lnk, exe);
                Assert.Equal(exe.ToLowerInvariant(), InstallShellLink.ReadLnkTarget(lnk).ToLowerInvariant(), "target");
                Assert.False(InstallHealthCheck.Check(FolderGame(lnk)).Broken, "existing target");

                File.Delete(exe);
                InstallHealth h = InstallHealthCheck.Check(FolderGame(lnk));
                Assert.True(h.Broken, "deleted target");
                Assert.True(h.Reason.Contains("destino") && h.Reason.Contains("Real Game.exe"), h.Reason);
            }
            finally { Cleanup(d); }
        }

        public static void TestLnkToDirectoryIsOk()
        {
            string d = NewTempDir();
            try
            {
                string sub = Path.Combine(d, "GameFolder");
                Directory.CreateDirectory(sub);
                string lnk = Path.Combine(d, "Folder.lnk");
                CreateLnk(lnk, sub);
                Assert.False(InstallHealthCheck.Check(FolderGame(lnk)).Broken);
            }
            finally { Cleanup(d); }
        }

        public static void TestLnkOnMissingDriveGivesDriveReason()
        {
            string d = NewTempDir();
            try
            {
                char l = MissingDriveLetter();
                string lnk = Path.Combine(d, "Ext.lnk");
                CreateLnk(lnk, l + @":\Games\Foo\foo.exe");
                InstallHealth h = InstallHealthCheck.Check(FolderGame(lnk));
                Assert.True(h.Broken);
                Assert.Equal("O disco " + l + @":\ não está conectado.", h.Reason);
            }
            finally { Cleanup(d); }
        }

        public static void TestUrlFileAndUriTargets()
        {
            string d = NewTempDir();
            try
            {
                string steamUrl = Path.Combine(d, "CS.url");
                File.WriteAllText(steamUrl, "[InternetShortcut]\r\nURL=steam://rungameid/730\r\n");
                Assert.False(InstallHealthCheck.Check(FolderGame(steamUrl)).Broken, "steam url");

                string exe = Path.Combine(d, "x.exe");
                File.WriteAllText(exe, "x");
                string fileUrl = Path.Combine(d, "File.url");
                File.WriteAllText(fileUrl, "[InternetShortcut]\r\nURL=" + new Uri(exe).AbsoluteUri + "\r\n");
                Assert.False(InstallHealthCheck.Check(FolderGame(fileUrl)).Broken, "file url exists");
                File.Delete(exe);
                Assert.True(InstallHealthCheck.Check(FolderGame(fileUrl)).Broken, "file url missing");
            }
            finally { Cleanup(d); }
        }

        public static void TestImportedGames()
        {
            string d = NewTempDir();
            try
            {
                var ok = new Game { Id = "steam:1", Source = "steam", InstallDir = d, LaunchTarget = "steam://rungameid/1" };
                Assert.False(InstallHealthCheck.Check(ok).Broken);
                var gone = new Game { Id = "steam:2", Source = "steam", InstallDir = Path.Combine(d, "nope"), LaunchTarget = "steam://rungameid/2" };
                InstallHealth h = InstallHealthCheck.Check(gone);
                Assert.True(h.Broken);
                Assert.True(h.Reason.StartsWith("A pasta de instalação"), h.Reason);
                var uriOnly = new Game { Id = "epic:x", Source = "epic", LaunchTarget = "com.epicgames.launcher://apps/x?action=launch" };
                Assert.False(InstallHealthCheck.Check(uriOnly).Broken);
                var bnet = new Game { Id = "x", Source = "folder", LaunchTarget = "battlenet://Pro" };
                Assert.False(InstallHealthCheck.Check(bnet).Broken);
                char l = MissingDriveLetter();
                var onMissingDrive = new Game { Id = "epic:y", Source = "epic", InstallDir = l + @":\Epic\Y" };
                Assert.Equal("O disco " + l + @":\ não está conectado.", InstallHealthCheck.Check(onMissingDrive).Reason);
            }
            finally { Cleanup(d); }
        }
    }
}
