// Hydra 3.x+ keeps its data in a LevelDB folder (%APPDATA%\hydralauncher\hydra-db) with keys
// "!games!<shop>:<objectId>" → JSON. Older versions used SQLite (hydra.db), which is not supported.
using System;
using System.Collections.Generic;
using System.IO;

namespace GamesHub
{
    public sealed class HydraSource : IExtraSource
    {
        private readonly string _dataDir;

        public HydraSource() : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "hydralauncher")) { }

        /// <summary>For tests / alternative roots: the folder containing hydra-db\.</summary>
        public HydraSource(string dataDir) { _dataDir = dataDir ?? ""; }

        public string Name => "hydra";

        public List<Game> Scan()
        {
            var games = new List<Game>();
            try
            {
                string db = Path.Combine(_dataDir, "hydra-db");
                if (!Directory.Exists(db))
                {
                    if (File.Exists(Path.Combine(_dataDir, "hydra.db")))
                        Log.Info("HydraSource: only the legacy SQLite library (hydra.db) was found; not supported");
                    return games;
                }

                var records = LevelDbReader.ReadAll(db, HydraRecords.GamesPrefix);
                foreach (var kv in records)
                {
                    try
                    {
                        Game g = HydraRecords.Parse(kv.Key, kv.Value, File.Exists);
                        if (g != null) games.Add(g);
                    }
                    catch (Exception ex) { Log.Warn("HydraSource: bad record " + kv.Key, ex); }
                }
                games.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex) { Log.Warn("HydraSource: scan failed", ex); }
            return games;
        }
    }
}
