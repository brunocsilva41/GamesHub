// OWNER: DIST agent. Finds and closes running GamesHub / GamesLounge (v1) processes of a given install.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace GamesHub.Installer
{
    internal static class AppProcesses
    {
        private static readonly string[] Names = { "GamesHub", "GamesLounge" };

        /// <summary>Running app processes whose image lives in installDir (or whose path can't be read).</summary>
        public static List<Process> Find(string installDir)
        {
            var list = new List<Process>();
            int self = Process.GetCurrentProcess().Id;
            foreach (string name in Names)
            {
                foreach (Process p in Process.GetProcessesByName(name))
                {
                    if (p.Id == self) { p.Dispose(); continue; }
                    string img = ImagePath(p);
                    if (img == null || PathSafety.IsInside(img, installDir)) list.Add(p);
                    else p.Dispose();
                }
            }
            return list;
        }

        public static bool AnyRunning(string installDir)
        {
            List<Process> l = Find(installDir);
            bool any = l.Count > 0;
            foreach (Process p in l) p.Dispose();
            return any;
        }

        /// <summary>Waits up to timeoutMs for all app processes of installDir to exit on their own.</summary>
        public static bool WaitForExit(string installDir, int timeoutMs)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (!AnyRunning(installDir)) return true;
                Thread.Sleep(250);
            }
            return !AnyRunning(installDir);
        }

        /// <summary>
        /// Asks the app to close (WM_CLOSE to its main window, then "GamesHub.exe --quit" which asks the running
        /// single instance to exit even when it lives in the tray). Processes still alive after graceMs are killed only
        /// if confirmKill returns true. Returns true when nothing of installDir is running anymore.
        /// </summary>
        public static bool CloseAll(string installDir, int graceMs, Func<bool> confirmKill)
        {
            List<Process> procs = Find(installDir);
            if (procs.Count == 0) return true;
            InstallerLog.Info("Closing " + procs.Count + " running process(es): " + string.Join(", ", procs.Select(p => p.ProcessName + "#" + p.Id)));

            bool hasV2 = false;
            foreach (Process p in procs)
            {
                try
                {
                    if (p.ProcessName.Equals("GamesHub", StringComparison.OrdinalIgnoreCase)) hasV2 = true;
                    p.CloseMainWindow();
                }
                catch (Exception ex) { InstallerLog.Warn("CloseMainWindow failed for " + p.Id, ex); }
            }
            string exe = Path.Combine(installDir, Product.ExeName);
            if (hasV2 && File.Exists(exe))
            {
                try
                {
                    using (Process.Start(new ProcessStartInfo(exe, "--quit") { UseShellExecute = false, WorkingDirectory = installDir })) { }
                }
                catch (Exception ex) { InstallerLog.Warn("Could not send --quit", ex); }
            }
            foreach (Process p in procs) p.Dispose();

            if (WaitForExit(installDir, graceMs)) return true;
            if (confirmKill != null && !confirmKill()) return false;

            foreach (Process p in Find(installDir))
            {
                try
                {
                    InstallerLog.Info("Killing " + p.ProcessName + "#" + p.Id);
                    p.Kill();
                    p.WaitForExit(5000);
                }
                catch (Exception ex) { InstallerLog.Warn("Kill failed for " + p.Id, ex); }
                finally { p.Dispose(); }
            }
            return WaitForExit(installDir, 3000);
        }

        private static string ImagePath(Process p)
        {
            IntPtr h = IntPtr.Zero;
            try
            {
                h = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, p.Id);
                if (h == IntPtr.Zero) return null;
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
            }
            catch (Exception ex)
            {
                InstallerLog.Warn("Cannot query image of " + p.Id, ex);
                return null;
            }
            finally { if (h != IntPtr.Zero) CloseHandle(h); }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(int access, bool inherit, int pid);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder name, ref int size);
        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr h);
    }
}
