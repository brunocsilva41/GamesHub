using System;
using System.IO;
using System.Runtime.InteropServices;

namespace GamesHub
{
    public static class InstallHealthCheck
    {
        public static InstallHealth Check(Game game)
        {
            if (game == null) return Ok();
            try
            {
                if (!string.IsNullOrEmpty(game.FilePath)) return CheckFolderGame(game);
                if (!string.IsNullOrEmpty(game.InstallDir)) return CheckPath(game.InstallDir, true, "A pasta de instalação não existe mais");
                // Imported game launched straight from an exe (no InstallDir): check that exe.
                string t = (game.LaunchTarget ?? "").Trim().Trim('"');
                if (IsLocalAbsolutePath(t)) return CheckPath(t, false, "O executável do jogo não existe mais");
                return Ok(); // URI launch targets (steam://, com.epicgames..., battlenet://) are never broken here
            }
            catch (Exception ex)
            {
                Log.Warn("InstallHealthCheck failed for " + game.Id, ex);
                return Ok();
            }
        }

        private static InstallHealth CheckFolderGame(Game game)
        {
            string ext = (string.IsNullOrEmpty(game.Ext) ? Path.GetExtension(game.FilePath) : game.Ext).ToLowerInvariant();
            if (!File.Exists(game.FilePath))
            {
                InstallHealth drive = CheckDrive(game.FilePath);
                if (drive != null) return drive;
                return Broken(ext == ".exe" ? "O executável não existe mais na pasta de jogos." : "O atalho não existe mais na pasta de jogos.");
            }
            switch (ext)
            {
                case ".lnk":
                    string target = InstallShellLink.ReadLnkTarget(game.FilePath);
                    if (!IsLocalAbsolutePath(target)) return Ok(); // shell item / URL / unreadable: can't judge
                    return CheckPath(target, null, "O arquivo de destino do atalho não existe mais");
                case ".url":
                    string file = InstallShellLink.FileUrlToPath(InstallShellLink.ReadUrl(game.FilePath));
                    if (!IsLocalAbsolutePath(file)) return Ok();
                    return CheckPath(file, null, "O arquivo apontado pelo atalho não existe mais");
                default:
                    return Ok(); // .exe present
            }
        }

        /// <summary>isDir: true = must be a directory, false = file, null = either.</summary>
        private static InstallHealth CheckPath(string path, bool? isDir, string missingText)
        {
            if (!IsLocalAbsolutePath(path)) return Ok();
            InstallHealth drive = CheckDrive(path);
            if (drive != null) return drive;
            if (IsNetworkDrive(path)) return Ok(); // no network probing
            bool exists = isDir == true ? Directory.Exists(path)
                        : isDir == false ? File.Exists(path)
                        : File.Exists(path) || Directory.Exists(path);
            return exists ? Ok() : Broken(missingText + " (" + path + ").");
        }

        /// <summary>Returns a Broken result when the path's drive letter is not mounted / not ready, else null.</summary>
        public static InstallHealth CheckDrive(string path)
        {
            char letter = DriveLetter(path);
            if (letter == '\0') return null;
            string root = letter + @":\";
            uint mask = GetLogicalDrives();
            if ((mask & (1u << (letter - 'A'))) == 0) return Broken("O disco " + root + " não está conectado.");
            uint type = GetDriveType(root);
            if (type == DRIVE_REMOTE) return null;
            if (type == DRIVE_NO_ROOT_DIR) return Broken("O disco " + root + " não está conectado.");
            if (!Directory.Exists(root)) return Broken("O disco " + root + " não está disponível (sem mídia ou desconectado).");
            return null;
        }

        public static bool IsLocalAbsolutePath(string p)
            => !string.IsNullOrEmpty(p) && p.Length >= 3 && char.IsLetter(p[0]) && p[1] == ':' && (p[2] == '\\' || p[2] == '/');

        private static bool IsNetworkDrive(string path)
        {
            char l = DriveLetter(path);
            return l != '\0' && GetDriveType(l + @":\") == DRIVE_REMOTE;
        }

        private static char DriveLetter(string path)
            => IsLocalAbsolutePath(path) ? char.ToUpperInvariant(path[0]) : '\0';

        private static InstallHealth Ok() => new InstallHealth();
        private static InstallHealth Broken(string reason) => new InstallHealth { Broken = true, Reason = reason };

        private const uint DRIVE_NO_ROOT_DIR = 1, DRIVE_REMOTE = 4;
        [DllImport("kernel32.dll")] private static extern uint GetLogicalDrives();
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern uint GetDriveType(string root);
    }
}
