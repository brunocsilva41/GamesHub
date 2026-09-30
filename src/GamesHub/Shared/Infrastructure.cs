using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace GamesHub
{
    public static class AppInfo
    {
        public const string Name = "GamesHub";
        public const string AppUserModelId = "GamesHub.App";
        /// <summary>Official repository: source of Releases for the updater and of the "about" links.</summary>
        public const string DefaultUpdateRepo = "brunocsilva41/GamesHub";
        public const string RepoUrl = "https://github.com/" + DefaultUpdateRepo;
        public static string Version
        {
            get
            {
                Version v = Assembly.GetExecutingAssembly().GetName().Version;
                return v.Major + "." + v.Minor + "." + v.Build;
            }
        }
    }

    /// <summary>All per-user data lives under %LOCALAPPDATA%\GamesHub. Nothing is written next to the exe
    /// or inside the games folder (except shortcuts the user explicitly adds).</summary>
    public static class AppPaths
    {
        public static readonly string AppDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
        /// <summary>GAMESHUB_DATA_DIR overrides the location (the test runner uses it to stay off real data).</summary>
        public static readonly string DataDir =
            Environment.GetEnvironmentVariable("GAMESHUB_DATA_DIR") is string d && d.Length > 0 ? d
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppInfo.Name);

        /// <summary>True when GAMESHUB_DATA_DIR isolates this process (tests, screenshots, portable runs):
        /// it must not touch per-user shell state shared with the real install (Jump List, AppUserModelID).</summary>
        public static bool IsIsolated => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GAMESHUB_DATA_DIR"));

        public static string WebDir => Path.Combine(AppDir, "web");
        public static string SettingsFile => Path.Combine(DataDir, "settings.json");
        public static string LibraryFile => Path.Combine(DataDir, "library.json");
        public static string CacheDir => Path.Combine(DataDir, "cache");
        public static string ArtDir => Path.Combine(CacheDir, "art");
        public static string TrashDir => Path.Combine(DataDir, "trash");
        public static string LogDir => Path.Combine(DataDir, "logs");
        public static string WebViewDir => Path.Combine(DataDir, "webview");

        /// <summary>v1 kept its data in &lt;gamesDir&gt;\_hub. Used only for one-time migration (read-only).</summary>
        public static string LegacyHubDir(string gamesDir) => Path.Combine(gamesDir ?? "", "_hub");

        public static string DefaultGamesDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "jogos");

        public static string AppFile(string name) => Path.Combine(AppDir, name);

        public static void EnsureDirs()
        {
            foreach (string d in new[] { DataDir, CacheDir, ArtDir, TrashDir, LogDir, WebViewDir })
                Directory.CreateDirectory(d);
        }
    }

    /// <summary>Guards Path.Combine against untrusted segments (game data, settings, web input) that could
    /// escape the intended base folder through a rooted path or "..".</summary>
    public static class PathGuard
    {
        /// <summary>Full path of <paramref name="relative"/> under <paramref name="baseDir"/>, or null when the
        /// segment is empty, rooted (C:\x, \x, C:x, \\server\x), invalid, or resolves outside baseDir.</summary>
        public static string ResolveUnder(string baseDir, string relative)
        {
            if (string.IsNullOrWhiteSpace(baseDir) || string.IsNullOrWhiteSpace(relative)) return null;
            try
            {
                if (Path.IsPathRooted(relative)) return null;
                string root = Path.GetFullPath(baseDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                              + Path.DirectorySeparatorChar;
                string full = Path.GetFullPath(Path.Combine(root, relative));
                return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) && full.Length > root.Length ? full : null;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException
                                       || ex is System.Security.SecurityException)
            {
                return null;
            }
        }
    }

    /// <summary>Thread-safe rolling log in AppPaths.LogDir (gameshub.log, rotated at 1 MB, keeps 3).</summary>
    public static class Log
    {
        private static readonly object Gate = new object();
        private const long MaxBytes = 1024 * 1024;
        private const int Keep = 3;

        public static void Info(string msg) => Write("INFO", msg, null);
        public static void Warn(string msg, Exception ex = null) => Write("WARN", msg, ex);
        public static void Error(string msg, Exception ex = null) => Write("ERROR", msg, ex);

        private static readonly Regex SteamUserData = new Regex(@"(userdata[\\/]+)\d+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly string UserProfile = SafeUserProfile();

        private static string SafeUserProfile()
        {
            try { return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) ?? ""; }
            catch (Exception ex) when (ex is ArgumentException || ex is PlatformNotSupportedException) { return ""; }
        }

        /// <summary>One safe log line: control characters escaped (a message can never forge extra lines) and
        /// personal data redacted (the user's profile folder, Steam account ids in "userdata\&lt;id&gt;").</summary>
        public static string Clean(string text) => Clean(text, UserProfile);

        public static string Clean(string text, string userProfile)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            string s = text;
            string profile = (userProfile ?? "").TrimEnd('\\', '/');
            if (profile.Length >= 3)
            {
                s = Regex.Replace(s, Regex.Escape(profile) + @"(?=[\\/""'\s,;:)\]]|$)", "%USERPROFILE%",
                                  RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                string fwd = profile.Replace('\\', '/');
                if (fwd != profile)
                    s = Regex.Replace(s, Regex.Escape(fwd) + @"(?=[\\/""'\s,;:)\]]|$)", "%USERPROFILE%",
                                      RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }
            s = SteamUserData.Replace(s, "$1<id>");
            return EscapeControl(s);
        }

        /// <summary>\r, \n, \t and every other control/line-separator character written as a visible escape.</summary>
        public static string EscapeControl(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\r': sb.Append("\\r"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (char.IsControl(c) || c == (char)0x2028 || c == (char)0x2029) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        private static void Write(string level, string msg, Exception ex)
        {
            try
            {
                string text = msg ?? "";
                if (ex != null) text += " | " + ex.GetType().Name + ": " + ex.Message;
                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + level.PadRight(5) + " " + Clean(text);
                lock (Gate)
                {
                    Directory.CreateDirectory(AppPaths.LogDir);
                    string file = Path.Combine(AppPaths.LogDir, "gameshub.log");
                    if (File.Exists(file) && new FileInfo(file).Length > MaxBytes) Rotate(file);
                    File.AppendAllText(file, line + Environment.NewLine, Encoding.UTF8);
                }
            }
            // Resilience boundary: logging itself must never throw into the caller (and cannot log its own failure).
            catch { /* logging must never throw */ }
        }

        private static void Rotate(string file)
        {
            for (int i = Keep - 1; i >= 1; i--)
            {
                string src = file + "." + i, dst = file + "." + (i + 1);
                if (File.Exists(dst)) File.Delete(dst);
                if (File.Exists(src)) File.Move(src, dst);
            }
            string first = file + ".1";
            if (File.Exists(first)) File.Delete(first);
            File.Move(file, first);
        }
    }

    /// <summary>JSON helpers (JavaScriptSerializer, no external deps) + atomic file writes.</summary>
    public static class Json
    {
        /// <summary>Upper bound for one message coming from a web page (bridge / quick-launch palette).</summary>
        public const int MaxMessageLength = 4 * 1024 * 1024;

        /// <summary>Files (library.json can be large) and outgoing payloads: no practical length limit.</summary>
        private static JavaScriptSerializer New() => new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 64 };

        public static string Serialize(object o) => New().Serialize(o);
        public static T Deserialize<T>(string s) => New().Deserialize<T>(s);
        public static object DeserializeObject(string s) => New().DeserializeObject(s);

        /// <summary>Parses an untrusted page message: ArgumentException when longer than MaxMessageLength.</summary>
        public static object DeserializeMessage(string s)
        {
            if (s != null && s.Length > MaxMessageLength) throw new ArgumentException("Message exceeds " + MaxMessageLength + " characters.");
            return new JavaScriptSerializer { MaxJsonLength = MaxMessageLength, RecursionLimit = 64 }.DeserializeObject(s);
        }

        /// <summary>Reads and deserializes a file; returns fallback if missing or unreadable (logs a warning).
        /// A file that exists but does not parse is moved aside to "&lt;file&gt;.corrupt-&lt;yyyyMMddHHmmss&gt;" (so a
        /// later save cannot destroy it) and "&lt;file&gt;.bak" — the previous good version — is tried instead.</summary>
        public static T Load<T>(string file, T fallback)
        {
            string text;
            try
            {
                if (!File.Exists(file)) return fallback;
                text = File.ReadAllText(file, Encoding.UTF8);
            }
            catch (Exception ex) when (ExpectedErrors.IsFileSystem(ex))
            {
                Log.Warn("Json.Load: cannot read " + file, ex);
                return fallback;
            }
            if (TryParse(text, out T value)) return value;

            Log.Warn("Json.Load: " + file + " is corrupt; keeping a copy and trying the backup");
            Quarantine(file);
            try
            {
                string bak = file + ".bak";
                if (File.Exists(bak) && TryParse(File.ReadAllText(bak, Encoding.UTF8), out value))
                {
                    Log.Info("Json.Load: restored " + file + " from its backup");
                    return value;
                }
            }
            catch (Exception ex) when (ExpectedErrors.IsFileSystem(ex))
            {
                Log.Warn("Json.Load: cannot read the backup of " + file, ex);
            }
            return fallback;
        }

        private static bool TryParse<T>(string text, out T value)
        {
            value = default(T);
            if (string.IsNullOrWhiteSpace(text)) return false;   // truncated/emptied by a crash or a full disk
            try
            {
                value = Deserialize<T>(text);
                return value != null;
            }
            // Resilience boundary: corrupt user files must fall back; JavaScriptSerializer's type converters
            // (e.g. BaseNumberConverter) throw plain System.Exception for bad values, so no narrower type is safe.
            catch (Exception ex)
            {
                Log.Warn("Json.Load: parse failed", ex);
                return false;
            }
        }

        /// <summary>Renames a corrupt file to "&lt;file&gt;.corrupt-&lt;stamp&gt;" (copies it when renaming fails).
        /// Returns the new path, or null when neither worked.</summary>
        internal static string Quarantine(string file)
        {
            string dest = file + ".corrupt-" + DateTime.Now.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture);
            for (int i = 1; File.Exists(dest); i++) dest = file + ".corrupt-" + DateTime.Now.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture) + "-" + i;
            try
            {
                File.Move(file, dest);
                return dest;
            }
            catch (Exception ex) when (ExpectedErrors.IsFileSystem(ex))
            {
                Log.Warn("Json.Load: cannot rename corrupt " + file + "; copying it", ex);
            }
            try
            {
                File.Copy(file, dest, false);
                return dest;
            }
            catch (Exception ex) when (ExpectedErrors.IsFileSystem(ex))
            {
                Log.Error("Json.Load: cannot preserve corrupt " + file, ex);
                return null;
            }
        }

        /// <summary>Writes via temp file + replace so a crash never leaves a half-written file.</summary>
        public static void Save(string file, object value)
        {
            WriteAllTextAtomic(file, Serialize(value));
        }

        /// <summary>Writes "&lt;file&gt;.tmp" straight to disk (write-through + flush), then swaps it in with
        /// File.Replace. User data keeps the previous version as "&lt;file&gt;.bak" (what Load falls back to);
        /// rebuildable files under AppPaths.CacheDir skip the backup.</summary>
        public static void WriteAllTextAtomic(string file, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            string tmp = file + ".tmp";
            byte[] bytes = new UTF8Encoding(false).GetBytes(text ?? "");
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(true);
            }
            if (File.Exists(file)) File.Replace(tmp, file, KeepsBackup(file) ? file + ".bak" : null);
            else File.Move(tmp, file);
        }

        private static bool KeepsBackup(string file)
        {
            string cache = AppPaths.CacheDir.TrimEnd('\\') + "\\";
            try { return !Path.GetFullPath(file).StartsWith(Path.GetFullPath(cache), StringComparison.OrdinalIgnoreCase); }
            catch (Exception ex) when (ExpectedErrors.IsFileSystem(ex)) { return true; }
        }

        // Small accessors for Dictionary<string, object> produced by DeserializeObject.
        public static string Str(IDictionary<string, object> d, string key, string def = "")
            => d != null && d.TryGetValue(key, out object v) && v != null ? Convert.ToString(v) : def;
        public static bool Bool(IDictionary<string, object> d, string key, bool def = false)
            => d != null && d.TryGetValue(key, out object v) && v is bool b ? b : def;
        public static long Long(IDictionary<string, object> d, string key, long def = 0)
        {
            if (d == null || !d.TryGetValue(key, out object v) || v == null) return def;
            try { return Convert.ToInt64(v); }
            catch (Exception ex) when (ex is FormatException || ex is InvalidCastException || ex is OverflowException) { return def; }
        }
        public static IDictionary<string, object> Obj(IDictionary<string, object> d, string key)
            => d != null && d.TryGetValue(key, out object v) ? v as IDictionary<string, object> : null;
    }
}
