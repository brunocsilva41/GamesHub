// Steam app names. Never deletes user files.
using System;
using System.IO;
using System.Net;
using System.Text;

namespace GamesHub
{
    internal static class ShortcutFactory
    {
        /// <summary>Creates a shortcut in gamesDir for an .exe/.lnk/.url. Returns the created file path.</summary>
        public static string CreateFromFile(string gamesDir, string sourcePath, string displayName)
        {
            string ext = Path.GetExtension(sourcePath).ToLowerInvariant();
            string baseName = GameRules.SanitizeFileName(displayName);
            Directory.CreateDirectory(gamesDir);
            if (ext == ".url")
            {
                string dest = GameRules.UniquePath(gamesDir, baseName, ".url");
                File.Copy(sourcePath, dest, false);
                return dest;
            }
            string lnk = GameRules.UniquePath(gamesDir, baseName, ".lnk");
            ShortcutInfo info;
            if (ext == ".lnk")
            {
                info = LibShellLink.ReadLnk(sourcePath);
                if (info.Target.Length == 0)
                {
                    // Shell-namespace shortcut (e.g. Store app): no file target to rebuild, copy it as is.
                    File.Copy(sourcePath, lnk, false);
                    return lnk;
                }
            }
            else
            {
                info = new ShortcutInfo { Target = sourcePath, WorkingDir = Path.GetDirectoryName(sourcePath), IconPath = sourcePath };
            }
            LibShellLink.WriteLnk(lnk, info);
            return lnk;
        }

        public static string CreateSteamUrl(string gamesDir, string appId, string displayName)
        {
            Directory.CreateDirectory(gamesDir);
            string dest = GameRules.UniquePath(gamesDir, GameRules.SanitizeFileName(displayName), ".url");
            File.WriteAllText(dest, LibShellLink.BuildUrlFile("steam://rungameid/" + appId), Encoding.ASCII);
            return dest;
        }

        /// <summary>Moves a file into trashDir under a unique timestamped name. Returns the new path.</summary>
        public static string MoveToTrash(string file, string trashDir, DateTime now)
        {
            Directory.CreateDirectory(trashDir);
            string dest = GameRules.TrashPath(trashDir, Path.GetFileName(file), now);
            File.Move(file, dest);
            return dest;
        }

        /// <summary>Moves a trashed file back. Returns false (and moves nothing) if the original name is taken.</summary>
        public static bool RestoreFromTrash(string trashPath, string originalPath)
        {
            if (File.Exists(originalPath) || Directory.Exists(originalPath)) return false;
            Directory.CreateDirectory(Path.GetDirectoryName(originalPath));
            File.Move(trashPath, originalPath);
            return true;
        }

        /// <summary>Game name from the Steam store API (pt-BR), "" on any failure. Blocks up to ~8 s.</summary>
        public static string FetchSteamName(string appId)
        {
            string url = "https://store.steampowered.com/api/appdetails?appids=" + appId + "&l=portuguese&cc=br";
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Timeout = 8000;
                req.ReadWriteTimeout = 8000;
                req.UserAgent = AppInfo.Name + "/" + AppInfo.Version;
                req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    return ParseAppDetailsName(reader.ReadToEnd(), appId);
            }
            catch (Exception ex)
            {
                Log.Warn("Steam appdetails failed for " + appId, ex);
                return "";
            }
        }

        /// <summary>Pure: name from { "&lt;id&gt;": { "success": true, "data": { "name": ... } } }.</summary>
        public static string ParseAppDetailsName(string json, string appId)
        {
            var root = Json.DeserializeObject(json) as System.Collections.Generic.IDictionary<string, object>;
            var entry = Json.Obj(root, appId);
            if (!Json.Bool(entry, "success")) return "";
            return Json.Str(Json.Obj(entry, "data"), "name").Trim();
        }
    }
}
