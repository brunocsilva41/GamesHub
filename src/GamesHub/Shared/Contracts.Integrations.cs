// Integration services (Steam local data, install health, PCGamingWiki, store metadata, automation, variants,
// extra sources, quick launch). Each is implemented in its own folder and must not depend on the others;
// GameCatalog wires them into the library and the bridge.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GamesHub
{
    // ================================================================ Steam local data (STEAMDATA)
    // Implemented by Integrations/Steam/SteamLocalData.cs — ctor: SteamLocalData()

    public sealed class SteamLocalStats
    {
        public string AppId = "";
        public long PlaytimeMinutes;       // total, from userdata/<id>/config/localconfig.vdf
        public DateTime? LastPlayed;       // from localconfig "LastPlayed" (unix seconds)
        public bool UpdatePending;         // from appmanifest StateFlags / UpdateResult / BytesToDownload
        public long SizeOnDisk = -1;       // from appmanifest "SizeOnDisk"
        public string InstallDir = "";
    }

    public interface ISteamLocalData
    {
        /// <summary>Reads every Steam library + the most recently active Steam user (loginusers.vdf).
        /// Keyed by appid. Cheap (file reads only); cache by file mtimes. Never throws.</summary>
        Dictionary<string, SteamLocalStats> Load();
        string ValidateUri(string appId);   // steam://validate/<id>
        string UninstallUri(string appId);  // steam://uninstall/<id>
    }

    // ================================================================ Install health & disk (INSTALL)
    // Implemented by Integrations/Install/InstallInspector.cs — ctor: InstallInspector()

    public sealed class InstallHealth
    {
        public bool Broken;
        public string Reason = "";   // pt-BR, e.g. "O arquivo de destino do atalho não existe mais."
    }

    public sealed class DriveSpace
    {
        public string Name = "";     // "C:\"
        public string Label = "";
        public long TotalBytes;
        public long FreeBytes;
    }

    public sealed class UninstallInfo
    {
        /// <summary>"steam" | "epic" | "registry" | "none"</summary>
        public string Method = "none";
        public string Command = "";      // URI or UninstallString
        public string DisplayName = "";
    }

    public interface IInstallInspector
    {
        /// <summary>Fast (no recursion): checks shortcut target / exe / InstallDir existence.
        /// URI-only games (steam://, com.epicgames...) are never "broken" here.</summary>
        InstallHealth CheckHealth(Game game);
        /// <summary>Folder size of game.InstallDir (background, cached in cache/sizes.json by dir+mtime).
        /// Returns -1 when unknown. Must never block the UI thread.</summary>
        Task<long> GetSizeBytesAsync(Game game);
        long GetCachedSizeBytes(Game game);
        List<DriveSpace> GetDrives();
        /// <summary>Steam → steam://uninstall, Epic → opens launcher library, others → HKCU/HKLM
        /// Uninstall registry entries matched by InstallLocation / DisplayIcon / name.</summary>
        UninstallInfo FindUninstaller(Game game);
        /// <summary>Runs the uninstaller (the UI has already confirmed with the user).</summary>
        OpResult RunUninstaller(UninstallInfo info);
    }

    // ================================================================ PCGamingWiki (PCGW)
    // Implemented by Integrations/Pcgw/PcgwService.cs — ctor: PcgwService(AppSettings settings)

    public sealed class ResolvedPath
    {
        public string Raw = "";      // as written on the wiki, e.g. {{p|appdata}}\Foo\saves
        public string Path = "";     // expanded Windows path ("" if it could not be expanded)
        public bool Exists;
        public string Kind = "";     // "save" | "config"
    }

    public sealed class PcgwInfo
    {
        public bool Found;
        public string PageUrl = "";          // https://www.pcgamingwiki.com/wiki/<Page>
        public string Title = "";
        public List<ResolvedPath> SaveLocations = new List<ResolvedPath>();
        public List<ResolvedPath> ConfigLocations = new List<ResolvedPath>();
    }

    public interface IPcgwService
    {
        /// <summary>Best URL without network: by Steam appid (Special:Search / AppID redirect) or by name.</summary>
        string PageUrl(Game game, string steamAppId);
        /// <summary>Network + cache (cache/pcgw/, 14 days). Never throws; Found=false on failure.</summary>
        Task<PcgwInfo> GetInfoAsync(Game game, string steamAppId);
    }

    // ================================================================ Game metadata (META)
    // Implemented by Integrations/Metadata/MetadataService.cs — ctor: MetadataService(AppSettings settings)

    public sealed class GameInfo
    {
        public string AppId = "";
        public List<string> Genres = new List<string>();        // pt-BR names from the store
        public List<string> Categories = new List<string>();    // "Multijogador", "Cooperativo", "Suporte a controle"...
        public string ShortDescription = "";
        public string ReleaseDate = "";
        public List<string> Developers = new List<string>();
        public List<string> Publishers = new List<string>();
        public int Metacritic;                                   // 0 = none
        public string Website = "";
        public bool ControllerSupport;
        public DateTime FetchedAt;
    }

    public interface IMetadataService
    {
        GameInfo GetCached(string appId);          // null when not cached
        Task<GameInfo> FetchAsync(string appId);   // network + cache (cache/meta/<appid>.json, 30 days)
        /// <summary>Queues background fetches (rate-limited, ~1 req/1.5s) and raises MetadataUpdated per appid.</summary>
        void Prefetch(IEnumerable<string> appIds);
        event Action<string> MetadataUpdated;
    }

    // ================================================================ Before/after actions (AUTO)
    // Implemented by Integrations/Automation/AutomationService.cs — ctor: AutomationService()

    public sealed class AutomationAction
    {
        /// <summary>"run" | "close" | "powerPlan" | "audioDevice" | "resolution" | "wait"</summary>
        public string Type = "";
        public string Target = "";     // run: exe/path/URI; close: process name; powerPlan: GUID; audioDevice: device id; resolution: "1920x1080@60"
        public string Args = "";       // run: arguments
        public int Seconds;            // wait
        public bool Enabled = true;
    }

    public sealed class AutomationProfile
    {
        public bool Enabled = true;
        public bool UseDefault = true;                               // also run the global default profile
        public List<AutomationAction> Before = new List<AutomationAction>();
        /// <summary>Runs when the tracked game process exits. Settings changed by "Before"
        /// (power plan, audio device, resolution) are restored automatically first.</summary>
        public List<AutomationAction> After = new List<AutomationAction>();
    }

    public sealed class NamedOption
    {
        public string Id = "";
        public string Name = "";
        public bool Current;
    }

    public interface IAutomationService
    {
        AutomationProfile GetProfile(string gameId);            // never null (empty profile)
        OpResult SaveProfile(string gameId, AutomationProfile profile);
        AutomationProfile GetDefaultProfile();
        OpResult SaveDefaultProfile(AutomationProfile profile);
        /// <summary>Called by the bridge right before library.Launch(id). Must finish fast (&lt; ~3s incl. waits the user set).</summary>
        Task RunBeforeAsync(Game game);
        /// <summary>Called by the integration layer when LIB raises RunningChanged(id,false).</summary>
        Task RunAfterAsync(Game game);
        List<NamedOption> ListPowerPlans();
        List<NamedOption> ListAudioDevices();       // playback devices
        List<NamedOption> ListResolutions();        // primary display modes "1920x1080@60"
    }

    // ================================================================ Launch variants (VARIANTS)
    // Implemented by Integrations/Variants/VariantService.cs — ctor: VariantService()

    public sealed class VariantGroup
    {
        public string Id = "";
        public string PrimaryId = "";
        public List<string> MemberIds = new List<string>();          // includes PrimaryId
        public Dictionary<string, string> Labels = new Dictionary<string, string>(); // memberId -> label
    }

    public interface IVariantService
    {
        /// <summary>Suggested groups by name similarity (e.g. "X" + "X DirectX 11"), excluding games
        /// already grouped or suggestions the user dismissed.</summary>
        List<VariantGroup> Suggest(List<Game> games);
        List<VariantGroup> GetGroups();
        OpResult Group(List<string> memberIds, string primaryId);
        OpResult Ungroup(string groupId);
        OpResult DismissSuggestion(List<string> memberIds);
        OpResult SetLabel(string memberId, string label);
        OpResult SetPrimary(string groupId, string primaryId);
        /// <summary>Collapses groups: returns the list with non-primary members removed and each primary's
        /// Variants filled (primary first). Play time/last played of the primary = aggregate of members.
        /// Does not mutate the input games.</summary>
        List<Game> Apply(List<Game> games);
    }

    // ================================================================ Extra sources (SOURCES)
    // Implemented by Integrations/Sources/RiotSource.cs (ctor RiotSource()) and HydraSource.cs (ctor HydraSource())

    public interface IExtraSource
    {
        string Name { get; }           // "riot" | "hydra"
        /// <summary>Installed games only. Ids "riot:&lt;product&gt;" / "hydra:&lt;objectId or shop:id&gt;".
        /// Fill Name, Source, Platform, LaunchTarget (+LaunchArgs if needed), InstallDir, Exe, SteamAppId when known.
        /// Read-only; never throws (log and return what you have).</summary>
        List<Game> Scan();
    }

    // ================================================================ Quick launch (QUICK)
    // Implemented by QuickLaunch/QuickLaunchController.cs —
    // ctor: QuickLaunchController(ILibraryService library, AppSettings settings)

    public interface IQuickLaunch
    {
        /// <summary>Registers the hotkey (settings.QuickLaunchHotkey) on the UI thread. Returns false +
        /// message when the combination is taken by another program.</summary>
        OpResult Start();
        OpResult ApplyHotkey(string hotkey);
        void Show();
        void Dispose();
    }
}
