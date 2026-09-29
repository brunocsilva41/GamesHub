using System;
using System.Collections.Generic;

namespace GamesHub
{
    /// <summary>Parsed command line: --minimized, --show, --debug, --quit, --launch &lt;gameId&gt;.</summary>
    public sealed class StartupArgs
    {
        /// <summary>Start hidden in the tray (autostart).</summary>
        public bool Minimized;
        /// <summary>Explicitly bring the window to the front.</summary>
        public bool Show;
        /// <summary>Enable dev tools, context menu and browser accelerators.</summary>
        public bool Debug;
        /// <summary>Game id to launch through the library (Jump List), or null.</summary>
        public string LaunchId;
        /// <summary>Ask the running instance to exit for real (used by Setup/Uninstall before replacing files).</summary>
        public bool Quit;

        /// <summary>True when the command line asks for nothing specific (plain start → show the window).</summary>
        public bool IsPlain => !Minimized && !Show && !Quit && LaunchId == null;

        public static StartupArgs Parse(IList<string> args)
        {
            var r = new StartupArgs();
            if (args == null) return r;
            for (int i = 0; i < args.Count; i++)
            {
                string a = (args[i] ?? "").Trim();
                if (a.Length == 0) continue;
                string key = a.TrimStart('-', '/').ToLowerInvariant();
                switch (key)
                {
                    case "minimized":
                    case "tray":
                        r.Minimized = true;
                        break;
                    case "show":
                        r.Show = true;
                        break;
                    case "quit":
                        r.Quit = true;
                        break;
                    case "debug":
                        r.Debug = true;
                        break;
                    case "launch":
                        if (i + 1 < args.Count && !string.IsNullOrWhiteSpace(args[i + 1]))
                            r.LaunchId = args[++i].Trim();
                        break;
                    default:
                        if (key.StartsWith("launch=", StringComparison.Ordinal) && key.Length > 7)
                            r.LaunchId = a.Substring(a.IndexOf('=') + 1).Trim();
                        break;
                }
            }
            return r;
        }

        /// <summary>Quotes a single argument for a Windows command line (CommandLineToArgvW rules).</summary>
        public static string Quote(string arg)
        {
            if (string.IsNullOrEmpty(arg)) return "\"\"";
            if (arg.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return arg;
            var sb = new System.Text.StringBuilder("\"");
            int backslashes = 0;
            foreach (char c in arg)
            {
                if (c == '\\') { backslashes++; continue; }
                if (c == '"') sb.Append('\\', backslashes * 2 + 1);
                else sb.Append('\\', backslashes);
                backslashes = 0;
                sb.Append(c);
            }
            sb.Append('\\', backslashes * 2).Append('"');
            return sb.ToString();
        }
    }
}
