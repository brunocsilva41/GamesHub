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
                r.Assets.AddRange(list.OfType<IDictionary<string, object>>().Select(ad => new ReleaseAsset
                {
                    Name = Json.Str(ad, "name"),
                    DownloadUrl = Json.Str(ad, "browser_download_url"),
                    Size = Json.Long(ad, "size"),
                }));
            return r;
        }

        /// <summary>Hosts the updater downloads release assets from (github.com redirects to the CDN hosts).</summary>
        private static readonly string[] DownloadHosts = { "github.com", "objects.githubusercontent.com", "release-assets.githubusercontent.com" };
        private static readonly string[] PageHosts = { "github.com" };

        /// <summary>Exact installer file name for this release: "GamesHub-Setup-&lt;version&gt;.exe"; null without a version.</summary>
        public string InstallerName => Version == null ? null : "GamesHub-Setup-" + Version + ".exe";

        /// <summary>The installer asset, named exactly after the release version and served over https.</summary>
        public ReleaseAsset FindInstaller() => FindAsset(InstallerName);

        /// <summary>Checksum manifest of the installer: exactly "&lt;installer&gt;.sha256".</summary>
        public ReleaseAsset FindChecksum(ReleaseAsset installer) => installer == null ? null : FindAsset(installer.Name + ".sha256");

        /// <summary>Signature of the checksum manifest: exactly "&lt;installer&gt;.sha256.sig".</summary>
        public ReleaseAsset FindSignature(ReleaseAsset installer) => installer == null ? null : FindAsset(installer.Name + ".sha256.sig");

        private ReleaseAsset FindAsset(string name) => name == null ? null
            : Assets.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase) && IsHttps(x.DownloadUrl));

        public static bool IsHttps(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out Uri u) && u.Scheme == Uri.UriSchemeHttps;

        /// <summary>https on the default port, no credentials, and a GitHub release download/CDN host.</summary>
        public static bool IsAllowedDownloadUrl(Uri u) => IsHttpsOn(u, DownloadHosts);

        public static bool IsAllowedDownloadUrl(string url) => Uri.TryCreate(url, UriKind.Absolute, out Uri u) && IsAllowedDownloadUrl(u);

        /// <summary>An https://github.com/... page (release pages opened in the browser).</summary>
        public static bool IsGitHubPage(string url) => Uri.TryCreate(url, UriKind.Absolute, out Uri u) && IsHttpsOn(u, PageHosts);

        public static bool IsHttpsOn(Uri u, IEnumerable<string> hosts) =>
            u != null && u.IsAbsoluteUri && u.Scheme == Uri.UriSchemeHttps && u.IsDefaultPort && string.IsNullOrEmpty(u.UserInfo)
            && hosts.Any(h => string.Equals(u.Host, h, StringComparison.OrdinalIgnoreCase));
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
            foreach (string line in text.Replace("\r", "").Split('\n').Select(raw => raw.Trim().TrimStart('﻿'))
                                        .Where(l => l.Length > 0 && !l.StartsWith("#")))
            {
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

        /// <summary>
        /// Strict variant for signed manifests: only lines that name <paramref name="fileName"/> explicitly count (a bare
        /// hash or a path matches nothing), and two different hashes for the same name make the result null.
        /// </summary>
        public static string ParseExact(string text, string fileName)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(fileName)) return null;
            string found = null;
            foreach (string line in text.Replace("\r", "").Split('\n').Select(raw => raw.Trim().TrimStart('﻿'))
                                        .Where(l => l.Length > 0 && !l.StartsWith("#")))
            {
                string hash = null, name = null;
                Match m = Gnu.Match(line);
                if (m.Success && m.Groups[2].Success) { hash = m.Groups[1].Value; name = m.Groups[2].Value.Trim(); }
                else if ((m = Bsd.Match(line)).Success) { name = m.Groups[1].Value.Trim(); hash = m.Groups[2].Value; }
                if (hash == null || !string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase)) continue;
                hash = hash.ToLowerInvariant();
                if (found != null && found != hash) return null;
                found = hash;
            }
            return found;
        }
    }
}
