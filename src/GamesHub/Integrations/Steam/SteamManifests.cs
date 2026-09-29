using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace GamesHub
{
    /// <summary>One installed app as described by steamapps/appmanifest_&lt;id&gt;.acf.</summary>
    public sealed class SteamManifestInfo
    {
        public string AppId = "";
        public string Name = "";
        public string InstallDir = "";
        public long SizeOnDisk = -1;
        public long StateFlags;
        public long LastPlayedUnix;          // machine-wide fallback when localconfig has no value
        public bool UpdatePending;
    }

    public static class SteamManifests
    {
        // EAppState bits (steam_api / appmanifest "StateFlags").
        public const long StateUninstalled = 1;
        public const long StateUpdateRequired = 2;
        public const long StateFullyInstalled = 4;
        public const long StateUpdateRunning = 256;
        public const long StateUpdatePaused = 512;
        public const long StateUpdateStarted = 1024;
        public const long StateAddingFiles = 0x40000;
        public const long StatePreallocating = 0x80000;
        public const long StateDownloading = 0x100000;
        public const long StateStaging = 0x200000;
        public const long StateCommitting = 0x400000;
        public const long StateUpdateStopping = 0x800000;

        /// <summary>Bits meaning "an update is required or in progress".</summary>
        public const long UpdateBits = StateUpdateRequired | StateUpdateRunning | StateUpdatePaused | StateUpdateStarted
            | StateAddingFiles | StatePreallocating | StateDownloading | StateStaging | StateCommitting | StateUpdateStopping;

        /// <summary>
        /// UpdatePending rule:
        ///  1. StateFlags has any of <see cref="UpdateBits"/> (2 UpdateRequired, 256 UpdateRunning, 512 UpdatePaused,
        ///     1024 UpdateStarted, 0x40000 AddingFiles, 0x80000 Preallocating, 0x100000 Downloading,
        ///     0x200000 Staging, 0x400000 Committing, 0x800000 UpdateStopping); or
        ///  2. BytesToDownload &gt; BytesDownloaded (a partially downloaded update); or
        ///  3. TargetBuildID is set and differs from buildid (an update is queued for a newer build); or
        ///  4. UpdateResult != 0 while the FullyInstalled bit (4) is not set (last update failed).
        /// A plain "4" (FullyInstalled) with matching build ids and byte counters is up to date.
        /// </summary>
        public static bool IsUpdatePending(long stateFlags, long bytesToDownload, long bytesDownloaded,
                                           long updateResult, long buildId = 0, long targetBuildId = 0)
        {
            if ((stateFlags & UpdateBits) != 0) return true;
            if (bytesToDownload > 0 && bytesToDownload > bytesDownloaded) return true;
            if (targetBuildId > 0 && buildId > 0 && targetBuildId != buildId) return true;
            if (updateResult != 0 && (stateFlags & StateFullyInstalled) == 0) return true;
            return false;
        }

        /// <summary>HKCU\Software\Valve\Steam\SteamPath, else %ProgramFiles(x86)%\Steam. Null if neither exists.</summary>
        public static string FindSteamRoot()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam", false))
                {
                    string p = k?.GetValue("SteamPath") as string;
                    if (!string.IsNullOrWhiteSpace(p))
                    {
                        p = Path.GetFullPath(p.Replace('/', '\\'));
                        if (Directory.Exists(p)) return p;
                    }
                }
            }
            catch (Exception ex) { Log.Warn("Steam: reading SteamPath from registry failed", ex); }

            string pf = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
            if (string.IsNullOrEmpty(pf)) pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string def = Path.Combine(pf ?? "", "Steam");
            return Directory.Exists(def) ? def : null;
        }

        /// <summary>Library roots from libraryfolders.vdf (modern {"path"} blocks and the old "N" "path" form).
        /// The Steam root itself is always included first. Duplicates removed (case-insensitive).</summary>
        public static List<string> ParseLibraryFolders(SteamKv doc, string steamRoot)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Add(string p)
            {
                if (string.IsNullOrWhiteSpace(p)) return;
                string n = p.Replace('/', '\\').TrimEnd('\\');
                if (seen.Add(n)) result.Add(n);
            }
            Add(steamRoot);
            SteamKv lf = doc?.Node("libraryfolders");
            if (lf != null)
            {
                foreach (string key in lf.Order)
                {
                    if (!IsDigits(key)) continue;
                    SteamKv child = lf.Node(key);
                    if (child != null) Add(child.Str("path"));
                    else Add(lf.Str(key));
                }
            }
            return result;
        }

        /// <summary>Parses one appmanifest document. Returns null when there is no numeric appid.</summary>
        public static SteamManifestInfo ParseManifest(SteamKv doc, string libraryRoot)
        {
            SteamKv st = doc?.Node("AppState");
            if (st == null) return null;
            string id = st.Str("appid").Trim();
            if (!IsDigits(id)) return null;
            string dir = st.Str("installdir").Trim();
            long flags = st.Long("StateFlags");
            return new SteamManifestInfo
            {
                AppId = id,
                Name = st.Str("name"),
                InstallDir = dir.Length == 0 || string.IsNullOrEmpty(libraryRoot) ? "" :
                    Path.Combine(libraryRoot, "steamapps", "common", dir),
                SizeOnDisk = st.Values.ContainsKey("SizeOnDisk") ? st.Long("SizeOnDisk", -1) : -1,
                StateFlags = flags,
                LastPlayedUnix = st.Long("LastPlayed"),
                UpdatePending = IsUpdatePending(flags, st.Long("BytesToDownload"), st.Long("BytesDownloaded"),
                    st.Long("UpdateResult"), st.Long("buildid"), st.Long("TargetBuildID")),
            };
        }

        public static bool IsDigits(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (char c in s) if (c < '0' || c > '9') return false;
            return true;
        }
    }
}
