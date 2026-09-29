// OWNER: DIST agent. GitHub "releases/latest" JSON parsing, asset selection and .sha256 parsing (pure, testable).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace GamesHub
{
    public sealed class ReleaseAsset
    {
        public string Name = "";
        public string DownloadUrl = "";
        public long Size;
    }

    public sealed class GitHubRelease
    {
        public string TagName = "";
        public string Title = "";
        public string Body = "";
        public string HtmlUrl = "";
        public bool Draft, PreRelease;
        public List<ReleaseAsset> Assets = new List<ReleaseAsset>();

        /// <summary>Version from the tag (or title); null when neither is a valid version.</summary>
        public SemanticVersion Version =>
            SemanticVersion.TryParse(TagName, out SemanticVersion v) || SemanticVersion.TryParse(Title, out v) ? v : null;

        private static readonly Regex InstallerName = new Regex(@"^GamesHub-Setup-.*\.exe$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>Parses the object returned by GET /repos/{owner}/{repo}/releases/latest. Throws FormatException on garbage.</summary>
        public static GitHubRelease Parse(string json)
        {
            if (!(Json.DeserializeObject(json ?? "") is IDictionary<string, object> d))
                throw new FormatException("Release JSON is not an object");
            var r = new GitHubRelease
            {
                TagName = Json.Str(d, "tag_name"),
                Title = Json.Str(d, "name"),
                Body = Json.Str(d, "body"),
                HtmlUrl = Json.Str(d, "html_url"),
                Draft = Json.Bool(d, "draft"),
                PreRelease = Json.Bool(d, "prerelease"),
            };
            if (d.TryGetValue("assets", out object a) && a is IEnumerable<object> list)
                foreach (object o in list)
                    if (o is IDictionary<string, object> ad)
                        r.Assets.Add(new ReleaseAsset { Name = Json.Str(ad, "name"), DownloadUrl = Json.Str(ad, "browser_download_url"), Size = Json.Long(ad, "size") });
            return r;
        }

        /// <summary>The installer asset (GamesHub-Setup-*.exe); prefers the one whose name contains the release version.</summary>
        public ReleaseAsset FindInstaller()
        {
            List<ReleaseAsset> c = Assets.Where(x => InstallerName.IsMatch(x.Name) && IsHttps(x.DownloadUrl)).ToList();
            SemanticVersion v = Version;
            return (v == null ? null : c.FirstOrDefault(x => x.Name.IndexOf(v.ToString(), StringComparison.OrdinalIgnoreCase) >= 0))
                ?? c.FirstOrDefault();
        }

        /// <summary>Checksum asset for the installer: "&lt;installer&gt;.sha256", else any *.sha256 / SHA256SUMS.</summary>
        public ReleaseAsset FindChecksum(ReleaseAsset installer)
        {
            if (installer == null) return null;
            var sums = Assets.Where(x => IsHttps(x.DownloadUrl)).ToList();
            return sums.FirstOrDefault(x => x.Name.Equals(installer.Name + ".sha256", StringComparison.OrdinalIgnoreCase))
                ?? sums.FirstOrDefault(x => x.Name.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase)
                                            && x.Name.StartsWith("GamesHub-Setup-", StringComparison.OrdinalIgnoreCase))
                ?? sums.FirstOrDefault(x => x.Name.Equals("SHA256SUMS", StringComparison.OrdinalIgnoreCase)
                                            || x.Name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsHttps(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out Uri u) && u.Scheme == Uri.UriSchemeHttps;
    }

    public static class Sha256File
    {
        private static readonly Regex Gnu = new Regex(@"^([0-9a-fA-F]{64})(?:\s+\*?(.+?))?\s*$");
        private static readonly Regex Bsd = new Regex(@"^SHA256\s*\((.+)\)\s*=\s*([0-9a-fA-F]{64})\s*$", RegexOptions.IgnoreCase);

        /// <summary>
        /// Extracts the expected hash (lowercase hex) for fileName from a checksum file. Supports a bare hash,
        /// "hash  name" / "hash *name" (sha256sum) and "SHA256 (name) = hash" (BSD). A line without a file name
        /// matches any file. Returns null when nothing applies.
        /// </summary>
        public static string Parse(string text, string fileName)
        {
            if (string.IsNullOrEmpty(text)) return null;
            string anyName = null;
            foreach (string raw in text.Replace("\r", "").Split('\n'))
            {
                string line = raw.Trim().TrimStart('﻿');
                if (line.Length == 0 || line.StartsWith("#")) continue;
                string hash = null, name = null;
                Match m = Gnu.Match(line);
                if (m.Success) { hash = m.Groups[1].Value; name = m.Groups[2].Success ? m.Groups[2].Value.Trim() : null; }
                else if ((m = Bsd.Match(line)).Success) { name = m.Groups[1].Value.Trim(); hash = m.Groups[2].Value; }
                if (hash == null) continue;
                hash = hash.ToLowerInvariant();
                if (name == null) { anyName = anyName ?? hash; continue; }
                name = name.Replace('\\', '/');
                string bare = name.Substring(name.LastIndexOf('/') + 1);
                if (fileName == null || bare.Equals(fileName, StringComparison.OrdinalIgnoreCase)) return hash;
            }
            return anyName;
        }
    }
}
