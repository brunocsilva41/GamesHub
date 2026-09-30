// update-pending flag and size on disk (appmanifest_*.acf). Read-only; never writes Steam files/registry.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace GamesHub
{
    public sealed class SteamLocalData : ISteamLocalData
    {
        private readonly string fixedRoot;     // null → discover (registry / Program Files)
        private readonly object gate = new object();
        private string cacheSignature;
        private Dictionary<string, SteamLocalStats> cache = new Dictionary<string, SteamLocalStats>();
        private Dictionary<string, string> names = new Dictionary<string, string>();

        public SteamLocalData() { }

        /// <summary>Uses an explicit Steam root (tests / diagnostics).</summary>
        public SteamLocalData(string steamRoot) { fixedRoot = steamRoot; }

        /// <summary>Names from the manifests of the last Load() (appid → name). Diagnostics only.</summary>
        public Dictionary<string, string> LastNames { get { lock (gate) return new Dictionary<string, string>(names); } }

        public string ValidateUri(string appId) => SteamManifests.IsDigits(appId?.Trim()) ? "steam://validate/" + appId.Trim() : "";
        public string UninstallUri(string appId) => SteamManifests.IsDigits(appId?.Trim()) ? "steam://uninstall/" + appId.Trim() : "";

        public Dictionary<string, SteamLocalStats> Load()
        {
            lock (gate)
            {
                try
                {
                    string root = fixedRoot ?? SteamManifests.FindSteamRoot();
                    if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return Copy(new Dictionary<string, SteamLocalStats>());

                    var files = CollectFiles(root, out List<string> libraries, out string localConfig);
                    string sig = Signature(root, files);
                    if (sig == cacheSignature) return Copy(cache);

                    var sw = Stopwatch.StartNew();
                    var newNames = new Dictionary<string, string>();
                    var result = Build(root, libraries, localConfig, newNames);
                    cache = result;
                    names = newNames;
                    cacheSignature = sig;
                    Log.Info("Steam local data: " + result.Count + " apps in " + sw.ElapsedMilliseconds + " ms");
                    return Copy(result);
                }
                // Resilience boundary: parses untrusted third-party Steam files (VDF/ACF/localconfig); the caller gets the last good data instead of an exception.
                catch (Exception ex)
                {
                    Log.Warn("Steam local data: Load failed; returning cached/partial data", ex);
                    return Copy(cache);
                }
            }
        }

        // ------------------------------------------------------------------ file discovery & cache key

        private List<string> CollectFiles(string root, out List<string> libraries, out string localConfig)
        {
            var files = new List<string>();
            string lfFile = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            files.Add(lfFile);
            libraries = SteamManifests.ParseLibraryFolders(TryParseFile(lfFile), root);

            foreach (string apps in libraries.Select(lib => Path.Combine(lib, "steamapps")))
            {
                try
                {
                    if (Directory.Exists(apps)) files.AddRange(Directory.GetFiles(apps, "appmanifest_*.acf"));
                }
                catch (Exception ex) when (ExpectedErrors.IsFileSystem(ex)) { Log.Warn("Steam: cannot list " + apps, ex); }
            }

            string loginUsers = Path.Combine(root, "config", "loginusers.vdf");
            files.Add(loginUsers);
            string id64 = SteamUsers.PickActiveUser(TryParseFile(loginUsers));
            try { localConfig = SteamUsers.FindLocalConfig(root, SteamUsers.AccountIdFromSteamId64(id64)); }
            catch (Exception ex) when (ExpectedErrors.IsFileSystem(ex)) { Log.Warn("Steam: cannot locate localconfig.vdf", ex); localConfig = null; }
            if (localConfig != null) files.Add(localConfig);
            return files;
        }

        private static string Signature(string root, List<string> files)
        {
            var sb = new StringBuilder(root).Append('|');
            foreach (string f in files)
            {
                long t = 0, len = -1;
                try
                {
                    var fi = new FileInfo(f);
                    if (fi.Exists) { t = fi.LastWriteTimeUtc.Ticks; len = fi.Length; }
                }
                catch (Exception ex) when (ExpectedErrors.IsFileSystem(ex)) { Log.Warn("Steam: stat failed " + f, ex); }
                sb.Append(f).Append('*').Append(t).Append('*').Append(len).Append('|');
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ build

        private static Dictionary<string, SteamLocalStats> Build(string root, List<string> libraries, string localConfig,
                                                                Dictionary<string, string> namesOut)
        {
            var result = new Dictionary<string, SteamLocalStats>(StringComparer.Ordinal);
            var manifestLastPlayed = new Dictionary<string, long>();

            foreach (string lib in libraries)
            {
                string apps = Path.Combine(lib, "steamapps");
                string[] acfs;
                try { acfs = Directory.Exists(apps) ? Directory.GetFiles(apps, "appmanifest_*.acf") : new string[0]; }
                catch (Exception ex) when (ExpectedErrors.IsFileSystem(ex)) { Log.Warn("Steam: cannot list " + apps, ex); continue; }
                foreach (string acf in acfs)
                {
                    SteamManifestInfo m = SteamManifests.ParseManifest(TryParseFile(acf), lib);
                    if (m == null) { Log.Warn("Steam: unreadable manifest " + acf); continue; }
                    if (result.ContainsKey(m.AppId)) continue;   // first library wins (same order as Steam)
                    result[m.AppId] = new SteamLocalStats
                    {
                        AppId = m.AppId,
                        InstallDir = m.InstallDir,
                        SizeOnDisk = m.SizeOnDisk,
                        UpdatePending = m.UpdatePending,
                    };
                    manifestLastPlayed[m.AppId] = m.LastPlayedUnix;
                    if (!string.IsNullOrEmpty(m.Name)) namesOut[m.AppId] = m.Name;
                }
            }

            Dictionary<string, SteamAppPlay> play = null;
            if (localConfig != null)
            {
                try
                {
                    using (var sr = new StreamReader(localConfig, Encoding.UTF8, true, 1 << 16))
                        play = SteamUsers.ReadPlayStats(sr);
                }
                catch (Exception ex) when (ExpectedErrors.IsFileSystem(ex)) { Log.Warn("Steam: cannot read " + localConfig, ex); }
            }

            if (play != null)
            {
                foreach (var kv in play)
                {
                    if (!result.TryGetValue(kv.Key, out SteamLocalStats s))
                    {
                        // Played but not installed on this PC (or a tool): still reported for play-time import.
                        s = new SteamLocalStats { AppId = kv.Key };
                        result[kv.Key] = s;
                    }
                    s.PlaytimeMinutes = kv.Value.PlaytimeMinutes;
                    s.LastPlayed = SteamUsers.FromUnix(kv.Value.LastPlayedUnix);
                }
            }

            // Fallback: the manifest's machine-wide LastPlayed when the user's localconfig has none.
            foreach (var kv in manifestLastPlayed.Where(e => result[e.Key].LastPlayed == null))
                result[kv.Key].LastPlayed = SteamUsers.FromUnix(kv.Value);

            return result;
        }

        private static SteamKv TryParseFile(string file)
        {
            try { return File.Exists(file) ? SteamKvParser.ParseFile(file) : null; }
            catch (Exception ex) when (ExpectedErrors.IsFileSystem(ex)) { Log.Warn("Steam: cannot read " + file, ex); return null; }
        }

        private static Dictionary<string, SteamLocalStats> Copy(Dictionary<string, SteamLocalStats> src)
        {
            var d = new Dictionary<string, SteamLocalStats>(src.Count, StringComparer.Ordinal);
            foreach (var kv in src)
            {
                SteamLocalStats s = kv.Value;
                d[kv.Key] = new SteamLocalStats
                {
                    AppId = s.AppId, PlaytimeMinutes = s.PlaytimeMinutes, LastPlayed = s.LastPlayed,
                    UpdatePending = s.UpdatePending, SizeOnDisk = s.SizeOnDisk, InstallDir = s.InstallDir,
                };
            }
            return d;
        }
    }
}
