// GameRootResolver — finds a game's install root from its executable.
//
// Shortcuts and loose executables only tell us where the .exe lives, and for many games that is a sub-folder
// of the install (Unreal: <Root>\<Project>\Binaries\Win64\x.exe; others: <Root>\bin\x64, <Root>\Game, ...).
// Disk usage, play-time tracking (processes under InstallDir) and "Abrir local" need the real root.
//
// Algorithm (deterministic, no network, one directory listing per visited level, at most MaxLevels levels):
//  0. exe folder = start. It must be usable (see "never" below) and must not be the games folder itself
//     (a loose exe there has no install of its own) → otherwise "".
//     A declared install dir that contains the exe (and is usable) is authoritative and returned as is.
//  1. At each level (current folder C, its parent P):
//     a. C has a HARD root marker → return C:
//          unins*.exe / uninstall*.exe, installscript*.vdf, .egstore\, goggame-*.info, MicrosoftGame.config,
//          a Unity pair (X.exe + X_Data\), an Unreal root (Engine\ + a <Project>\Binaries\), or C is the
//          InstallLocation of a Windows "Uninstall" registry entry.
//     b. P is not usable → return C.
//     c. Unreal: C is Binaries\<Platform> → climb; C is Binaries → climb to the project; C is a project
//        (came from Binaries, or has Binaries\ + Content\) → climb to P when P shows it belongs to the same
//        game (Engine\, an .exe, a root marker, or — for paths that no longer exist — always).
//     d. C has a SOFT root marker → return C: steam_appid.txt, *.pak, Content\Paks\, version.txt/*.ver next to
//        many files. (Soft because cracks drop steam_appid.txt into Binaries\Win64, hence checked after c.)
//     e. C's name is a structural folder (bin, binaries, win64, win32, x64, x86) → climb.
//     f. C's name is a generic sub-folder (game, retail, release, shipping, client, launcher, exe, executable)
//        → climb only when P shows it belongs to the same game (an .exe/.dll/.pak or a root marker in P).
//     g. P has a HARD root marker and does not look like a host of several programs (a launcher that keeps
//        games in sub-folders: another sibling folder with its own .exe) → climb.
//     h. Otherwise return C.
//  "Never" (not usable): drive roots, the Windows folder, Program Files, the user profile and well-known user
//  folders, library/collection folders (steamapps\common, SteamLibrary, Games, Jogos, Epic Games, Riot Games,
//  XboxGames, GOG Games...), the games folder, and a launcher's own folder (Steam, Epic, Riot Client, Hydra,
//  Battle.net, EA, Ubisoft, GOG Galaxy — by path or because it holds the launcher's client exe).
//  When the exe folder does not exist (stale shortcut, tests), only the name rules (c, e, f without evidence
//  → no climb) apply.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GamesHub
{
    /// <summary>Names directly inside a folder (no recursion). Case-insensitive.</summary>
    public sealed class DirListing
    {
        public static readonly DirListing Empty = new DirListing();
        public readonly HashSet<string> Files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> Dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>File-system (and registry) view used by <see cref="GameRootResolver"/>; replaceable in tests.</summary>
    public interface IGameRootProbe
    {
        bool DirectoryExists(string dir);
        /// <summary>Entries directly inside dir; <see cref="DirListing.Empty"/> when missing/unreadable.</summary>
        DirListing List(string dir);
        /// <summary>Lower-cased, normalized InstallLocation values of the Windows Uninstall entries.</summary>
        ICollection<string> RegisteredInstallLocations();
    }

    /// <summary>Real disk. Registry locations come from <see cref="UninstallRegistry"/> (cached there) unless a
    /// fixed list is given.</summary>
    public sealed class DiskGameRootProbe : IGameRootProbe
    {
        private const int MaxEntries = 5000;
        private readonly IEnumerable<string> _fixedLocations;
        private readonly object _gate = new object();
        private List<UninstallEntry> _regSource;
        private HashSet<string> _regKeys;

        public DiskGameRootProbe() { }

        /// <summary>Uses these locations instead of the registry (tests: pass an empty list).</summary>
        public DiskGameRootProbe(IEnumerable<string> registeredLocations) { _fixedLocations = registeredLocations ?? new string[0]; }

        public bool DirectoryExists(string dir)
        {
            try { return !string.IsNullOrEmpty(dir) && Directory.Exists(dir); }
            catch (Exception ex) when (ExpectedErrors.IsFileSystem(ex)) { return false; }
        }

        public DirListing List(string dir)
        {
            var l = new DirListing();
            try
            {
                int n = 0;
                foreach (FileSystemInfo e in new DirectoryInfo(dir).EnumerateFileSystemInfos())
                {
                    if ((e.Attributes & FileAttributes.Directory) != 0) l.Dirs.Add(e.Name); else l.Files.Add(e.Name);
                    if (++n >= MaxEntries) break;
                }
            }
            catch (Exception ex) when (ExpectedErrors.IsFileSystem(ex)) { return DirListing.Empty; }
            return l;
        }

        public ICollection<string> RegisteredInstallLocations()
        {
            if (_fixedLocations != null) return Keys(_fixedLocations);
            List<UninstallEntry> entries = UninstallRegistry.ReadAll();   // cached for 60 s by UninstallRegistry
            lock (_gate)
            {
                if (!ReferenceEquals(entries, _regSource))
                {
                    _regKeys = Keys(entries.Where(e => !e.SystemComponent).Select(e => e.InstallLocation));
                    _regSource = entries;
                }
                return _regKeys;
            }
        }

        private static HashSet<string> Keys(IEnumerable<string> locations)
        {
            return new HashSet<string>(locations.Select(InstallPaths.Key).Where(k => k.Length > 3), StringComparer.Ordinal);
        }
    }

    public static class GameRootResolver
    {
        public const int MaxLevels = 5;

        private static readonly IGameRootProbe Disk = new DiskGameRootProbe();

        private static readonly HashSet<string> StructuralNames = Set("bin", "binaries", "win64", "win32", "x64", "x86");
        private static readonly HashSet<string> GenericNames = Set("game", "retail", "release", "shipping", "client",
            "launcher", "exe", "executable");
        private static readonly HashSet<string> UnrealPlatforms = Set("win64", "win32", "wingdk", "windows", "windowsnoeditor");
        // Sibling folders that commonly carry their own .exe inside a single game's root.
        private static readonly HashSet<string> HelperDirs = Set("_commonredist", "commonredist", "redist", "_redist", "redistributables",
            "directx", "vcredist", "support", "_support", "easyanticheat", "battleye", "engine", "crashhandler", "crashreporter",
            "_installer", "installer", "uninstall", "tools", "launcher", "bin", "binaries", "__installer", "prereqs", "prerequisites");
        // Client executables that mark a launcher's own folder (never a game root).
        private static readonly HashSet<string> LauncherClients = Set("steam.exe", "epicgameslauncher.exe", "riotclientservices.exe",
            "hydra.exe", "battle.net.exe", "battle.net launcher.exe", "eadesktop.exe", "ealauncher.exe", "origin.exe",
            "ubisoftconnect.exe", "upc.exe", "uplay.exe", "galaxyclient.exe");

        private static HashSet<string> Set(params string[] items) => new HashSet<string>(items, StringComparer.OrdinalIgnoreCase);

        /// <summary>Best install root for a game executable (see file header). "" when there is none (loose exe in the
        /// games folder, exe in a drive root / system / library folder).</summary>
        public static string Resolve(string exePath, string declaredInstallDir = null, string gamesDir = null)
            => Resolve(exePath, declaredInstallDir, gamesDir, Disk);

        public static string Resolve(string exePath, string declaredInstallDir, string gamesDir, IGameRootProbe probe)
        {
            probe = probe ?? Disk;
            string exeDir = GameRules.NormalizeDir(SafeParent(exePath));
            string declared = GameRules.NormalizeDir(declaredInstallDir);
            if (declared.Length > 0 && !IsForbidden(declared, gamesDir)
                && (exeDir.Length == 0 || InstallPaths.IsSameOrInside(exeDir, declared)))
                return declared;
            if (exeDir.Length == 0 || IsForbidden(exeDir, gamesDir)) return "";

            var ctx = new Context(probe, gamesDir, probe.DirectoryExists(exeDir));
            if (ctx.IsLauncherFolder(exeDir)) return "";
            string cur = exeDir;
            bool fromBinaries = false;
            for (int level = 0; level <= MaxLevels; level++)
            {
                DirListing here = ctx.List(cur);
                if (HasHardMarker(ctx, cur, here)) return cur;
                string parent = GameRules.NormalizeDir(SafeParent(cur));
                if (level == MaxLevels || !ctx.Usable(parent)) return cur;
                string name = Path.GetFileName(cur);
                string parentName = Path.GetFileName(parent);

                // c. Unreal layout
                if (UnrealPlatforms.Contains(name) && parentName.Equals("Binaries", StringComparison.OrdinalIgnoreCase))
                { cur = parent; continue; }
                if (name.Equals("Binaries", StringComparison.OrdinalIgnoreCase)) { cur = parent; fromBinaries = true; continue; }
                if (fromBinaries || (here.Dirs.Contains("Binaries") && here.Dirs.Contains("Content")))
                {
                    fromBinaries = false;
                    if (!ctx.OnDisk || BelongsToProject(ctx, parent)) { cur = parent; continue; }
                    return cur;
                }
                // d. soft markers
                if (HasSoftMarker(ctx, cur, here)) return cur;
                // e/f. intermediate folder names
                if (StructuralNames.Contains(name)) { cur = parent; continue; }
                if (GenericNames.Contains(name))
                {
                    if (ctx.OnDisk && ParentShowsSameGame(ctx, parent)) { cur = parent; continue; }
                    return cur;
                }
                // g. the parent is the install root of this sub-folder
                if (ctx.OnDisk && HasHardMarker(ctx, parent, ctx.List(parent)) && !HostsOtherPrograms(ctx, parent, name))
                { cur = parent; continue; }
                return cur;
            }
            return cur;
        }

        private sealed class Context
        {
            private readonly IGameRootProbe _probe;
            private readonly Dictionary<string, DirListing> _lists = new Dictionary<string, DirListing>(StringComparer.OrdinalIgnoreCase);
            private ICollection<string> _registered;
            public readonly string GamesDir;
            public readonly bool OnDisk;

            public Context(IGameRootProbe probe, string gamesDir, bool onDisk) { _probe = probe; GamesDir = gamesDir; OnDisk = onDisk; }

            public DirListing List(string dir)
            {
                if (!OnDisk) return DirListing.Empty;
                if (!_lists.TryGetValue(dir, out DirListing l)) _lists[dir] = l = _probe.List(dir) ?? DirListing.Empty;
                return l;
            }

            public bool DirExists(string dir) => OnDisk && _probe.DirectoryExists(dir);

            public bool IsRegistered(string dir)
            {
                if (!OnDisk) return false;
                _registered = _registered ?? _probe.RegisteredInstallLocations() ?? new string[0];
                return _registered.Contains(InstallPaths.Key(dir));
            }

            public bool IsLauncherFolder(string dir) => List(dir).Files.Any(LauncherClients.Contains);

            public bool Usable(string dir) => dir.Length > 0 && !IsForbidden(dir, GamesDir) && !IsLauncherFolder(dir);
        }

        /// <summary>Drive roots, system/user folders, library containers, launcher folders and the games folder.</summary>
        private static bool IsForbidden(string dir, string gamesDir)
            => GameRules.IsGenericDir(dir, gamesDir) || InstallPaths.IsForbiddenRoot(dir);

        private static bool HasHardMarker(Context ctx, string dir, DirListing l)
        {
            foreach (string f in l.Files)
            {
                string lf = f.ToLowerInvariant();
                if (lf.EndsWith(".exe") && (lf.StartsWith("unins") || lf.StartsWith("uninstall"))) return true;
                if (lf.StartsWith("installscript") && lf.EndsWith(".vdf")) return true;
                if (lf.StartsWith("goggame-") && lf.EndsWith(".info")) return true;
                if (lf == "microsoftgame.config") return true;
                if (lf.EndsWith(".exe") && l.Dirs.Contains(f.Substring(0, f.Length - 4) + "_Data")) return true; // Unity
            }
            if (l.Dirs.Contains(".egstore")) return true;
            if (l.Dirs.Contains("Engine") && l.Dirs.Any(d => !d.Equals("Engine", StringComparison.OrdinalIgnoreCase)
                                                             && ctx.DirExists(Path.Combine(dir, d, "Binaries")))) return true;
            return ctx.IsRegistered(dir);
        }

        private static bool HasSoftMarker(Context ctx, string dir, DirListing l)
        {
            if (l.Files.Contains("steam_appid.txt")) return true;
            if (l.Files.Any(f => f.EndsWith(".pak", StringComparison.OrdinalIgnoreCase))) return true;
            if (l.Dirs.Contains("Content") && ctx.DirExists(Path.Combine(dir, "Content", "Paks"))) return true;
            bool versionFile = l.Files.Contains("version.txt") || l.Files.Any(f => f.EndsWith(".ver", StringComparison.OrdinalIgnoreCase));
            return versionFile && l.Files.Count + l.Dirs.Count >= 8;
        }

        /// <summary>The folder above an Unreal project is the game root when it holds Engine\, the game's .exe or a root marker.</summary>
        private static bool BelongsToProject(Context ctx, string parent)
        {
            DirListing l = ctx.List(parent);
            return l.Dirs.Contains("Engine") || l.Files.Any(IsExe) || HasHardMarker(ctx, parent, l) || l.Files.Contains("steam_appid.txt");
        }

        private static bool ParentShowsSameGame(Context ctx, string parent)
        {
            DirListing l = ctx.List(parent);
            return l.Files.Any(f => IsExe(f) || f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".pak", StringComparison.OrdinalIgnoreCase))
                || l.Dirs.Contains("Engine") || HasHardMarker(ctx, parent, l) || HasSoftMarker(ctx, parent, l);
        }

        /// <summary>True when another (non-helper) sub-folder of parent carries its own .exe: parent then hosts several
        /// programs (e.g. a launcher that keeps games in sub-folders) and must not swallow this one.</summary>
        private static bool HostsOtherPrograms(Context ctx, string parent, string self)
        {
            int checkedDirs = 0;
            foreach (string d in ctx.List(parent).Dirs.Where(x => !x.Equals(self, StringComparison.OrdinalIgnoreCase) && !HelperDirs.Contains(x) && !x.StartsWith(".")))
            {
                if (++checkedDirs > 32) return true;
                if (ctx.List(Path.Combine(parent, d)).Files.Any(IsExe)) return true;
            }
            return false;
        }

        private static bool IsExe(string f) => f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

        private static string SafeParent(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            try { return Path.GetDirectoryName(path.Trim().Trim('"').TrimEnd('\\', '/')) ?? ""; }
            catch (Exception ex) when (ExpectedErrors.IsFileSystem(ex)) { Log.Warn("GameRootResolver: bad path " + path, ex); return ""; }
        }
    }
}
