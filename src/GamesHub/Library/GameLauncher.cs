using System;
using System.Diagnostics;
using System.IO;

namespace GamesHub
{
    internal static class GameLauncher
    {
        /// <summary>Pure: how to start the game. lnk = parsed .lnk data (only used for folder .lnk with LaunchArgs).
        /// sourceArgs = arguments required by an imported game's source (e.g. Riot "--launch-product=...").</summary>
        public static ProcessStartInfo BuildStartInfo(Game g, ShortcutInfo lnk, string sourceArgs = "")
        {
            string args = (g.LaunchArgs ?? "").Trim();
            if (g.Ext == ".url") return Shell(g.LaunchTarget.Length > 0 ? g.LaunchTarget : g.FilePath);
            if (g.Source != GameRules.SourceFolder)
            {
                // URIs (steam://, com.epicgames.launcher://...) are opened as is; executables get source + user args.
                if (GameRules.IsUri(g.LaunchTarget)) return Shell(g.LaunchTarget);
                var imp = Shell(g.LaunchTarget);
                imp.Arguments = ((sourceArgs ?? "") + " " + args).Trim();
                imp.WorkingDirectory = SafeDir(g.LaunchTarget);
                return imp;
            }

            if (g.Ext == ".exe")
            {
                var psi = Shell(g.FilePath);
                psi.Arguments = args;
                psi.WorkingDirectory = Path.GetDirectoryName(g.FilePath) ?? "";
                return psi;
            }

            // .lnk: without extra args let the shell run it (keeps run-as, window style, etc.).
            string target = lnk?.Target ?? "";
            if (args.Length == 0 || target.Length == 0 || !File.Exists(target)) return Shell(g.FilePath);
            var p = Shell(target);
            p.Arguments = ((lnk.Arguments ?? "") + " " + args).Trim();
            p.WorkingDirectory = !string.IsNullOrEmpty(lnk.WorkingDir) && Directory.Exists(lnk.WorkingDir)
                ? lnk.WorkingDir : Path.GetDirectoryName(target) ?? "";
            return p;
        }

        private static string SafeDir(string path)
        {
            try { return Path.GetDirectoryName(path) ?? ""; }
            catch (ArgumentException ex) { Log.Warn("Bad launch path: " + path, ex); return ""; }
        }

        private static ProcessStartInfo Shell(string fileName) => new ProcessStartInfo(fileName) { UseShellExecute = true };

        public static void Start(ProcessStartInfo psi)
        {
            using (Process.Start(psi)) { }
        }
    }
}
