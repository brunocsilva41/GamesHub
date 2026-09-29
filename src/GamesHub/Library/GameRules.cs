// OWNER: LIB agent. Pure rules used by the library sources (no I/O): names, ids, platforms, app ids.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

[assembly: InternalsVisibleTo("GamesHub.Tests")]

namespace GamesHub
{
    internal static class GameRules
    {
        public const string SourceFolder = "folder", SourceSteam = "steam", SourceEpic = "epic";

        private static readonly string[] ShortcutSuffixes = { " - Atalho", " - Shortcut", " - Copia", " - Cópia", " - Copy" };

        public static string FolderId(string fileName) => "folder:" + (fileName ?? "").ToLowerInvariant();
        public static string SteamId(string appId) => "steam:" + appId;
        public static string EpicId(string appName) => "epic:" + appName;

        public static bool IsGameFileExt(string ext)
        {
            ext = (ext ?? "").ToLowerInvariant();
            return ext == ".lnk" || ext == ".url" || ext == ".exe";
        }

        /// <summary>"PointBlank.exe - Atalho.lnk" → "PointBlank"; "Hot Wheels 2.exe.lnk" → "Hot Wheels 2".</summary>
        public static string CleanName(string fileName)
        {
            string name = fileName ?? "";
            string ext = Path.GetExtension(name);
            if (IsGameFileExt(ext)) name = name.Substring(0, name.Length - ext.Length);
            bool changed = true;
            while (changed)
            {
                changed = false;
                name = name.Trim();
                foreach (string suffix in ShortcutSuffixes)
                    if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    { name = name.Substring(0, name.Length - suffix.Length); changed = true; }
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                { name = name.Substring(0, name.Length - 4); changed = true; }
            }
            name = name.Trim();
            return name.Length > 0 ? name : Path.GetFileNameWithoutExtension(fileName ?? "");
        }

        /// <summary>Makes a string safe to use as a file name (no extension).</summary>
        public static string SanitizeFileName(string s)
        {
            var sb = new StringBuilder();
            char[] invalid = Path.GetInvalidFileNameChars();
            foreach (char c in s ?? "")
            {
                if (c == '/' || c == '\\') sb.Append('-');
                else if (c == ':') sb.Append(" -");
                else if (Array.IndexOf(invalid, c) < 0) sb.Append(c);
            }
            string r = Regex.Replace(sb.ToString(), @"\s+", " ").Trim().TrimEnd('.', ' ');
            if (r.Length > 120) r = r.Substring(0, 120).TrimEnd('.', ' ');
            return r.Length == 0 ? "Novo jogo" : r;
        }

        /// <summary>Returns "dir\base.ext", or "dir\base (2).ext", ... for the first name not taken.</summary>
        public static string UniquePath(string dir, string baseName, string ext, Func<string, bool> exists = null)
        {
            exists = exists ?? File.Exists;
            string p = Path.Combine(dir, baseName + ext);
            for (int i = 2; exists(p); i++) p = Path.Combine(dir, baseName + " (" + i + ")" + ext);
            return p;
        }

        /// <summary>Trash file name: "base (yyyyMMdd-HHmmss).ext", with a counter if that is taken too.</summary>
        public static string TrashPath(string trashDir, string fileName, DateTime now, Func<string, bool> exists = null)
        {
            string ext = Path.GetExtension(fileName);
            string stem = Path.GetFileNameWithoutExtension(fileName) + " (" + now.ToString("yyyyMMdd-HHmmss") + ")";
            return UniquePath(trashDir, stem, ext, exists);
        }

        /// <summary>True for "scheme://..." targets (steam://, com.epicgames.launcher://, https://...).</summary>
        public static bool IsUri(string s) => Regex.IsMatch(s ?? "", @"^[a-z][a-z0-9+.\-]{1,40}://", RegexOptions.IgnoreCase);

        // ------------------------------------------------------------------ platforms

        private static readonly (string Platform, Regex Pattern)[] PlatformRules =
        {
            ("Steam", R(@"steam://|hydralauncher://run|(^|[\\/])steam\.exe")),
            ("Epic", R(@"com\.epicgames\.launcher://|EpicGamesLauncher")),
            ("Riot", R(@"RiotClientServices|riotclient://")),
            ("Roblox", R(@"roblox")),
            ("Minecraft", R(@"sklauncher|javaw|minecraft")),
            ("Battle.net", R(@"battlenet://|blizzard://|battle\.net( launcher)?\.exe")),
            ("EA", R(@"origin2?://|ealink://|EADesktop|EALauncher|Origin\.exe")),
            ("Ubisoft", R(@"uplay://|UbisoftConnect|Ubisoft Game Launcher[\\/](upc|UbisoftConnect|uplay)\.exe")),
            ("GOG", R(@"goggalaxy://|GalaxyClient")),
        };

        private static Regex R(string p) => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>target = shortcut target + " " + arguments, or the URL of a .url file.</summary>
        public static string DetectPlatform(string target)
        {
            target = target ?? "";
            foreach (var rule in PlatformRules)
                if (rule.Pattern.IsMatch(target)) return rule.Platform;
            return "PC";
        }

        private static readonly Regex[] AppIdPatterns =
        {
            R(@"steam://rungameid/(\d+)"), R(@"steam://run/(\d+)"), R(@"steam://launch/(\d+)"),
            R(@"objectId=(\d+)"), R(@"-applaunch\s+(\d+)"),
        };

        /// <summary>Steam app id from a target/URL, "" if none (ignores 64-bit non-Steam shortcut ids).</summary>
        public static string ExtractSteamAppId(string target)
        {
            foreach (Regex r in AppIdPatterns)
            {
                Match m = r.Match(target ?? "");
                if (m.Success && ulong.TryParse(m.Groups[1].Value, out ulong v) && v > 0 && v <= uint.MaxValue)
                    return v.ToString();
            }
            return "";
        }

        /// <summary>AppName from "com.epicgames.launcher://apps/ns%3Aid%3AAppName?action=launch" (or "apps/AppName?...").</summary>
        public static string ExtractEpicAppName(string target)
        {
            Match m = Regex.Match(target ?? "", @"com\.epicgames\.launcher://apps/([^?\s""]+)", RegexOptions.IgnoreCase);
            if (!m.Success) return "";
            string s = Uri.UnescapeDataString(m.Groups[1].Value).TrimEnd('/');
            int i = s.LastIndexOf(':');
            return i >= 0 ? s.Substring(i + 1) : s;
        }

        /// <summary>Accepts "730", " 730 ", or a store URL ".../app/730/...". Returns "" when invalid.</summary>
        public static string NormalizeAppId(string input)
        {
            string s = (input ?? "").Trim();
            if (Regex.IsMatch(s, @"^\d{1,10}$"))
            {
                ulong v = ulong.Parse(s);
                return v > 0 && v <= uint.MaxValue ? v.ToString() : "";
            }
            Match m = Regex.Match(s, @"/app/(\d{1,10})(\D|$)");
            return m.Success ? NormalizeAppId(m.Groups[1].Value) : "";
        }

        // ------------------------------------------------------------------ launchers / generic dirs

        private static readonly HashSet<string> LauncherExes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "steam.exe", "steamservice.exe", "riotclientservices.exe", "riotclientux.exe", "epicgameslauncher.exe",
            "battle.net.exe", "battle.net launcher.exe", "javaw.exe", "java.exe", "robloxplayerlauncher.exe",
            "robloxstudiolauncherbeta.exe", "robloxplayerbeta.exe", "robloxstudiobeta.exe", "eadesktop.exe",
            "ealauncher.exe", "origin.exe", "ubisoftconnect.exe", "upc.exe", "uplay.exe", "galaxyclient.exe",
            "hydra.exe", "explorer.exe", "cmd.exe", "powershell.exe", "rundll32.exe", "msedge.exe", "chrome.exe",
            "firefox.exe", "xboxpcapp.exe", "gamelaunchhelper.exe",
        };

        /// <summary>True when the exe is a store/launcher client (or a generic host) rather than the game itself.</summary>
        public static bool IsLauncherExe(string exePath)
        {
            string file = Path.GetFileName(exePath ?? "");
            if (file.Length == 0) return false;
            if (LauncherExes.Contains(file)) return true;
            return file.StartsWith("sklauncher", StringComparison.OrdinalIgnoreCase)
                || file.StartsWith("minecraftlauncher", StringComparison.OrdinalIgnoreCase);
        }

        private static readonly string[] LauncherDirSuffixes =
        {
            @"\steam", @"\steamapps", @"\steamapps\common", @"\epic games", @"\epic games\launcher", @"\riot games",
            @"\riot client", @"\battle.net", @"\ubisoft game launcher", @"\gog galaxy", @"\ea desktop",
            @"\electronic arts", @"\roblox", @"\roblox\versions", @"\hydra", @"\hydralauncher",
        };

        private static readonly string[] LauncherDirParts = { @"\riot client\", @"\epic games\launcher\", @"\battle.net\", @"\steam\bin\" };

        private static readonly Lazy<string[]> GenericDirs = new Lazy<string[]>(() =>
        {
            var list = new List<string>();
            foreach (Environment.SpecialFolder f in new[] {
                Environment.SpecialFolder.Windows, Environment.SpecialFolder.System, Environment.SpecialFolder.SystemX86,
                Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86,
                Environment.SpecialFolder.CommonApplicationData, Environment.SpecialFolder.UserProfile,
                Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.CommonDesktopDirectory,
                Environment.SpecialFolder.MyDocuments, Environment.SpecialFolder.LocalApplicationData,
                Environment.SpecialFolder.ApplicationData })
                list.Add(Environment.GetFolderPath(f));
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            list.Add(Path.Combine(profile, "Downloads"));
            list.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"));
            list.Add(Path.GetTempPath());
            return list.Where(d => !string.IsNullOrEmpty(d)).Select(NormalizeDir).ToArray();
        });

        public static string NormalizeDir(string dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) return "";
            try { return Path.GetFullPath(dir.Trim()).TrimEnd('\\', '/'); }
            catch (Exception ex) { Log.Warn("NormalizeDir failed: " + dir, ex); return ""; }
        }

        /// <summary>True for folders too broad to identify a single game (drive roots, Windows, Program Files,
        /// the user profile, the games folder itself, launcher install folders...).</summary>
        public static bool IsGenericDir(string dir, string gamesDir)
        {
            string d = NormalizeDir(dir);
            if (d.Length == 0) return true;
            if (d.Length <= 3 || string.Equals(Path.GetPathRoot(d).TrimEnd('\\'), d, StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(d, NormalizeDir(gamesDir), StringComparison.OrdinalIgnoreCase)) return true;
            string win = GenericDirs.Value.Length > 0 ? GenericDirs.Value[0] : "";
            if (win.Length > 0 && d.StartsWith(win + "\\", StringComparison.OrdinalIgnoreCase)) return true;
            foreach (string g in GenericDirs.Value)
                if (string.Equals(d, g, StringComparison.OrdinalIgnoreCase)) return true;
            string lower = d.ToLowerInvariant();
            foreach (string s in LauncherDirSuffixes) if (lower.EndsWith(s)) return true;
            foreach (string s in LauncherDirParts) if ((lower + "\\").Contains(s)) return true;
            return false;
        }
    }
}
