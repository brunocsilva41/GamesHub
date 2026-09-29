// including wildcard / {{p|uid}} resolution by directory enumeration.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace GamesHub
{
    /// <summary>Maps a PCGW path key ("appdata", "userprofile\documents", "game", "steam", ...) to a folder.
    /// Returns null when unknown/unavailable.</summary>
    public interface IPcgwFolders
    {
        string Get(string key);
    }

    public static class PcgwPaths
    {
        /// <summary>Marker that {{p|uid}} expands to before wildcard resolution.</summary>
        public const string UidWildcard = "*";

        private static readonly Regex PTemplate = new Regex(@"\{\{\s*p(?:ath)?\s*\|\s*([^{}|]*?)\s*\}\}", RegexOptions.IgnoreCase);
        private static readonly string[] RegistryKeys = { "hkcu", "hklm", "wow64" };

        // "{{p|userprofile}}\Documents" is written both ways on the wiki; map to the real Known Folder.
        private static readonly (Regex re, string key)[] ProfileAliases =
        {
            (new Regex(@"\{\{\s*p\s*\|\s*userprofile\s*\}\}[\\/]+Documents(?=[\\/]|$)", RegexOptions.IgnoreCase), "userprofile\\documents"),
            (new Regex(@"\{\{\s*p\s*\|\s*userprofile\s*\}\}[\\/]+AppData[\\/]+LocalLow(?=[\\/]|$)", RegexOptions.IgnoreCase), "userprofile\\appdata\\locallow"),
            (new Regex(@"\{\{\s*p\s*\|\s*userprofile\s*\}\}[\\/]+AppData[\\/]+Roaming(?=[\\/]|$)", RegexOptions.IgnoreCase), "appdata"),
            (new Regex(@"\{\{\s*p\s*\|\s*userprofile\s*\}\}[\\/]+AppData[\\/]+Local(?=[\\/]|$)", RegexOptions.IgnoreCase), "localappdata"),
        };

        public static string NormalizeKey(string key) =>
            Regex.Replace((key ?? "").Trim().ToLowerInvariant().Replace('/', '\\'), @"\\+", "\\");

        public static bool IsRegistry(string raw)
        {
            foreach (Match m in PTemplate.Matches(raw ?? ""))
                if (RegistryKeys.Contains(NormalizeKey(m.Groups[1].Value))) return true;
            return false;
        }

        /// <summary>Expands the templates. Returns a Windows path that may still contain wildcards ("*"),
        /// or null when it cannot be expanded (registry key, unknown template, missing folder).</summary>
        public static string Expand(string raw, IPcgwFolders folders)
        {
            if (string.IsNullOrWhiteSpace(raw) || IsRegistry(raw)) return null;
            string s = raw.Trim();
            foreach (var (re, key) in ProfileAliases) s = re.Replace(s, "{{p|" + key + "}}");
            bool failed = false;
            s = PTemplate.Replace(s, m =>
            {
                string key = NormalizeKey(m.Groups[1].Value);
                if (key == "uid") return UidWildcard;
                string v = folders?.Get(key);
                if (string.IsNullOrEmpty(v)) { failed = true; return ""; }
                return v.TrimEnd('\\', '/');
            });
            if (failed || s.Contains("{{") || s.Contains("}}") || s.Contains("[[")) return null;
            return NormalizePath(s);
        }

        /// <summary>Forward → back slashes, collapses repeated separators (keeps a UNC prefix), trims trailing '\'.</summary>
        public static string NormalizePath(string p)
        {
            if (string.IsNullOrWhiteSpace(p)) return "";
            string s = p.Trim().Replace('/', '\\');
            bool unc = s.StartsWith("\\\\");
            s = Regex.Replace(s, @"\\{2,}", "\\");
            if (unc) s = "\\" + s;
            s = s.TrimEnd('\\', ' ');
            if (s.Length == 2 && s[1] == ':') s += "\\";
            if (!unc && !Regex.IsMatch(s, @"^[A-Za-z]:\\")) return ""; // must be absolute
            return s;
        }

        public static bool HasWildcard(string s) => s != null && (s.IndexOf('*') >= 0 || s.IndexOf('?') >= 0);

        /// <summary>Resolves wildcards by enumerating the file system. Exactly one match → that path; several →
        /// the most recently written. A wildcard in the LAST segment is treated as a file pattern: the result is
        /// the containing folder. No match → ("", false).</summary>
        public static (string path, bool exists) Resolve(string pattern)
        {
            if (string.IsNullOrEmpty(pattern)) return ("", false);
            if (!HasWildcard(pattern)) return (pattern, Exists(pattern));

            string[] segs = pattern.Split('\\');
            bool fileGlob = HasWildcard(segs[segs.Length - 1]);
            int dirCount = fileGlob ? segs.Length - 1 : segs.Length;

            // Walk the segments, keeping every existing match (bounded).
            var current = new List<string> { segs[0] + "\\" };
            for (int i = 1; i < dirCount; i++)
            {
                string seg = segs[i];
                bool last = i == dirCount - 1;
                var next = new List<string>();
                foreach (string dir in current)
                {
                    if (!HasWildcard(seg)) { next.Add(Path.Combine(dir, seg)); continue; }
                    if (!Directory.Exists(dir)) continue;
                    try
                    {
                        IEnumerable<string> found = last && !fileGlob
                            ? Directory.EnumerateFileSystemEntries(dir, seg)
                            : Directory.EnumerateDirectories(dir, seg);
                        next.AddRange(found.Take(64));
                    }
                    catch (Exception ex) { Log.Warn("PCGW: cannot enumerate " + dir, ex); }
                }
                current = next;
                if (current.Count == 0) return ("", false);
            }

            if (fileGlob)
            {
                string glob = segs[segs.Length - 1];
                List<string> dirs = current.Where(Directory.Exists).ToList();
                if (dirs.Count == 0) return ("", false);
                List<string> withFiles = dirs.Where(d => AnyEntry(d, glob)).ToList();
                if (withFiles.Count > 0) return (MostRecent(withFiles), true);
                return (MostRecent(dirs), false);
            }

            List<string> existing = current.Where(Exists).ToList();
            if (existing.Count == 0) return ("", false);
            return (MostRecent(existing), true);
        }

        private static bool AnyEntry(string dir, string glob)
        {
            try { return Directory.EnumerateFileSystemEntries(dir, glob).Any(); }
            catch (Exception ex) { Log.Warn("PCGW: cannot enumerate " + dir, ex); return false; }
        }

        private static bool Exists(string p)
        {
            try { return Directory.Exists(p) || File.Exists(p); }
            catch (Exception ex) { Log.Warn("PCGW: exists check failed " + p, ex); return false; }
        }

        private static string MostRecent(List<string> paths)
        {
            if (paths.Count == 1) return paths[0];
            return paths.OrderByDescending(p =>
            {
                try { return Directory.Exists(p) ? Directory.GetLastWriteTimeUtc(p) : File.GetLastWriteTimeUtc(p); }
                catch (Exception ex) { Log.Warn("PCGW: mtime failed " + p, ex); return DateTime.MinValue; }
            }).ThenBy(p => p, StringComparer.OrdinalIgnoreCase).First();
        }

        /// <summary>Row → ResolvedPath (expanded + wildcard-resolved + existence).</summary>
        public static ResolvedPath ToResolved(PcgwRow row, IPcgwFolders folders)
        {
            var rp = new ResolvedPath { Raw = row.Raw, Kind = row.Kind };
            string expanded = Expand(row.Raw, folders);
            if (string.IsNullOrEmpty(expanded)) return rp;
            var (path, exists) = Resolve(expanded);
            rp.Path = path;
            rp.Exists = exists;
            return rp;
        }
    }
}
