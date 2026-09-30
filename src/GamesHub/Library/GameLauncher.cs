using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace GamesHub
{
    internal static class GameLauncher
    {
        /// <summary>Pure: how to start the game. lnk = parsed .lnk data (only used for folder .lnk with LaunchArgs).
        /// sourceArgs = arguments required by an imported game's source (e.g. Riot "--launch-product=...").</summary>
        public static ProcessStartInfo BuildStartInfo(Game g, ShortcutInfo lnk, string sourceArgs = "")
        {
            string args = (g.LaunchArgs ?? "").Trim();
            if (g.Ext == ".url") return Shell(UrlLaunchTarget(g.LaunchTarget, g.FilePath));
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
            if (lnk == null || args.Length == 0 || string.IsNullOrEmpty(lnk.Target) || !File.Exists(lnk.Target)) return Shell(g.FilePath);
            string target = lnk.Target;
            var p = Shell(target);
            p.Arguments = ((lnk.Arguments ?? "") + " " + args).Trim();
            p.WorkingDirectory = !string.IsNullOrEmpty(lnk.WorkingDir) && Directory.Exists(lnk.WorkingDir)
                ? lnk.WorkingDir : Path.GetDirectoryName(target) ?? "";
            return p;
        }

        // Launcher protocols a .url may start directly. Anything else (http(s), file:, search-ms:, ms-*, UNC paths,
        // unknown schemes) is not run from the raw string: the .url file itself goes to the shell, which applies
        // Mark-of-the-Web / SmartScreen and the user's own handler associations.
        private static readonly HashSet<string> UrlSchemes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "steam", "com.epicgames.launcher", "riotclient", "battlenet", "blizzard", "origin", "origin2", "ealink",
            "uplay", "goggalaxy", "hydralauncher",
        };

        /// <summary>Pure: what to hand the shell for a .url game. The URL itself (trimmed) when it is a well-formed
        /// absolute URI of an allowed launcher scheme, without spaces, quotes or control characters; otherwise the
        /// .url file path. The original text is kept (not Uri.AbsoluteUri) because launchers are picky about case
        /// and trailing slashes ("battlenet://Pro" must not become "battlenet://pro/").</summary>
        public static string UrlLaunchTarget(string url, string urlFile)
        {
            string u = (url ?? "").Trim();
            bool clean = u.Length > 0 && u.Length <= 2048 && u.All(c => !char.IsWhiteSpace(c) && !char.IsControl(c) && c != '"');
            if (clean && Uri.TryCreate(u, UriKind.Absolute, out Uri uri) && !uri.IsUnc && !uri.IsFile
                && UrlSchemes.Contains(uri.Scheme) && u.StartsWith(uri.Scheme + ":", StringComparison.OrdinalIgnoreCase)
                && u.TrimEnd('/').Length > uri.Scheme.Length + 1)
                return u;
            return urlFile ?? "";
        }

        /// <summary>Pure: a pt-BR error when the start info would run a batch script (.bat/.cmd) with arguments that
        /// cmd.exe would interpret (&amp; | &lt; &gt; ^ % "); null when it is safe to start.</summary>
        public static string UnsafeBatchArgs(ProcessStartInfo psi, params string[] addedArgs)
        {
            if (psi == null) return null;
            string file = (psi.FileName ?? "").Trim().Trim('"');
            if (!file.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) && !file.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
                return null;
            foreach (string a in addedArgs ?? new string[0])
            {
                if ((a ?? "").IndexOfAny(BatchMetaChars) >= 0)
                    return "Os argumentos de inicialização contêm caracteres não permitidos em scripts .bat/.cmd (& | < > ^ % \"). Remova-os e tente novamente.";
            }
            return null;
        }

        private static readonly char[] BatchMetaChars = { '&', '|', '<', '>', '^', '%', '"' };

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
