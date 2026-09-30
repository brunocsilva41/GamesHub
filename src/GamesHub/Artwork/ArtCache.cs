using System;
using System.IO;
using System.Linq;
using System.Text;

namespace GamesHub
{
    /// <summary>
    /// On-disk layout of the artwork cache (pure file-system logic, no network):
    ///   steam-&lt;appid&gt;/header.jpg|capsule.jpg|hero.jpg|logo.png   (shared by every game with that app id)
    ///   g-&lt;hash(game.Id)&gt;/icon.png, custom-&lt;kind&gt;.&lt;ext&gt;, sgdb-&lt;kind&gt;.&lt;ext&gt;   (per game)
    /// Resolution per kind: custom &gt; Steam &gt; SteamGridDB &gt; (icon only) extracted icon.
    /// </summary>
    public sealed class ArtCache
    {
        private static readonly string[] Exts = { ".jpg", ".png" };

        public string Root { get; }

        public ArtCache(string root) { Root = root; }

        // ------------------------------------------------------------ keys

        /// <summary>Stable, file-system safe key for a game id (FNV-1a 64 of the lowercased id).</summary>
        public static string GameKey(string gameId)
        {
            ulong h = 14695981039346656037UL;
            foreach (byte b in Encoding.UTF8.GetBytes((gameId ?? "").Trim().ToLowerInvariant()))
            {
                h ^= b;
                h *= 1099511628211UL;
            }
            return "g-" + h.ToString("x16");
        }

        public static string SteamKey(string appId) => "steam-" + appId;

        /// <summary>Base file name (no extension) of a kind inside a cache folder.</summary>
        public static string BaseName(string kind, string origin)
        {
            // kind becomes part of a file name: only the known kinds are accepted.
            if (!ArtKind.IsValid(kind)) throw new ArgumentException("Unknown artwork kind: " + kind, nameof(kind));
            return origin == "steam" ? kind : origin + "-" + kind;   // "header", "custom-header", "sgdb-header"
        }

        public static string ExtFor(string kind) => ArtKind.NeedsAlpha(kind) ? ".png" : ".jpg";

        // ------------------------------------------------------------ paths

        public string GameDir(string gameId) => Path.Combine(Root, GameKey(gameId));   // hashed: always a plain name

        /// <summary>Folder of a Steam app's art. Throws ArgumentException unless appId is a plain numeric app id,
        /// so a crafted id ("..\x") can never point outside the cache.</summary>
        public string SteamDir(string appId)
        {
            if (!ArtKind.IsAppId(appId)) throw new ArgumentException("Not a Steam app id: " + appId, nameof(appId));
            return Path.Combine(Root, SteamKey(appId));
        }

        /// <summary>Path without extension; callers add the extension of the actual image format.</summary>
        public string SteamBase(string appId, string kind) => Path.Combine(SteamDir(appId), BaseName(kind, "steam"));
        public string CustomBase(string gameId, string kind) => Path.Combine(GameDir(gameId), BaseName(kind, "custom"));
        public string SgdbBase(string gameId, string kind) => Path.Combine(GameDir(gameId), BaseName(kind, "sgdb"));
        public string IconFile(string gameId) => Path.Combine(GameDir(gameId), "icon.png");

        /// <summary>Returns the existing file for a base path (any known extension) or null.</summary>
        public static string FindExisting(string basePath)
        {
            return Exts.Select(ext => basePath + ext).FirstOrDefault(File.Exists);
        }

        /// <summary>Deletes every extension variant of a base path. Returns true if something was deleted.</summary>
        public static bool DeleteAll(string basePath)
        {
            bool any = false;
            foreach (string p in Exts.Select(ext => basePath + ext).Where(File.Exists))
            {
                try { File.Delete(p); any = true; }
                catch (Exception ex) when (ExpectedErrors.IsFileSystem(ex)) { Log.Warn("Art: cannot delete " + p, ex); }
            }
            return any;
        }

        // ------------------------------------------------------------ resolution

        public string FindCustom(string gameId, string kind) => FindExisting(CustomBase(gameId, kind));

        public string FindSteam(string appId, string kind)
            => ArtKind.IsAppId(appId) && kind != ArtKind.Icon ? FindExisting(SteamBase(appId, kind)) : null;

        public string FindSgdb(string gameId, string kind)
            => kind != ArtKind.Icon ? FindExisting(SgdbBase(gameId, kind)) : null;

        public string FindIcon(string gameId)
        {
            string p = IconFile(gameId);
            return File.Exists(p) ? p : null;
        }

        /// <summary>Absolute path of the file that wins for this kind, or null.</summary>
        public string FindBest(string gameId, string appId, string kind)
        {
            return FindCustom(gameId, kind)
                ?? FindSteam(appId, kind)
                ?? FindSgdb(gameId, kind)
                ?? (kind == ArtKind.Icon ? FindIcon(gameId) : null);
        }

        /// <summary>Everything currently cached for a game, as paths relative to Root with forward slashes.</summary>
        public Artwork Resolve(string gameId, string appId)
        {
            var art = new Artwork();
            foreach (string kind in ArtKind.All)
                ArtKind.Set(art, kind, Relative(FindBest(gameId, appId, kind)));
            return art;
        }

        public string Relative(string absolute)
        {
            if (string.IsNullOrEmpty(absolute)) return null;
            string root = Root.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            string rel = absolute.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? absolute.Substring(root.Length) : absolute;
            return rel.Replace('\\', '/');
        }
    }
}
