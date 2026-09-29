//   GamesHub-Setup-x.y.z.exe                     wizard
//   /silent [/dir <path>] [/games <path>]         unattended install (+ /nodesktop /nostartmenu /autostart)
//   /update [/dir <path>]                         used by the in-app updater: waits for the app, installs, relaunches
// Result: %TEMP%\GamesHub\setup-result.txt ("OK:<dir>" | "ERRO:<msg>"), log: %TEMP%\GamesHub\setup.log
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace GamesHub.Installer
{
    internal static class SetupProgram
    {
        public const string ResultFile = "setup-result.txt";

        [STAThread]
        private static int Main(string[] args)
        {
            InstallerLog.Init("setup.log");
            SetupOptions o = ParseArgs(args);

            using (var mutex = new Mutex(true, @"Local\GamesHub.Setup", out bool first))
            {
                if (!first)
                {
                    InstallerLog.Warn("Another Setup instance is running");
                    if (o.Mode == SetupMode.Interactive)
                        MessageBox.Show("O instalador do GamesHub já está em execução.", "GamesHub", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 3;
                }

                if (o.Help)
                {
                    MessageBox.Show("Uso: GamesHub-Setup.exe [/silent] [/dir <pasta>] [/games <pasta>] [/nodesktop] [/nostartmenu] [/autostart] [/update]",
                        "GamesHub", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                }

                if (o.Mode == SetupMode.Silent) return RunSilent(o);

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                if (o.Mode == SetupMode.Update)
                {
                    var f = new UpdateForm(o);
                    Application.Run(f);
                    return f.ExitCode;
                }
                var wizard = new SetupForm(o);
                Application.Run(wizard);
                return wizard.ExitCode;
            }
        }

        private static int RunSilent(SetupOptions o)
        {
            try
            {
                SetupResult r = new SetupEngine().Run(o);
                InstallerLog.WriteResult(ResultFile, "OK:" + r.InstallDir);
                return 0;
            }
            catch (Exception ex)
            {
                InstallerLog.Error("Silent install failed", ex);
                InstallerLog.WriteResult(ResultFile, "ERRO:" + ex.Message);
                return 1;
            }
        }

        internal static SetupOptions ParseArgs(string[] args)
        {
            var o = new SetupOptions();
            string dir = null, games = null;
            for (int i = 0; i < args.Length; i++)
            {
                string a = (args[i] ?? "").Trim();
                string key = a.TrimStart('/', '-').ToLowerInvariant(), val = null;
                int eq = key.IndexOfAny(new[] { '=', ':' });
                if (eq > 1) { val = a.Substring(a.Length - (key.Length - eq - 1)); key = key.Substring(0, eq); }
                string Next() => val ?? (i + 1 < args.Length ? args[++i] : null);
                switch (key)
                {
                    case "silent": case "s": case "quiet": o.Mode = o.Mode == SetupMode.Update ? SetupMode.Update : SetupMode.Silent; break;
                    case "update": o.Mode = SetupMode.Update; break;
                    case "dir": case "d": dir = Next(); break;
                    case "games": case "g": games = Next(); break;
                    case "nodesktop": o.DesktopShortcut = false; break;
                    case "nostartmenu": o.StartMenuShortcut = false; break;
                    case "autostart": o.StartWithWindows = true; break;
                    case "?": case "h": case "help": o.Help = true; break;
                    default: InstallerLog.Warn("Unknown argument: " + a); break;
                }
            }

            o.InstallDir = !string.IsNullOrWhiteSpace(dir) ? dir : Product.RegisteredInstallDir() ?? Product.DefaultInstallDir;
            if (o.Mode == SetupMode.Update)
            {
                // keep the user's shortcut choices: only existing shortcuts are re-pointed
                o.DesktopShortcut = o.StartMenuShortcut = false;
                o.GamesDir = string.IsNullOrWhiteSpace(games) ? null : games;
            }
            else
            {
                o.GamesDir = !string.IsNullOrWhiteSpace(games) ? games : DefaultGamesDir(o.InstallDir);
            }
            return o;
        }

        /// <summary>settings.json gamesDir → v1 config.json → Desktop\jogos.</summary>
        internal static string DefaultGamesDir(string installDir)
        {
            try
            {
                if (File.Exists(Product.SettingsFile)
                    && new System.Web.Script.Serialization.JavaScriptSerializer().DeserializeObject(File.ReadAllText(Product.SettingsFile))
                        is System.Collections.Generic.Dictionary<string, object> d
                    && d.TryGetValue("gamesDir", out object g) && g is string gs && gs.Length > 0)
                    return gs;
            }
            catch (Exception ex) { InstallerLog.Warn("Cannot read settings.json", ex); }
            return ExistingInstall.Detect(installDir).LegacyGamesDir ?? Product.DefaultGamesDir;
        }

        /// <summary>Starts the installed app (normal user token, no inherited console).</summary>
        internal static void LaunchApp(string exe)
        {
            try
            {
                if (!File.Exists(exe)) { InstallerLog.Warn("Cannot launch, missing: " + exe); return; }
                using (Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe) })) { }
                InstallerLog.Info("Launched " + exe);
            }
            catch (Exception ex) { InstallerLog.Warn("Launch failed", ex); }
        }
    }
}
