// OWNER: DIST agent. Uninstall.exe entry point.
//   Uninstall.exe               confirmation window (optionally deletes %LOCALAPPDATA%\GamesHub)
//   Uninstall.exe /silent       unattended; add /purge to also delete user data
// Result: %TEMP%\GamesHub\uninstall-result.txt, log: %TEMP%\GamesHub\uninstall.log
using System;
using System.Linq;
using System.Windows.Forms;

namespace GamesHub.Installer
{
    internal static class UninstallProgram
    {
        public const string ResultFile = "uninstall-result.txt";

        [STAThread]
        private static int Main(string[] args)
        {
            InstallerLog.Init("uninstall.log");
            string[] flags = args.Select(a => (a ?? "").Trim().TrimStart('/', '-').ToLowerInvariant()).ToArray();
            bool silent = flags.Contains("silent") || flags.Contains("s") || flags.Contains("quiet");
            bool purge = flags.Contains("purge");

            if (silent)
            {
                var engine = new UninstallEngine();
                try
                {
                    engine.Run(purge);
                    InstallerLog.WriteResult(ResultFile, "OK");
                    engine.ScheduleSelfDelete();
                    return 0;
                }
                catch (Exception ex)
                {
                    InstallerLog.Error("Silent uninstall failed", ex);
                    InstallerLog.WriteResult(ResultFile, "ERRO:" + ex.Message);
                    return 1;
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var form = new UninstallForm();
            Application.Run(form);
            if (form.ExitCode == 0) form.Engine.ScheduleSelfDelete();
            return form.ExitCode;
        }
    }
}
