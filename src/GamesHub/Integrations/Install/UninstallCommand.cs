using System;
using System.IO;
using System.Text.RegularExpressions;

namespace GamesHub
{
    public sealed class ParsedCommand
    {
        public string File = "";
        public string Args = "";
    }

    public static class UninstallCommand
    {
        /// <summary>Splits an UninstallString. fileExists lets tests fake the file system (default File.Exists).
        /// MsiExec "/I{GUID}" (maintenance dialog) becomes "/X{GUID}" (remove); nothing is ever made quiet.</summary>
        public static ParsedCommand Parse(string command, Func<string, bool> fileExists = null)
        {
            fileExists = fileExists ?? File.Exists;
            var r = new ParsedCommand();
            string s = Environment.ExpandEnvironmentVariables((command ?? "").Trim());
            if (s.Length == 0) return r;

            if (s[0] == '"')
            {
                int end = s.IndexOf('"', 1);
                if (end < 0) { r.File = s.Trim('"'); }
                else { r.File = s.Substring(1, end - 1); r.Args = s.Substring(end + 1).Trim(); }
            }
            else if (SafeExists(fileExists, s))
            {
                r.File = s;
            }
            else
            {
                // Unquoted path with spaces: prefer the shortest prefix (split at spaces) that exists,
                // else the shortest prefix ending in .exe, else the first token.
                string byExists = null, byExe = null;
                for (int i = 0; i <= s.Length; i++)
                {
                    if (i < s.Length && s[i] != ' ') continue;
                    string prefix = s.Substring(0, i);
                    if (prefix.Length == 0) continue;
                    if (byExists == null && InstallHealthCheck.IsLocalAbsolutePath(prefix) && SafeExists(fileExists, prefix)) byExists = prefix;
                    if (byExe == null && prefix.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) byExe = prefix;
                }
                string file = byExists ?? byExe;
                if (file == null) { int sp = s.IndexOf(' '); file = sp < 0 ? s : s.Substring(0, sp); }
                r.File = file;
                r.Args = s.Substring(file.Length).Trim();
            }

            if (IsMsiExec(r.File))
                r.Args = Regex.Replace(r.Args, @"(^|\s)[/-]I\s*(\{[0-9A-Fa-f\-]+\})", "$1/X$2", RegexOptions.IgnoreCase);
            return r;
        }

        private static bool SafeExists(Func<string, bool> exists, string path)
        {
            try { return exists(path); }
            catch (Exception ex) { Log.Warn("UninstallCommand: exists check failed for " + path, ex); return false; }
        }

        public static bool IsMsiExec(string file)
        {
            string f = (file ?? "").Trim();
            string n = f.Substring(f.LastIndexOfAny(new[] { '\\', '/' }) + 1).ToLowerInvariant();
            return n == "msiexec" || n == "msiexec.exe";
        }
    }
}
