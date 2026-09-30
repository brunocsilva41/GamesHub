using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace GamesHub
{
    public sealed class InstallInspector : IInstallInspector
    {
        public const string EpicLibraryUri = "com.epicgames.launcher://store/library";
        private readonly InstallSizeService _sizes;

        public InstallInspector() : this(Path.Combine(AppPaths.CacheDir, "install", "sizes.json")) { }

        /// <summary>For tests: custom sizes cache file.</summary>
        public InstallInspector(string sizesCacheFile)
        {
            _sizes = new InstallSizeService(sizesCacheFile);
        }

        // ------------------------------------------------------------ health

        public InstallHealth CheckHealth(Game game) => InstallHealthCheck.Check(game);

        // ------------------------------------------------------------ sizes

        public Task<long> GetSizeBytesAsync(Game game)
        {
            string dir = InstallSizeService.DirOf(game);
            return dir.Length == 0 ? Task.FromResult(-1L) : _sizes.GetAsync(dir);
        }

        public long GetCachedSizeBytes(Game game)
        {
            string dir = InstallSizeService.DirOf(game);
            return dir.Length == 0 ? -1 : _sizes.GetCached(dir);
        }

        // ------------------------------------------------------------ drives

        public List<DriveSpace> GetDrives()
        {
            var list = new List<DriveSpace>();
            DriveInfo[] drives;
            try { drives = DriveInfo.GetDrives(); }
            catch (Exception ex) when (ExpectedErrors.IsFileSystem(ex)) { Log.Warn("InstallInspector: GetDrives failed", ex); return list; }
            foreach (DriveInfo d in drives)
            {
                try
                {
                    if (d.DriveType != DriveType.Fixed && d.DriveType != DriveType.Removable) continue;
                    if (!d.IsReady) continue;
                    list.Add(new DriveSpace { Name = d.Name, Label = d.VolumeLabel ?? "", TotalBytes = d.TotalSize, FreeBytes = d.AvailableFreeSpace });
                }
                catch (Exception ex) when (ExpectedErrors.IsFileSystem(ex)) { Log.Warn("InstallInspector: drive " + d.Name + " unreadable", ex); }
            }
            return list;
        }

        // ------------------------------------------------------------ uninstall

        public UninstallInfo FindUninstaller(Game game)
        {
            if (game == null) return new UninstallInfo();
            try
            {
                string appId = SteamAppIdOf(game);
                if (appId.Length > 0)
                    return new UninstallInfo { Method = "steam", Command = "steam://uninstall/" + appId, DisplayName = game.Name };
                if (IsEpic(game))
                    return new UninstallInfo { Method = "epic", Command = EpicLibraryUri, DisplayName = game.Name };

                UninstallEntry e = UninstallMatcher.Best(UninstallRegistry.ReadAll(), GameDirForMatching(game), game.Name);
                if (e != null)
                    return new UninstallInfo { Method = "registry", Command = e.UninstallString, DisplayName = e.DisplayName };
            }
            catch (Exception ex) when (ExpectedErrors.IsFileSystem(ex) || ExpectedErrors.IsRegistry(ex))
            {
                Log.Warn("InstallInspector: FindUninstaller failed for " + game.Id, ex);
            }
            return new UninstallInfo();
        }

        public static string SteamAppIdOf(Game g)
        {
            string id = (g.SteamAppId ?? "").Trim();
            if (g.Source == "steam")
            {
                string gameId = g.Id ?? "";
                if (id.Length == 0 && gameId.StartsWith("steam:")) id = gameId.Substring(6);
                return IsDigits(id) ? id : "";
            }
            return string.Equals(g.Platform, "Steam", StringComparison.OrdinalIgnoreCase) && IsDigits(id) ? id : "";
        }

        public static bool IsEpic(Game g)
            => g.Source == "epic" || (string.Equals(g.Platform, "Epic", StringComparison.OrdinalIgnoreCase)
                && (g.LaunchTarget ?? "").StartsWith("com.epicgames.launcher:", StringComparison.OrdinalIgnoreCase));

        private static bool IsDigits(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (char c in s) if (c < '0' || c > '9') return false;
            return true;
        }

        private static readonly HashSet<string> LauncherExes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "RiotClientServices.exe", "Hydra.exe", "steam.exe", "EpicGamesLauncher.exe", "Battle.net.exe", "Battle.net Launcher.exe",
            "EADesktop.exe", "Origin.exe", "UbisoftConnect.exe", "upc.exe", "GalaxyClient.exe", "javaw.exe", "java.exe",
            "cmd.exe", "powershell.exe", "explorer.exe", "rundll32.exe",
        };

        /// <summary>InstallDir / Exe folder, else the folder of the shortcut's target.</summary>
        public static string GameDirForMatching(Game g)
        {
            if (!string.IsNullOrWhiteSpace(g.InstallDir)) return InstallPaths.Normalize(g.InstallDir);
            string target = g.Exe ?? "";
            if (target.Length == 0 && !string.IsNullOrEmpty(g.FilePath) && File.Exists(g.FilePath))
            {
                string ext = (g.Ext ?? "").ToLowerInvariant();
                // A loose .exe in the games folder says nothing about its install folder → name-only.
                target = ext == ".lnk" ? InstallShellLink.ReadLnkTarget(g.FilePath)
                       : ext == ".url" ? InstallShellLink.FileUrlToPath(InstallShellLink.ReadUrl(g.FilePath))
                       : "";
            }
            target = InstallPaths.Normalize(target);
            if (!InstallHealthCheck.IsLocalAbsolutePath(target)) return "";
            // Shortcut launches a launcher (Riot Client, Hydra, Steam...): its folder is not the game's → name-only matching,
            // otherwise "2XKO.lnk" would offer to uninstall the whole Riot Client.
            if (LauncherExes.Contains(Path.GetFileName(target)) && UninstallMatcher.NormalizeName(g.Name) != UninstallMatcher.NormalizeName(Path.GetFileNameWithoutExtension(target)))
                return "";
            if (Directory.Exists(target)) return InstallPaths.Normalize(target);
            return InstallPaths.Normalize(Path.GetDirectoryName(target));
        }

        public OpResult RunUninstaller(UninstallInfo info)
        {
            if (info == null || info.Method == "none" || string.IsNullOrWhiteSpace(info.Command))
                return OpResult.Fail("Nenhum desinstalador encontrado para este jogo.");
            string name = string.IsNullOrEmpty(info.DisplayName) ? "o jogo" : info.DisplayName;
            try
            {
                switch (info.Method)
                {
                    case "steam":
                        if (!info.Command.StartsWith("steam://uninstall/", StringComparison.OrdinalIgnoreCase))
                            return OpResult.Fail("Comando de desinstalação da Steam inválido.");
                        ShellOpen(info.Command);
                        return OpResult.Success("A Steam vai pedir a confirmação para desinstalar " + name + ".");
                    case "epic":
                        if (!info.Command.StartsWith("com.epicgames.launcher:", StringComparison.OrdinalIgnoreCase))
                            return OpResult.Fail("Comando da Epic inválido.");
                        ShellOpen(info.Command);
                        return OpResult.Success("Abrimos a biblioteca da Epic: desinstale " + name + " por lá (menu \"…\" do jogo → Desinstalar).");
                    case "registry":
                        return RunRegistryUninstaller(info.Command, name);
                    default:
                        return OpResult.Fail("Método de desinstalação desconhecido.");
                }
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                return OpResult.Fail("A desinstalação foi cancelada.");
            }
            catch (Exception ex) when (ExpectedErrors.IsProcess(ex) || ExpectedErrors.IsFileSystem(ex))
            {
                Log.Warn("InstallInspector: RunUninstaller failed (" + info.Method + ": " + info.Command + ")", ex);
                return OpResult.Fail("Não foi possível iniciar o desinstalador: " + ex.Message);
            }
        }

        private static OpResult RunRegistryUninstaller(string command, string name)
        {
            ParsedCommand p = UninstallCommand.Parse(command);
            if (p.File.Length == 0) return OpResult.Fail("O comando de desinstalação está vazio.");
            bool rooted = InstallHealthCheck.IsLocalAbsolutePath(p.File);
            if (rooted && !File.Exists(p.File))
                return OpResult.Fail("O desinstalador não foi encontrado (" + p.File + "). O jogo pode já ter sido removido.");
            var psi = new ProcessStartInfo(p.File, p.Args) { UseShellExecute = true };
            if (rooted) psi.WorkingDirectory = Path.GetDirectoryName(p.File);
            Log.Info("InstallInspector: starting uninstaller: " + p.File + " " + p.Args);
            using (Process.Start(psi)) { }
            return OpResult.Success("Desinstalador de " + name + " aberto. Siga as instruções na janela dele.");
        }

        private static void ShellOpen(string uri)
        {
            Log.Info("InstallInspector: opening " + uri);
            using (Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true })) { }
        }
    }
}
