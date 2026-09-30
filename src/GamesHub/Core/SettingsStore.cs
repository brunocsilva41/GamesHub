using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace GamesHub
{
    public sealed class SettingsPatchResult
    {
        /// <summary>camelCase keys whose value actually changed.</summary>
        public readonly List<string> Changed = new List<string>();
        /// <summary>camelCase keys whose value was invalid (the old value was kept).</summary>
        public readonly List<string> Rejected = new List<string>();
        public bool Has(string key) => Changed.Contains(key);
    }

    /// <summary>
    /// Owns the single live AppSettings instance (shared by every service) and its persistence as
    /// camelCase JSON in AppPaths.SettingsFile. The instance is mutated in place, never replaced.
    /// DTO mapping is generic over AppSettings' public fields (bool/string/int/long/double); specific
    /// keys get extra validation/normalization through <see cref="Validators"/>.
    /// </summary>
    public static class SettingsStore
    {
        private static readonly object Gate = new object();
        private static readonly Regex KeyRx = new Regex(@"^[A-Za-z0-9]{0,128}$");
        private const int MaxStringLength = 2048;

        public static readonly string[] OnLaunchValues = { "tray", "minimize", "none" };
        public static readonly string[] SortByValues = { "name", "recent", "playtime", "added" };
        public static readonly string[] ViewValues = { "grid", "list" };
        public static readonly string[] CardStyleValues = { "landscape", "portrait" };
        public static readonly string[] LanguageValues = { "pt-BR", "en-US" };

        /// <summary>camelCase key → AppSettings field, in declaration order.</summary>
        private static readonly Dictionary<string, FieldInfo> Fields = typeof(AppSettings)
            .GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Where(f => IsSupported(f.FieldType))
            .ToDictionary(f => CamelCase(f.Name), f => f);

        /// <summary>Per-key normalizers for string settings: return the canonical value or null if invalid.
        /// gamesDir is handled separately (needs the directory-exists check).</summary>
        private static readonly Dictionary<string, Func<string, string>> Validators = new Dictionary<string, Func<string, string>>
        {
            ["onLaunch"] = v => OneOf(v, OnLaunchValues),
            ["sortBy"] = v => OneOf(v, SortByValues),
            ["view"] = v => OneOf(v, ViewValues),
            ["cardStyle"] = v => OneOf(v, CardStyleValues),
            ["language"] = v => OneOf(v, LanguageValues),
            ["hotkey"] = NormalizeHotkey,
            ["quickLaunchHotkey"] = NormalizeHotkey,
            ["steamGridDbKey"] = v => KeyRx.IsMatch(v.Trim()) ? v.Trim() : null,
        };

        /// <summary>Secret settings: never sent to the UI (only "&lt;key&gt;Set": bool) and stored DPAPI-protected on disk
        /// under "&lt;key&gt;Protected". A plain value found in an older settings.json is re-saved protected.</summary>
        private static readonly string[] SecretKeys = { "steamGridDbKey" };
        private const string ProtectedSuffix = "Protected";

        public static AppSettings Current { get; private set; } = new AppSettings();

        /// <summary>Raised (on the caller's thread) after ApplyPatch changed and saved something.</summary>
        public static event Action Changed;

        /// <summary>Loads settings.json (or migrates v1 config on first run). Call once at startup.</summary>
        public static AppSettings Load()
        {
            lock (Gate)
            {
                var s = new AppSettings();
                bool exists = File.Exists(AppPaths.SettingsFile);
                bool migrated = false;
                if (exists)
                {
                    var dict = Json.Load<Dictionary<string, object>>(AppPaths.SettingsFile, null);
                    if (dict != null)
                    {
                        SettingsPatchResult r = FromDisk(s, dict, out migrated);
                        if (r.Rejected.Count > 0) Log.Warn("settings.json: ignored invalid values: " + string.Join(", ", r.Rejected));
                    }
                }
                if (string.IsNullOrWhiteSpace(s.GamesDir))
                {
                    s.GamesDir = MigrateGamesDir();
                    migrated = true;
                }
                Current = s;
                if (!exists || migrated) Save();
                Log.Info("Settings loaded (gamesDir=" + s.GamesDir + ")");
                return s;
            }
        }

        public static void Save()
        {
            lock (Gate)
            {
                try
                {
                    Json.Save(AppPaths.SettingsFile, ToDisk(Current));
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException
                                           || ex is NotSupportedException || ex is ArgumentException || ex is InvalidOperationException)
                {
                    Log.Warn("Could not save settings", ex);
                }
            }
        }

        /// <summary>Validates and applies a partial camelCase patch to Current, saves and raises Changed.</summary>
        public static SettingsPatchResult ApplyPatch(IDictionary<string, object> patch)
        {
            SettingsPatchResult r;
            lock (Gate)
            {
                r = ApplyPatchTo(Current, patch);
                if (r.Changed.Count > 0) Save();
            }
            if (r.Rejected.Count > 0) Log.Warn("Settings patch rejected: " + string.Join(", ", r.Rejected));
            if (r.Changed.Count > 0)
            {
                Log.Info("Settings changed: " + string.Join(", ", r.Changed));
                Changed?.Invoke();
            }
            return r;
        }

        /// <summary>DTO for the UI: all supported AppSettings fields as a camelCase dictionary, except that secrets are
        /// blanked and reported only as "&lt;key&gt;Set": bool (the UI treats them as write-only).</summary>
        public static Dictionary<string, object> ToDto(AppSettings s)
        {
            Dictionary<string, object> d = Fields.ToDictionary(kv => kv.Key, kv => kv.Value.GetValue(s));
            foreach (string k in SecretKeys)
            {
                d[k + "Set"] = !string.IsNullOrEmpty(d[k] as string);
                d[k] = "";
            }
            return d;
        }

        /// <summary>What settings.json stores: every field, with secrets replaced by their DPAPI-protected form.</summary>
        public static Dictionary<string, object> ToDisk(AppSettings s)
        {
            Dictionary<string, object> d = Fields.ToDictionary(kv => kv.Key, kv => kv.Value.GetValue(s));
            foreach (string k in SecretKeys)
            {
                string plain = d[k] as string;
                d.Remove(k);
                if (string.IsNullOrEmpty(plain)) continue;
                string blob = SecretProtector.Protect(plain);
                if (blob != null) d[k + ProtectedSuffix] = blob;
                else Log.Warn("Could not protect " + k + " with DPAPI; it is kept for this session only");
            }
            return d;
        }

        /// <summary>Pure (apart from DPAPI): applies a settings.json dictionary to <paramref name="s"/>. Protected secrets
        /// are decrypted; resave is true when the file holds a legacy plain secret or obsolete keys and should be
        /// rewritten. Folder existence is not checked (a games folder on an unplugged drive must survive a restart).</summary>
        public static SettingsPatchResult FromDisk(AppSettings s, IDictionary<string, object> dict, out bool resave)
        {
            resave = dict != null && dict.ContainsKey("updateRepo"); // no longer a setting (the updater's repo is fixed)
            SettingsPatchResult r = ApplyPatchTo(s, dict, _ => true);
            if (dict == null) return r;
            foreach (string k in SecretKeys)
            {
                if (dict.TryGetValue(k, out object plain) && plain is string p && p.Length > 0) resave = true; // migrate to DPAPI
                if (!dict.TryGetValue(k + ProtectedSuffix, out object blob) || !(blob is string b) || b.Length == 0) continue;
                string value = SecretProtector.Unprotect(b);
                if (value == null)
                {
                    Log.Warn("settings.json: " + k + " could not be decrypted (another Windows user or machine?); ignored");
                    continue;
                }
                SettingsPatchResult sr = ApplyPatchTo(s, new Dictionary<string, object> { [k] = value }, _ => true);
                r.Rejected.AddRange(sr.Rejected);
            }
            return r;
        }

        /// <summary>Pure: applies a patch to <paramref name="s"/>. Unknown keys are ignored; invalid values are
        /// reported in Rejected and leave the field unchanged. dirExists defaults to Directory.Exists.</summary>
        public static SettingsPatchResult ApplyPatchTo(AppSettings s, IDictionary<string, object> patch, Func<string, bool> dirExists = null)
        {
            var r = new SettingsPatchResult();
            if (patch == null) return r;
            dirExists = dirExists ?? Directory.Exists;
            foreach (KeyValuePair<string, object> kv in patch.Where(p => Fields.ContainsKey(p.Key)))
            {
                FieldInfo field = Fields[kv.Key];
                object value = Coerce(kv.Key, field.FieldType, kv.Value, dirExists);
                if (value == null)
                {
                    r.Rejected.Add(kv.Key);
                    continue;
                }
                if (Equals(field.GetValue(s), value)) continue;
                field.SetValue(s, value);
                r.Changed.Add(kv.Key);
            }
            return r;
        }

        /// <summary>Converts a JSON value to the field's type and validates it; null = invalid.</summary>
        private static object Coerce(string key, Type type, object v, Func<string, bool> dirExists)
        {
            if (type == typeof(bool)) return v is bool ? v : null;
            if (type == typeof(string))
            {
                if (!(v is string str) || str.Length > MaxStringLength) return null;
                if (key == "gamesDir")
                {
                    string dir = NormalizeDir(str);
                    return dir != null && dirExists(dir) ? dir : null;
                }
                return Validators.TryGetValue(key, out Func<string, string> validate) ? validate(str) : str;
            }
            if (!IsNumber(v)) return null;
            try
            {
                if (type == typeof(int)) return Convert.ToInt32(v);
                if (type == typeof(long)) return Convert.ToInt64(v);
                if (type == typeof(double)) return Convert.ToDouble(v);
            }
            catch (OverflowException)
            {
                return null;
            }
            return null;
        }

        private static bool IsSupported(Type t) => t == typeof(bool) || t == typeof(string) || t == typeof(int) || t == typeof(long) || t == typeof(double);

        private static bool IsNumber(object v) => v is int || v is long || v is decimal || v is double || v is float;

        private static string CamelCase(string name) => char.ToLowerInvariant(name[0]) + name.Substring(1);

        private static string OneOf(string v, string[] allowed)
            => allowed.FirstOrDefault(a => string.Equals(a, v.Trim(), StringComparison.OrdinalIgnoreCase));

        private static string NormalizeHotkey(string v) => HotkeyParser.TryParse(v, out Hotkey hk) ? hk.Display : null;

        /// <summary>Full, rooted path without trailing separator (except drive roots); null if invalid.</summary>
        public static string NormalizeDir(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            path = path.Trim().Trim('"');
            try
            {
                if (!Path.IsPathRooted(path)) return null;
                string full = Path.GetFullPath(path);
                string root = Path.GetPathRoot(full);
                return full.Length > root.Length ? full.TrimEnd('\\', '/') : full;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                return null;
            }
        }

        /// <summary>First run: reuse the games folder of the v1 install (config.json), else the default.</summary>
        private static string MigrateGamesDir()
        {
            string[] candidates =
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "GamesHub", "config.json"),
                AppPaths.AppFile("config.json"),
            };
            foreach (string file in candidates)
            {
                var cfg = Json.Load<Dictionary<string, object>>(file, null);
                string dir = NormalizeDir(Json.Str(cfg, "gamesDir"));
                if (dir != null && Directory.Exists(dir))
                {
                    Log.Info("Migrated gamesDir from v1 config: " + file);
                    return dir;
                }
            }
            return AppPaths.DefaultGamesDir;
        }
    }

    /// <summary>
    /// DPAPI with CurrentUser scope (CryptProtectData, called directly so System.Security.dll is not needed): only the
    /// same Windows user on the same machine can decrypt. Output is Base64; both methods return null on failure.
    /// </summary>
    public static class SecretProtector
    {
        private const int UiForbidden = 0x1; // CRYPTPROTECT_UI_FORBIDDEN
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("GamesHub.settings.secret.v1");

        public static string Protect(string plain)
        {
            if (plain == null) return null;
            byte[] data = Encoding.UTF8.GetBytes(plain);
            try
            {
                byte[] blob = Transform(data, true);
                return blob == null ? null : Convert.ToBase64String(blob);
            }
            finally { Array.Clear(data, 0, data.Length); }
        }

        public static string Unprotect(string base64)
        {
            if (string.IsNullOrEmpty(base64)) return null;
            byte[] blob;
            try { blob = Convert.FromBase64String(base64); }
            catch (FormatException) { return null; }
            byte[] data = Transform(blob, false);
            if (data == null) return null;
            try { return new UTF8Encoding(false, true).GetString(data); }
            catch (ArgumentException) { return null; } // DecoderFallbackException
            finally { Array.Clear(data, 0, data.Length); }
        }

        private static byte[] Transform(byte[] input, bool protect)
        {
            DataBlob src = default, entropy = default, dst = default;
            try
            {
                src = Alloc(input);
                entropy = Alloc(Entropy);
                bool ok = protect
                    ? CryptProtectData(ref src, IntPtr.Zero, ref entropy, IntPtr.Zero, IntPtr.Zero, UiForbidden, out dst)
                    : CryptUnprotectData(ref src, IntPtr.Zero, ref entropy, IntPtr.Zero, IntPtr.Zero, UiForbidden, out dst);
                if (!ok || dst.pbData == IntPtr.Zero) return null;
                var result = new byte[dst.cbData];
                Marshal.Copy(dst.pbData, result, 0, dst.cbData);
                return result;
            }
            finally
            {
                Free(src);
                Free(entropy);
                if (dst.pbData != IntPtr.Zero)
                {
                    Marshal.Copy(new byte[dst.cbData], 0, dst.pbData, dst.cbData);
                    LocalFree(dst.pbData);
                }
            }
        }

        private static DataBlob Alloc(byte[] bytes)
        {
            var b = new DataBlob { cbData = bytes.Length, pbData = Marshal.AllocHGlobal(Math.Max(1, bytes.Length)) };
            Marshal.Copy(bytes, 0, b.pbData, bytes.Length);
            return b;
        }

        private static void Free(DataBlob b)
        {
            if (b.pbData == IntPtr.Zero) return;
            Marshal.Copy(new byte[b.cbData], 0, b.pbData, b.cbData);
            Marshal.FreeHGlobal(b.pbData);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DataBlob
        {
            public int cbData;
            public IntPtr pbData;
        }

        [DllImport("crypt32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptProtectData(ref DataBlob pDataIn, IntPtr szDataDescr, ref DataBlob pOptionalEntropy,
                                                    IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, out DataBlob pDataOut);

        [DllImport("crypt32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptUnprotectData(ref DataBlob pDataIn, IntPtr ppszDataDescr, ref DataBlob pOptionalEntropy,
                                                      IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, out DataBlob pDataOut);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr hMem);
    }
}
