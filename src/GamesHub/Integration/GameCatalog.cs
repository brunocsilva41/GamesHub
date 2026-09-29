// Joins the library with the standalone integration services: Steam local stats, install health/size,
// store metadata, launch variants and before/after automation. The bridge, tray and quick launch read
// games through here so every surface sees the same enriched list.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace GamesHub
{
    public sealed class GameCatalog : IDisposable
    {
        private static readonly TimeSpan HealthTtl = TimeSpan.FromSeconds(60);

        public readonly AppSettings Settings;
        public readonly ILibraryService Library;
        public readonly IArtworkService Art;
        public readonly ISteamLocalData Steam;
        public readonly IInstallInspector Install;
        public readonly IMetadataService Meta;
        public readonly IPcgwService Pcgw;
        public readonly IAutomationService Automation;
        public readonly IVariantService Variants;

        /// <summary>Raised (any thread) when the enriched list may have changed.</summary>
        public event Action Changed;

        private readonly ConcurrentDictionary<string, (DateTime at, InstallHealth health)> _health =
            new ConcurrentDictionary<string, (DateTime, InstallHealth)>();
        private readonly ConcurrentDictionary<string, byte> _sizing = new ConcurrentDictionary<string, byte>();
        private readonly ConcurrentDictionary<string, byte> _prefetched = new ConcurrentDictionary<string, byte>();
        private readonly ConcurrentDictionary<string, byte> _automationActive = new ConcurrentDictionary<string, byte>();
        private readonly HydraSource _hydra = new HydraSource();
        private HashSet<string> _hydraInstalled;
        private DateTime _hydraAt;
        private volatile bool _disposed;

        public GameCatalog(AppSettings settings, ILibraryService library, IArtworkService art)
        {
            Settings = settings;
            Library = library;
            Art = art;
            Steam = new SteamLocalData();
            Install = new InstallInspector();
            Meta = new MetadataService(settings);
            Pcgw = new PcgwService(settings);
            Automation = new AutomationService();
            Variants = new VariantService();

            if (library is LibraryService concrete)
            {
                concrete.AddExtraSource(new RiotSource());
                concrete.AddExtraSource(new HydraSource());
            }
            library.Changed += RaiseChanged;
            library.RunningChanged += OnRunningChanged;
            Meta.MetadataUpdated += _ => RaiseChanged();
            if (Variants is VariantService vs) vs.Changed += RaiseChanged;
        }

        /// <summary>Call once before library.Start(): restores settings left changed by a crash mid-game.</summary>
        public void RecoverAutomation()
        {
            try
            {
                if (Automation is AutomationService a)
                {
                    int n = a.RestorePendingOnStartup();
                    if (n > 0) Log.Info("Automation: restored " + n + " pending setting(s) after an unclean exit");
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Automation crash-restore failed", ex);
            }
        }

        private void RaiseChanged()
        {
            if (!_disposed) Changed?.Invoke();
        }

        // ------------------------------------------------------------------ read

        /// <summary>Enriched + variant-collapsed snapshot. Touches the disk lightly; call off the UI thread.</summary>
        public List<Game> GetGames()
        {
            List<Game> games = Library.GetGames() ?? new List<Game>();
            Dictionary<string, SteamLocalStats> steam = SafeSteamStats();
            var wantMeta = new List<string>();

            foreach (Game g in games)
            {
                ApplySteam(g, steam);
                ApplyHealth(g);
                ApplySize(g);
                string appId = MatchedAppId(g);
                if (appId.Length > 0)
                {
                    GameInfo info = SafeMeta(appId);
                    if (info != null) g.Genres = info.Genres?.ToList() ?? new List<string>();
                    else if (_prefetched.TryAdd(appId, 0)) wantMeta.Add(appId);
                }
            }
            if (wantMeta.Count > 0 && Settings.FetchMetadata) Meta.Prefetch(wantMeta);

            try
            {
                return Variants.Apply(games);
            }
            catch (Exception ex)
            {
                Log.Warn("Variant grouping failed; showing ungrouped list", ex);
                return games;
            }
        }

        /// <summary>A single game, enriched but not collapsed (details page / member of a variant group).</summary>
        public Game Get(string id)
        {
            Game g = Library.Get(id);
            if (g == null) return null;
            ApplySteam(g, SafeSteamStats());
            ApplyHealth(g);
            ApplySize(g);
            string appId = MatchedAppId(g);
            GameInfo info = appId.Length > 0 ? SafeMeta(appId) : null;
            if (info != null) g.Genres = info.Genres?.ToList() ?? new List<string>();
            return g;
        }

        /// <summary>Steam app id for store/wiki lookups: explicit, else the artwork matcher's result.</summary>
        public string MatchedAppId(Game g)
        {
            if (g.SteamAppId == ArtKind.NotOnSteam) return "";
            if (!string.IsNullOrEmpty(g.SteamAppId)) return g.SteamAppId;
            try
            {
                return (Art as ArtworkService)?.GetMatchedSteamAppId(g) ?? "";
            }
            catch (Exception ex)
            {
                Log.Warn("GetMatchedSteamAppId failed: " + g.Id, ex);
                return "";
            }
        }

        private Dictionary<string, SteamLocalStats> SafeSteamStats()
        {
            try
            {
                return Steam.Load() ?? new Dictionary<string, SteamLocalStats>();
            }
            catch (Exception ex)
            {
                Log.Warn("Steam local data failed", ex);
                return new Dictionary<string, SteamLocalStats>();
            }
        }

        private GameInfo SafeMeta(string appId)
        {
            try { return Meta.GetCached(appId); }
            catch (Exception ex) { Log.Warn("Metadata cache read failed: " + appId, ex); return null; }
        }

        private void ApplySteam(Game g, Dictionary<string, SteamLocalStats> steam)
        {
            // Local Steam data is only consulted when the user lets GamesHub use Steam.
            if (!Settings.ImportSteam && !Settings.ImportSteamPlaytime) return;
            if (string.IsNullOrEmpty(g.SteamAppId) || !steam.TryGetValue(g.SteamAppId, out SteamLocalStats s)) return;
            g.UpdatePending = s.UpdatePending;
            if (s.SizeOnDisk > 0) g.SizeBytes = s.SizeOnDisk;
            if (!Settings.ImportSteamPlaytime) return;
            // Steam already counts sessions started from GamesHub: take the max, never the sum.
            g.PlaySeconds = Math.Max(g.PlaySeconds, s.PlaytimeMinutes * 60);
            if (s.LastPlayed.HasValue && (!g.LastPlayed.HasValue || s.LastPlayed.Value > g.LastPlayed.Value))
                g.LastPlayed = s.LastPlayed;
        }

        private void ApplyHealth(Game g)
        {
            string key = g.Id + "|" + g.FilePath + "|" + g.LaunchTarget + "|" + g.InstallDir;
            InstallHealth h;
            if (_health.TryGetValue(key, out var cached) && DateTime.UtcNow - cached.at < HealthTtl) h = cached.health;
            else
            {
                try { h = Install.CheckHealth(g) ?? new InstallHealth(); }
                catch (Exception ex) { Log.Warn("Health check failed: " + g.Id, ex); h = new InstallHealth(); }
                _health[key] = (DateTime.UtcNow, h);
            }
            g.Broken = h.Broken;
            g.BrokenReason = h.Reason ?? "";
            if (!g.Broken && IsUninstalledHydraShortcut(g))
            {
                g.Broken = true;
                g.BrokenReason = "Este jogo não está mais instalado no Hydra.";
            }
        }

        /// <summary>Hydra shortcuts launch Hydra.exe (which exists) even when the game itself is gone, so the
        /// file check can't see it: compare the objectId with the games Hydra reports as installed.</summary>
        private bool IsUninstalledHydraShortcut(Game g)
        {
            if (g.Source != "folder" || string.IsNullOrEmpty(g.SteamAppId) || !Settings.ImportHydra) return false;
            if ((g.LaunchTarget + " " + g.LaunchArgs).IndexOf("hydralauncher://", StringComparison.OrdinalIgnoreCase) < 0) return false;
            HashSet<string> installed = HydraInstalled();
            return installed.Count > 0 && !installed.Contains(g.SteamAppId);
        }

        private HashSet<string> HydraInstalled()
        {
            lock (_hydra)
            {
                if (_hydraInstalled == null || DateTime.UtcNow - _hydraAt > HealthTtl)
                {
                    try { _hydraInstalled = new HashSet<string>(_hydra.Scan().Select(x => x.SteamAppId).Where(x => !string.IsNullOrEmpty(x))); }
                    catch (Exception ex) { Log.Warn("Hydra scan for health failed", ex); _hydraInstalled = new HashSet<string>(); }
                    _hydraAt = DateTime.UtcNow;
                }
                return _hydraInstalled;
            }
        }

        private void ApplySize(Game g)
        {
            if (g.SizeBytes >= 0 || g.Broken) return;
            long cached = -1;
            try { cached = Install.GetCachedSizeBytes(g); }
            catch (Exception ex) { Log.Warn("Size cache read failed: " + g.Id, ex); }
            if (cached >= 0)
            {
                g.SizeBytes = cached;
                return;
            }
            if (string.IsNullOrEmpty(g.InstallDir) && string.IsNullOrEmpty(g.Exe)) return;
            if (!_sizing.TryAdd(g.Id, 0)) return; // one attempt per session
            Game copy = g;
            Task.Run(async () =>
            {
                try
                {
                    long size = await Install.GetSizeBytesAsync(copy).ConfigureAwait(false);
                    if (size >= 0) RaiseChanged();
                }
                catch (Exception ex)
                {
                    Log.Warn("Size computation failed: " + copy.Id, ex);
                }
            });
        }

        // ------------------------------------------------------------------ launch / automation

        /// <summary>Runs the "before" automation, then launches (the variant member when given).</summary>
        public async Task<OpResult> LaunchAsync(string id, string variantId)
        {
            string target = string.IsNullOrEmpty(variantId) ? id
                : (Variants as VariantService)?.ResolveLaunchId(id, variantId) ?? id;
            Game g = Library.Get(target);
            if (g == null) return OpResult.Fail("Jogo não encontrado.");

            if (Settings.AutomationEnabled)
            {
                try
                {
                    await Automation.RunBeforeAsync(g).ConfigureAwait(false);
                    _automationActive[g.Id] = 0;
                }
                catch (Exception ex)
                {
                    Log.Warn("Automation (before) failed: " + g.Id, ex);
                }
            }
            return Library.Launch(target);
        }

        private void OnRunningChanged(string id, bool running)
        {
            if (running || !_automationActive.TryRemove(id, out _)) return;
            Game g = Library.Get(id);
            if (g == null) return;
            Task.Run(async () =>
            {
                try { await Automation.RunAfterAsync(g).ConfigureAwait(false); }
                catch (Exception ex) { Log.Warn("Automation (after) failed: " + id, ex); }
            });
        }

        /// <summary>Removes every broken folder game (each moved to trash, undoable).</summary>
        public List<OpResult> CleanupBroken()
        {
            return (Library.GetGames() ?? new List<Game>())
                .Where(g => { ApplyHealth(g); return g.Broken; })
                .Select(g => Library.Remove(g.Id))
                .ToList();
        }

        public void Dispose()
        {
            _disposed = true;
            Library.Changed -= RaiseChanged;
            Library.RunningChanged -= OnRunningChanged;
            (Meta as IDisposable)?.Dispose();
            (Automation as IDisposable)?.Dispose();
        }
    }
}
