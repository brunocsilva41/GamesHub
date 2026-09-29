// OWNER: LIB agent. Pure functions: combine sources, drop duplicates, apply user overrides.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace GamesHub
{
    internal static class LibraryMerge
    {
        private static readonly FieldInfo[] GameFields = typeof(Game).GetFields(BindingFlags.Public | BindingFlags.Instance);
        private static readonly MethodInfo MemberwiseCloneMethod =
            typeof(object).GetMethod("MemberwiseClone", BindingFlags.NonPublic | BindingFlags.Instance);

        /// <summary>Deep copy of every public field of Game (future fields included): lists are copied,
        /// nested objects (Artwork, GameVariant...) are member-wise cloned. null Art becomes an empty Artwork.</summary>
        public static Game Clone(Game g)
        {
            var c = new Game();
            foreach (FieldInfo f in GameFields) f.SetValue(c, DeepCopy(f.GetValue(g)));
            if (c.Art == null) c.Art = new Artwork();
            if (c.Collections == null) c.Collections = new List<string>();
            return c;
        }

        private static object DeepCopy(object v)
        {
            if (v == null || v is string || v.GetType().IsValueType) return v;
            if (v is IList list && v.GetType().IsGenericType)
            {
                var copy = (IList)Activator.CreateInstance(v.GetType());
                foreach (object item in list) copy.Add(DeepCopy(item));
                return copy;
            }
            return MemberwiseCloneMethod.Invoke(v, null);
        }

        public static Artwork CloneArt(Artwork a) => a == null ? new Artwork() : (Artwork)DeepCopy(a);

        /// <summary>Folder entries win over imported ones (Steam, Epic, extra sources) for the same game (same Steam app id, Epic app,
        /// install dir or exe). The folder entry inherits InstallDir/Exe from the dropped duplicate.
        /// Inputs are not modified; the result contains copies.</summary>
        public static List<Game> Dedupe(IEnumerable<Game> folderGames, IEnumerable<Game> importedGames)
        {
            List<Game> folder = folderGames.Select(Clone).ToList();
            List<Game> imported = importedGames.Select(Clone).ToList();
            var dropped = new HashSet<Game>();
            foreach (Game f in folder)
            {
                string epicName = GameRules.ExtractEpicAppName(f.LaunchTarget);
                foreach (Game i in imported)
                {
                    if (dropped.Contains(i) || !IsSameGame(f, i, epicName)) continue;
                    dropped.Add(i);
                    if (f.InstallDir.Length == 0) f.InstallDir = i.InstallDir;
                    if (f.Exe.Length == 0) f.Exe = i.Exe;
                    if (f.SteamAppId.Length == 0) f.SteamAppId = i.SteamAppId;
                }
            }
            var result = new List<Game>(folder);
            var ids = new HashSet<string>(folder.Select(g => g.Id), StringComparer.OrdinalIgnoreCase);
            foreach (Game i in imported)
                if (!dropped.Contains(i) && ids.Add(i.Id)) result.Add(i);
            return result;
        }

        private static bool IsSameGame(Game f, Game i, string folderEpicName)
        {
            if (f.SteamAppId.Length > 0 && f.SteamAppId == i.SteamAppId) return true;
            if (i.Source == GameRules.SourceEpic && folderEpicName.Length > 0 && i.Id.Equals(GameRules.EpicId(folderEpicName), StringComparison.OrdinalIgnoreCase)) return true;
            if (f.Exe.Length > 0 && i.Exe.Length > 0 && PathEq(f.Exe, i.Exe)) return true;
            if (f.InstallDir.Length > 0 && i.InstallDir.Length > 0 && PathEq(f.InstallDir, i.InstallDir)) return true;
            if (f.Exe.Length > 0 && i.InstallDir.Length > 0 && IsUnder(f.Exe, i.InstallDir)) return true;
            return false;
        }

        public static bool PathEq(string a, string b) =>
            string.Equals(a.TrimEnd('\\', '/'), b.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

        public static bool IsUnder(string path, string dir) =>
            path.StartsWith(dir.TrimEnd('\\', '/') + "\\", StringComparison.OrdinalIgnoreCase);

        /// <summary>Source game + user metadata → library game (Art/Running are set by the caller).</summary>
        public static Game ApplyMeta(Game source, GameMeta meta, DateTime fallbackAddedAt)
        {
            Game g = Clone(source);
            meta = meta ?? new GameMeta();
            if (!string.IsNullOrWhiteSpace(meta.NameOverride)) g.Name = meta.NameOverride.Trim();
            g.LaunchArgs = meta.LaunchArgs ?? "";
            if (!string.IsNullOrEmpty(meta.SteamAppIdOverride)) g.SteamAppId = meta.SteamAppIdOverride;
            g.Favorite = meta.Favorite;
            g.Hidden = meta.Hidden;
            g.Collections = new List<string>(meta.Collections ?? new List<string>());
            g.LastPlayed = meta.LastPlayed;
            g.PlaySeconds = meta.PlaySeconds;
            g.AddedAt = meta.AddedAt ?? fallbackAddedAt;
            return g;
        }

        /// <summary>Trimmed, non-empty, case-insensitively distinct collection names (first spelling wins).</summary>
        public static List<string> NormalizeCollections(IEnumerable<string> cols) =>
            (cols ?? Enumerable.Empty<string>()).Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim())
                .Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();

        /// <summary>Fields that influence artwork; Resolve is re-run only when this changes.</summary>
        public static string ArtSignature(Game g) => string.Join("|", g.Name, g.SteamAppId, g.Exe, g.FilePath, g.LaunchTarget, g.Platform);
    }
}
