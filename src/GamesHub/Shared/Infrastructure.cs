// OWNER: integration (lead). Shared infrastructure used by every module.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;

namespace GamesHub
{
    public static class AppInfo
    {
        public const string Name = "GamesHub";
        public const string AppUserModelId = "GamesHub.App";
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

    /// <summary>Thread-safe rolling log in AppPaths.LogDir (gameshub.log, rotated at 1 MB, keeps 3).</summary>
    public static class Log
    {
        private static readonly object Gate = new object();
        private const long MaxBytes = 1024 * 1024;
        private const int Keep = 3;

        public static void Info(string msg) => Write("INFO", msg, null);
        public static void Warn(string msg, Exception ex = null) => Write("WARN", msg, ex);
        public static void Error(string msg, Exception ex = null) => Write("ERROR", msg, ex);

        private static void Write(string level, string msg, Exception ex)
        {
            try
            {
                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + level.PadRight(5) + " " + msg;
                if (ex != null) line += " | " + ex.GetType().Name + ": " + ex.Message;
                lock (Gate)
                {
                    Directory.CreateDirectory(AppPaths.LogDir);
                    string file = Path.Combine(AppPaths.LogDir, "gameshub.log");
                    if (File.Exists(file) && new FileInfo(file).Length > MaxBytes) Rotate(file);
                    File.AppendAllText(file, line + Environment.NewLine, Encoding.UTF8);
                }
            }
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
        private static JavaScriptSerializer New() => new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 64 };

        public static string Serialize(object o) => New().Serialize(o);
        public static T Deserialize<T>(string s) => New().Deserialize<T>(s);
        public static object DeserializeObject(string s) => New().DeserializeObject(s);

        /// <summary>Reads and deserializes a file; returns fallback if missing or invalid (logs a warning).</summary>
        public static T Load<T>(string file, T fallback)
        {
            try
            {
                if (!File.Exists(file)) return fallback;
                T v = Deserialize<T>(File.ReadAllText(file, Encoding.UTF8));
                return v == null ? fallback : v;
            }
            catch (Exception ex)
            {
                Log.Warn("Json.Load failed: " + file, ex);
                return fallback;
            }
        }

        /// <summary>Writes via temp file + replace so a crash never leaves a half-written file.</summary>
        public static void Save(string file, object value)
        {
            WriteAllTextAtomic(file, Serialize(value));
        }

        public static void WriteAllTextAtomic(string file, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            string tmp = file + ".tmp";
            File.WriteAllText(tmp, text, new UTF8Encoding(false));
            if (File.Exists(file)) File.Replace(tmp, file, null);
            else File.Move(tmp, file);
        }

        // Small accessors for Dictionary<string, object> produced by DeserializeObject.
        public static string Str(IDictionary<string, object> d, string key, string def = "")
            => d != null && d.TryGetValue(key, out object v) && v != null ? Convert.ToString(v) : def;
        public static bool Bool(IDictionary<string, object> d, string key, bool def = false)
            => d != null && d.TryGetValue(key, out object v) && v is bool b ? b : def;
        public static long Long(IDictionary<string, object> d, string key, long def = 0)
        {
            if (d == null || !d.TryGetValue(key, out object v) || v == null) return def;
            try { return Convert.ToInt64(v); } catch { return def; }
        }
        public static IDictionary<string, object> Obj(IDictionary<string, object> d, string key)
            => d != null && d.TryGetValue(key, out object v) ? v as IDictionary<string, object> : null;
    }
}
