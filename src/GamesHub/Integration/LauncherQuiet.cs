// Keeps store launchers (Steam, Epic, Riot, Battle.net, EA, Ubisoft, GOG, Hydra) out of the way when a game
// is started from GamesHub (setting "Abrir launchers minimizados"):
//   · Steam: if it is not running yet, it is started with -silent (tray only) before the game's steam:// link,
//     so Steam never opens its main window just to launch a game;
//   · every launcher: for a short while after the launch, windows those launchers show are minimized once each
//     (the game's own windows are never touched, and a window the user restores is left alone).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GamesHub
{
    public static class LauncherQuiet
    {
        /// <summary>Process names (without .exe) of launcher UIs whose windows may be minimized.</summary>
        public static readonly HashSet<string> LauncherProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "steam", "steamwebhelper", "EpicGamesLauncher", "EpicWebHelper", "RiotClientUx", "RiotClientServices",
            "Riot Client", "Battle.net", "EADesktop", "EABackgroundService", "Origin", "UbisoftConnect", "upc",
            "GalaxyClient", "Hydra",
        };

        private static readonly TimeSpan WatchFor = TimeSpan.FromSeconds(45);

        /// <summary>True when the launch target goes through Steam (steam:// URI or a Steam shortcut).</summary>
        public static bool IsSteamLaunch(Game g)
            => (g.LaunchTarget ?? "").StartsWith("steam://", StringComparison.OrdinalIgnoreCase)
               || string.Equals(g.Platform, "Steam", StringComparison.OrdinalIgnoreCase) && (g.LaunchTarget ?? "").IndexOf("steam://", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>Before launching: start Steam silently (tray only) when a Steam game is launched and Steam is not
        /// running. Waits until Steam is up (max ~20 s) so the steam:// link is handled by the silent instance.</summary>
        public static async Task PrepareAsync(Game g)
        {
            if (!IsSteamLaunch(g) || IsRunning("steam")) return;
            string exe = SteamExe();
            if (exe.Length == 0) return;
            try
            {
                using (Process.Start(new ProcessStartInfo(exe, "-silent") { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) })) { }
                Log.Info("Launchers: started Steam silently for " + g.Id);
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException || ex is IOException)
            {
                Log.Warn("Launchers: could not start Steam silently", ex);
                return;
            }
            // Steam accepts steam:// links once its client window host is up.
            for (int i = 0; i < 40 && !IsRunning("steamwebhelper"); i++) await Task.Delay(500).ConfigureAwait(false);
            await Task.Delay(1500).ConfigureAwait(false);
        }

        /// <summary>After launching: minimize launcher windows that pop up during the next seconds (each once).</summary>
        public static void MinimizeLauncherWindowsSoon()
        {
            var handled = new HashSet<IntPtr>();
            var deadline = DateTime.UtcNow + WatchFor;
            // Windows already visible before the launch belong to the user's session: leave them alone.
            foreach (IntPtr h in LauncherWindows()) handled.Add(h);
            Task.Run(async () =>
            {
                try
                {
                    while (DateTime.UtcNow < deadline)
                    {
                        await Task.Delay(400).ConfigureAwait(false);
                        foreach (IntPtr h in LauncherWindows())
                        {
                            if (!handled.Add(h) || IsIconic(h)) continue;
                            ShowWindowAsync(h, SW_SHOWMINNOACTIVE);
                            Log.Info("Launchers: minimized window \"" + Title(h) + "\"");
                        }
                    }
                }
                // Resilience boundary: fire-and-forget background watcher; a failure must never affect the game.
                catch (Exception ex)
                {
                    Log.Warn("Launchers: window watcher stopped", ex);
                }
            });
        }

        // ------------------------------------------------------------------ helpers

        private static bool IsRunning(string name)
        {
            Process[] ps = Process.GetProcessesByName(name);
            foreach (Process p in ps) p.Dispose();
            return ps.Length > 0;
        }

        private static string SteamExe()
        {
            string dir = SteamSource.FindSteamPath();
            if (string.IsNullOrEmpty(dir)) return "";
            string exe = Path.Combine(dir.Replace('/', '\\'), "steam.exe");
            return File.Exists(exe) ? exe : "";
        }

        /// <summary>Visible, titled top-level windows owned by launcher processes.</summary>
        private static List<IntPtr> LauncherWindows()
        {
            var pids = new HashSet<uint>();
            foreach (Process p in Process.GetProcesses())
            {
                using (p)
                {
                    try { if (LauncherProcesses.Contains(p.ProcessName)) pids.Add((uint)p.Id); }
                    catch (InvalidOperationException) { /* exited while enumerating */ }
                }
            }
            var result = new List<IntPtr>();
            if (pids.Count == 0) return result;
            EnumWindows((h, _) =>
            {
                if (!IsWindowVisible(h) || GetWindow(h, GW_OWNER) != IntPtr.Zero || GetWindowTextLength(h) == 0) return true;
                GetWindowThreadProcessId(h, out uint pid);
                if (pids.Contains(pid)) result.Add(h);
                return true;
            }, IntPtr.Zero);
            return result;
        }

        private static string Title(IntPtr h)
        {
            var sb = new StringBuilder(256);
            GetWindowText(h, sb, sb.Capacity);
            return sb.ToString();
        }

        private const int SW_SHOWMINNOACTIVE = 7;
        private const uint GW_OWNER = 4;
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);
        [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder s, int max);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr hWnd, int cmd);
    }
}
