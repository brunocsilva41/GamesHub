using System;
using System.IO;

namespace GamesHub
{
    /// <summary>Path.Combine for segments that come from outside (game ids, manifests, wiki data, cache keys):
    /// the result must stay inside the base folder.</summary>
    internal static class SafePath
    {
        /// <summary>Combines <paramref name="baseDir"/> with <paramref name="parts"/>; returns null when a part is
        /// null, rooted, climbs out with "..", or the path is invalid. Otherwise returns exactly what
        /// Path.Combine would.</summary>
        public static string Combine(string baseDir, params string[] parts)
        {
            if (string.IsNullOrEmpty(baseDir) || parts == null) return null;
            foreach (string p in parts)
                if (p == null) return null;
            try
            {
                var all = new string[parts.Length + 1];
                all[0] = baseDir;
                Array.Copy(parts, 0, all, 1, parts.Length);
                string combined = Path.Combine(all);
                return IsInside(baseDir, combined) ? combined : null;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException
                                       || ex is PathTooLongException || ex is System.Security.SecurityException)
            {
                return null;
            }
        }

        /// <summary>True when <paramref name="path"/> resolves to <paramref name="baseDir"/> or somewhere below it.
        /// False for invalid paths.</summary>
        public static bool IsInside(string baseDir, string path)
        {
            if (string.IsNullOrEmpty(baseDir) || string.IsNullOrEmpty(path)) return false;
            try
            {
                string root = Path.GetFullPath(baseDir).TrimEnd('\\', '/');
                string full = Path.GetFullPath(path).TrimEnd('\\', '/');
                return full.Equals(root, StringComparison.OrdinalIgnoreCase)
                       || full.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException
                                       || ex is PathTooLongException || ex is System.Security.SecurityException)
            {
                return false;
            }
        }
    }
}
