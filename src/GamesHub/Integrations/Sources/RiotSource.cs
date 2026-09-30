// from C:\ProgramData\Riot Games (read-only). Launch goes through RiotClientServices.exe.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace GamesHub
{
    public sealed class RiotSource : IExtraSource
    {
        public const string ClientFileName = "RiotClientServices.exe";

        private readonly string _riotDataDir;
        private readonly Func<string, bool> _isSignedByRiot;

        public RiotSource() : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Riot Games")) { }

        /// <summary>For tests / alternative roots: the folder containing RiotClientInstalls.json and Metadata\.
        /// <paramref name="isSignedByRiot"/> replaces the Authenticode check (default: valid signature by Riot Games).</summary>
        public RiotSource(string riotDataDir, Func<string, bool> isSignedByRiot = null)
        {
            _riotDataDir = riotDataDir ?? "";
            _isSignedByRiot = isSignedByRiot ?? Authenticode.IsRiotSigned;
        }

        public string Name => "riot";

        public List<Game> Scan()
        {
            var games = new List<Game>();
            try
            {
                string metaDir = Path.Combine(_riotDataDir, "Metadata");
                if (!Directory.Exists(metaDir)) return games;

                var clients = ReadClientPaths(Path.Combine(_riotDataDir, "RiotClientInstalls.json"));
                // RiotClientInstalls.json lives in ProgramData, writable by any local user: the path it names is
                // launched by us, so only a local, Riot-signed RiotClientServices.exe is accepted.
                string client = FirstTrusted(clients, _isSignedByRiot);
                if (client == "")
                {
                    Log.Warn("RiotSource: no trusted RiotClientServices.exe (missing, not local or not signed by Riot Games); skipping Riot games");
                    return games;
                }

                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string dir in Directory.GetDirectories(metaDir))
                {
                    try
                    {
                        Game g = ReadProduct(dir, client);
                        if (g != null && seen.Add(g.Id)) games.Add(g);
                    }
                    // Resilience boundary: per-product parse of untrusted third-party Riot files; one bad product must not stop the scan.
                    catch (Exception ex) { Log.Warn("RiotSource: failed reading " + dir, ex); }
                }
            }
            catch (Exception ex) when (ExpectedErrors.IsFileSystem(ex)) { Log.Warn("RiotSource: scan failed", ex); }
            return games;
        }

        private static Game ReadProduct(string dir, string client)
        {
            string dirName = Path.GetFileName(dir);
            if (!RiotCatalog.TrySplitProductDir(dirName, out string product, out string patchline)) return null;
            string yamlFile = Path.Combine(dir, dirName + ".product_settings.yaml");
            if (!File.Exists(yamlFile)) return null;   // product known to the client but not installed

            var y = FlatYaml.Parse(File.ReadAllText(yamlFile, Encoding.UTF8));
            y.TryGetValue("product_install_full_path", out string full);
            string installDir = RiotCatalog.NormalizePath(full);
            // A UNC/relative install path is never probed (would contact an SMB server named by the file).
            if (!SafePath.IsLocalAbsolute(installDir) || !Directory.Exists(installDir)) return null;

            y.TryGetValue("shortcut_name", out string shortcut);
            string exe = "";
            foreach (string c in RiotCatalog.ExeCandidates(product, installDir))
                if (File.Exists(c)) { exe = c; break; }

            return new Game
            {
                Id = RiotCatalog.GameId(product, patchline),
                Name = RiotCatalog.DisplayName(product, patchline, shortcut),
                Source = "riot",
                Platform = "Riot",
                LaunchTarget = client,
                LaunchArgs = RiotCatalog.LaunchArgs(product, patchline),
                InstallDir = installDir,
                Exe = exe,
                AddedAt = SafeCreationTime(yamlFile),
            };
        }

        /// <summary>RiotClientServices.exe candidates from RiotClientInstalls.json, in preference order.</summary>
        public static List<string> ReadClientPaths(string installsJson)
        {
            var list = new List<string>();
            try
            {
                if (!File.Exists(installsJson)) return list;
                var d = Json.DeserializeObject(File.ReadAllText(installsJson, Encoding.UTF8)) as IDictionary<string, object>;
                list.AddRange(ParseClientPaths(d));
            }
            catch (Exception ex) when (ExpectedErrors.IsFileOrJson(ex)) { Log.Warn("RiotSource: cannot read " + installsJson, ex); }
            return list;
        }

        public static List<string> ParseClientPaths(IDictionary<string, object> d)
        {
            var list = new List<string>();
            if (d == null) return list;
            void Add(string p) { p = RiotCatalog.NormalizePath(p); if (p != "" && !list.Contains(p)) list.Add(p); }
            Add(Json.Str(d, "rc_live"));
            Add(Json.Str(d, "rc_default"));
            foreach (var m in new[] { "patchlines", "associated_client" }.Select(section => Json.Obj(d, section)).Where(m => m != null))
            {
                foreach (var kv in m) Add(Convert.ToString(kv.Value));
            }
            return list;
        }

        /// <summary>First candidate that is a local absolute path to an existing RiotClientServices.exe accepted by
        /// <paramref name="isSignedByRiot"/>; "" when none.</summary>
        public static string FirstTrusted(IEnumerable<string> paths, Func<string, bool> isSignedByRiot)
        {
            foreach (string p in paths ?? Enumerable.Empty<string>())
            {
                if (IsTrustedClient(p, isSignedByRiot)) return p;
            }
            return "";
        }

        public static bool IsTrustedClient(string path, Func<string, bool> isSignedByRiot)
        {
            if (!SafePath.IsLocalAbsolute(path) || isSignedByRiot == null) return false;
            string name;
            try { name = Path.GetFileName(path); }
            catch (ArgumentException ex) { Log.Warn("RiotSource: bad client path " + path, ex); return false; }
            if (!string.Equals(name, ClientFileName, StringComparison.OrdinalIgnoreCase)) return false;
            if (!File.Exists(path)) return false;
            if (isSignedByRiot(path)) return true;
            Log.Warn("RiotSource: ignoring " + path + " (Authenticode signature missing, invalid or not from Riot Games)");
            return false;
        }

        private static DateTime SafeCreationTime(string file)
        {
            try { return File.GetCreationTime(file); }
            catch (Exception ex) when (ExpectedErrors.IsFileSystem(ex)) { Log.Warn("RiotSource: no creation time for " + file, ex); return DateTime.Now; }
        }
    }
}
