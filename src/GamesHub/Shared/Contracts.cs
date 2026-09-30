// Every module compiles against these types; change them deliberately (they are the modules' shared API).
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GamesHub
{
    // ------------------------------------------------------------------ model

    /// <summary>A game in the library, after merging its source data with user overrides.</summary>
    public sealed class Game
    {
        /// <summary>Stable id: "folder:&lt;file name lowercased&gt;", "steam:&lt;appid&gt;", "epic:&lt;AppName&gt;".</summary>
        public string Id = "";
        public string Name = "";
        /// <summary>"folder" | "steam" | "epic"</summary>
        public string Source = "folder";
        /// <summary>"Steam","Epic","Riot","Roblox","Minecraft","Battle.net","EA","Ubisoft","GOG","PC"</summary>
        public string Platform = "PC";
        /// <summary>File path or URI that Launcher executes (e.g. the .lnk, or steam://rungameid/730).</summary>
        public string LaunchTarget = "";
        /// <summary>User-provided extra arguments ("" = none).</summary>
        public string LaunchArgs = "";
        /// <summary>For folder source: full path of the .lnk/.url/.exe in the games folder. Otherwise "".</summary>
        public string FilePath = "";
        /// <summary>".lnk" | ".url" | ".exe" | "" (imported games)</summary>
        public string Ext = "";
        /// <summary>Install directory when known (used for play-time process detection). "" if unknown.</summary>
        public string InstallDir = "";
        /// <summary>Resolved game executable when known. "" if unknown.</summary>
        public string Exe = "";
        /// <summary>Steam app id used for artwork ("" if unknown). May be a user override.</summary>
        public string SteamAppId = "";
        public bool Favorite;
        public bool Hidden;
        public List<string> Collections = new List<string>();
        public DateTime? LastPlayed;
        public long PlaySeconds;
        public DateTime AddedAt = DateTime.Now;
        public bool Running;
        public Artwork Art = new Artwork();

        // ---- Integration fields (filled by GameCatalog, not by the library) ----
        /// <summary>Size on disk in bytes; -1 = unknown / not computed yet.</summary>
        public long SizeBytes = -1;
        public bool UpdatePending;
        /// <summary>Shortcut target / install folder no longer exists.</summary>
        public bool Broken;
        public string BrokenReason = "";
        public List<string> Genres = new List<string>();
        /// <summary>Other launch variants grouped under this game (e.g. "DirectX 11"). Empty = none.</summary>
        public List<GameVariant> Variants = new List<GameVariant>();
    }

    public sealed class GameVariant
    {
        public string Id = "";     // id of the member Game
        public string Label = "";  // e.g. "DirectX 11", "Plutonium", "Padrão"
    }

    /// <summary>
    /// Artwork paths RELATIVE to AppPaths.ArtDir, using forward slashes, e.g. "steam-730/hero.jpg".
    /// null/"" = not available. The bridge turns them into https://art.gameshub.example/... URLs.
    /// </summary>
    public sealed class Artwork
    {
        public string Header;   // wide 460x215 (grid card, landscape)
        public string Capsule;  // portrait 600x900 (grid card, portrait style)
        public string Hero;     // very wide background (hero banner / details page)
        public string Logo;     // transparent title logo (drawn over hero)
        public string Icon;     // square icon extracted from exe/lnk (fallback)
    }

    /// <summary>Partial edit of a game. null = leave unchanged.</summary>
    public sealed class GameEdit
    {
        public string Name;          // "" = reset to original name
        public string LaunchArgs;
        public string SteamAppId;    // "" = clear override
        public bool? Favorite;
        public bool? Hidden;
        public List<string> Collections;
    }

    public sealed class OpResult
    {
        public bool Ok;
        public string Message = "";   // pt-BR, user-facing
        public string UndoToken;      // non-null when the operation can be undone
        public string GameId;
        public static OpResult Success(string msg, string gameId = null, string undo = null)
            => new OpResult { Ok = true, Message = msg, GameId = gameId, UndoToken = undo };
        public static OpResult Fail(string msg) => new OpResult { Ok = false, Message = msg };
    }

    public sealed class SteamSearchResult
    {
        public string AppId = "";
        public string Name = "";
        public string IconUrl = "";
    }

    public sealed class UpdateInfo
    {
        public bool Available;
        public string Version = "";
        public string Notes = "";
        public string PageUrl = "";
        public string DownloadUrl = "";
    }

    // ------------------------------------------------------------------ settings

    /// <summary>User settings, persisted by SettingsStore (CORE) as JSON in AppPaths.SettingsFile.
    /// Services receive the live instance and read fields when needed.</summary>
    public sealed class AppSettings
    {
        public string GamesDir = "";
        public bool ImportSteam = true;
        public bool ImportEpic = true;
        /// <summary>After launching a game: "tray" | "minimize" | "none"</summary>
        public string OnLaunch = "tray";
        public bool StartWithWindows = false;
        public bool StartMinimized = false;
        public bool CloseToTray = true;
        public bool HotkeyEnabled = true;
        public string Hotkey = "Ctrl+Alt+G";
        /// <summary>"name" | "recent" | "playtime" | "added"</summary>
        public string SortBy = "name";
        /// <summary>"grid" | "list"</summary>
        public string View = "grid";
        /// <summary>"landscape" | "portrait"</summary>
        public string CardStyle = "landscape";
        public bool ReduceMotion = false;
        public bool AutoArtwork = true;
        public string SteamGridDbKey = "";
        public bool TrackPlaytime = true;
        public bool CheckUpdates = true;
        /// <summary>GitHub "owner/repo" whose Releases feed the updater (an empty value means the official
        /// repository; turn updates off with CheckUpdates).</summary>
        public string UpdateRepo = AppInfo.DefaultUpdateRepo;
        public string Language = "pt-BR";

        // ---- Integrations ----
        public bool ImportRiot = true;
        public bool ImportHydra = true;
        /// <summary>Import play time / last played from Steam's local files.</summary>
        public bool ImportSteamPlaytime = true;
        /// <summary>Fetch game info (genres, description...) from the Steam store.</summary>
        public bool FetchMetadata = true;
        /// <summary>Fetch save/config locations from PCGamingWiki.</summary>
        public bool PcgwEnabled = true;
        public bool QuickLaunchEnabled = true;
        public string QuickLaunchHotkey = "Ctrl+Shift+Space";
        public bool AutomationEnabled = true;
        /// <summary>Start Steam silently and minimize launcher windows (Steam, Epic, Riot…) when a game starts.</summary>
        public bool MinimizeLaunchers = true;
    }

    // ------------------------------------------------------------------ services

    /// <summary>Implemented by Artwork/ArtworkService.cs (ART). ctor: ArtworkService(AppSettings settings)</summary>
    public interface IArtworkService
    {
        /// <summary>Returns whatever art is cached right now (never blocks on network) and schedules
        /// missing downloads in the background. Raises ArtworkUpdated(game.Id) when new files land.</summary>
        Artwork Resolve(Game game);
        event Action<string> ArtworkUpdated;
        /// <summary>kind: "header" | "capsule" | "hero" | "logo" | "icon". Copies/normalizes the image.</summary>
        OpResult SetCustomImage(Game game, string kind, string sourceFile);
        OpResult ClearCustomImage(Game game, string kind);
        /// <summary>Drops cached (non-custom) art for the game and re-downloads.</summary>
        void Refresh(Game game);
        Task<List<SteamSearchResult>> SearchSteamAsync(string query);
        /// <summary>One-time copy of v1 covers (legacyHubDir\covers\&lt;appid&gt;.jpg) into the new cache.</summary>
        void ImportLegacy(string legacyHubDir);
    }

    /// <summary>Implemented by Library/LibraryService.cs (LIB). ctor: LibraryService(AppSettings settings, IArtworkService art)</summary>
    public interface ILibraryService
    {
        /// <summary>Snapshot copy; safe to enumerate from any thread.</summary>
        List<Game> GetGames();
        Game Get(string id);
        List<string> GetCollections();
        /// <summary>Raised (debounced ~300ms, any thread) when games, metadata, art, running state or play time change.</summary>
        event Action Changed;
        /// <summary>Raised when a tracked game process starts (true) or exits (false).</summary>
        event Action<string, bool> RunningChanged;
        /// <summary>Loads library.json, imports v1 data (playlog) once, scans all sources, starts watchers/trackers.</summary>
        void Start();
        void Rescan();
        OpResult Update(string id, GameEdit edit);
        /// <summary>Adds an .exe/.lnk/.url by creating a shortcut in GamesDir (folder source).</summary>
        OpResult AddFromFile(string path, string name);
        OpResult AddSteamApp(string appId, string name);
        /// <summary>Folder games: moved to AppPaths.TrashDir (never deleted). Imported games: hidden via ignore list.</summary>
        OpResult Remove(string id);
        OpResult Undo(string undoToken);
        OpResult Launch(string id);
        /// <summary>Folder that should be revealed in Explorer for this game ("" if none).</summary>
        string RevealPath(string id);
        void Dispose();
    }

    /// <summary>Implemented by Update/UpdateChecker.cs (DIST). ctor: UpdateChecker(AppSettings settings)</summary>
    public interface IUpdateChecker
    {
        Task<UpdateInfo> CheckAsync();
        /// <summary>Downloads the installer and starts it; returns false on failure. progress: 0..100</summary>
        Task<bool> DownloadAndInstallAsync(UpdateInfo info, Action<int> progress);
    }
}
