// OWNER: DIST agent. Updater logic tests (no network).
using System;
using System.IO;

namespace GamesHub.Tests
{
    public static class SemanticVersionTests
    {
        public static void TestParsesPrefixAndParts()
        {
            SemanticVersion v = SemanticVersion.Parse("v2.10.3");
            Assert.Equal(2, v.Major); Assert.Equal(10, v.Minor); Assert.Equal(3, v.Patch);
            Assert.Equal("", v.PreRelease);
            Assert.Equal("2.1.0", SemanticVersion.Parse("V2.1").ToString());
            Assert.Equal("2.0.0-beta.2", SemanticVersion.Parse(" 2.0.0-beta.2+build.7 ").ToString());
        }

        public static void TestRejectsGarbage()
        {
            foreach (string s in new[] { null, "", "v", "abc", "1..2", "1.2.3.4.5", "1.2.x", "1.2.3-", "1.2.3-a..b", "-1.0" })
                Assert.False(SemanticVersion.TryParse(s, out _), "should reject '" + s + "'");
        }

        public static void TestOrdering()
        {
            string[] ordered = { "1.0.0-alpha", "1.0.0-alpha.1", "1.0.0-alpha.beta", "1.0.0-beta", "1.0.0-beta.2", "1.0.0-beta.11",
                                 "1.0.0-rc.1", "1.0.0", "1.0.1", "1.1.0", "2.0.0", "2.0.0.1", "10.0.0" };
            for (int i = 0; i + 1 < ordered.Length; i++)
                Assert.True(SemanticVersion.Compare(ordered[i], ordered[i + 1]) < 0, ordered[i] + " < " + ordered[i + 1]);
            Assert.Equal(0, SemanticVersion.Compare("v2.0.0", "2.0"));
            Assert.Equal(0, SemanticVersion.Compare("2.0.0+abc", "2.0.0"));
        }
    }

    public static class ReleaseParsingTests
    {
        private const string Sample = @"{
  ""tag_name"": ""v2.1.0"", ""name"": ""GamesHub 2.1.0"", ""draft"": false, ""prerelease"": false,
  ""html_url"": ""https://github.com/owner/gameshub/releases/tag/v2.1.0"",
  ""body"": ""## Novidades\n- Coisas"",
  ""assets"": [
    { ""name"": ""GamesHub-Setup-2.1.0.exe.sha256"", ""size"": 90, ""browser_download_url"": ""https://github.com/owner/gameshub/releases/download/v2.1.0/GamesHub-Setup-2.1.0.exe.sha256"" },
    { ""name"": ""source.zip"", ""size"": 1000, ""browser_download_url"": ""https://github.com/owner/gameshub/releases/download/v2.1.0/source.zip"" },
    { ""name"": ""GamesHub-Setup-2.0.9.exe"", ""size"": 5, ""browser_download_url"": ""https://github.com/owner/gameshub/releases/download/v2.1.0/GamesHub-Setup-2.0.9.exe"" },
    { ""name"": ""GamesHub-Setup-2.1.0.exe"", ""size"": 3145728, ""browser_download_url"": ""https://github.com/owner/gameshub/releases/download/v2.1.0/GamesHub-Setup-2.1.0.exe"" }
  ]
}";

        public static void TestParsesFields()
        {
            GitHubRelease r = GitHubRelease.Parse(Sample);
            Assert.Equal("v2.1.0", r.TagName);
            Assert.Equal(4, r.Assets.Count);
            Assert.Equal(3145728L, r.Assets[3].Size);
            Assert.Equal("2.1.0", r.Version.ToString());
            Assert.True(r.Body.Contains("Novidades"));
        }

        public static void TestPicksVersionedInstallerAndChecksum()
        {
            GitHubRelease r = GitHubRelease.Parse(Sample);
            ReleaseAsset exe = r.FindInstaller();
            Assert.Equal("GamesHub-Setup-2.1.0.exe", exe.Name);
            Assert.Equal("GamesHub-Setup-2.1.0.exe.sha256", r.FindChecksum(exe).Name);
        }

        public static void TestIgnoresNonHttpsAndMissingAssets()
        {
            GitHubRelease r = GitHubRelease.Parse(@"{""tag_name"":""2.2.0"",""assets"":[{""name"":""GamesHub-Setup-2.2.0.exe"",""browser_download_url"":""http://evil/x.exe""}]}");
            Assert.True(r.FindInstaller() == null, "http asset must be ignored");
            Assert.True(GitHubRelease.Parse(@"{""tag_name"":""2.2.0""}").FindInstaller() == null);
            Assert.True(r.FindChecksum(null) == null);
        }

        public static void TestBuildInfoComparesWithCurrent()
        {
            GitHubRelease r = GitHubRelease.Parse(Sample);
            UpdateInfo newer = UpdateChecker.BuildInfo(r, "2.0.0");
            Assert.True(newer.Available);
            Assert.Equal("2.1.0", newer.Version);
            Assert.True(newer.DownloadUrl.EndsWith("GamesHub-Setup-2.1.0.exe"));
            Assert.True(newer.PageUrl.StartsWith("https://github.com/"));
            Assert.False(UpdateChecker.BuildInfo(r, "2.1.0").Available, "same version");
            Assert.False(UpdateChecker.BuildInfo(r, "3.0.0-beta").Available, "older than current pre-release of next major");
            Assert.True(UpdateChecker.BuildInfo(r, "2.1.0-rc.1").Available, "release beats its own rc");
            r.Draft = true;
            Assert.False(UpdateChecker.BuildInfo(r, "2.0.0").Available, "drafts ignored");
        }

        public static void TestRejectsInvalidJson()
        {
            bool threw = false;
            try { GitHubRelease.Parse("[1,2]"); } catch (FormatException) { threw = true; }
            Assert.True(threw);
        }

        public static void TestNormalizesRepo()
        {
            Assert.Equal("owner/repo", UpdateChecker.NormalizeRepo("owner/repo"));
            Assert.Equal("owner/repo", UpdateChecker.NormalizeRepo(" https://github.com/owner/repo.git "));
            Assert.Equal("owner/my.repo", UpdateChecker.NormalizeRepo("github.com/owner/my.repo/releases"));
            Assert.Equal(null, UpdateChecker.NormalizeRepo(""));
            Assert.Equal(null, UpdateChecker.NormalizeRepo("not a repo"));
            Assert.Equal(null, UpdateChecker.NormalizeRepo("../../etc"));
        }

        public static void TestDisabledWithoutRepo()
        {
            var u = new UpdateChecker(new AppSettings { UpdateRepo = "", CheckUpdates = true });
            Assert.False(u.CheckAsync().Result.Available);
            Assert.False(new UpdateChecker(new AppSettings { UpdateRepo = "owner/repo", CheckUpdates = false })
                .CheckOnStartupAsync().Result.Available, "startup check honours CheckUpdates");
        }
    }

    public static class Sha256FileTests
    {
        private const string H1 = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";
        private const string H2 = "60303ae22b998861bce3b28f33eec1be758a213c86c93c076dbe9f558c11c752";

        public static void TestBareHash()
        {
            Assert.Equal(H1, Sha256File.Parse(H1.ToUpperInvariant() + "\r\n", "GamesHub-Setup-2.1.0.exe"));
        }

        public static void TestSha256SumFormat()
        {
            string text = H2 + "  other.exe\n" + H1 + " *GamesHub-Setup-2.1.0.exe\n";
            Assert.Equal(H1, Sha256File.Parse(text, "GamesHub-Setup-2.1.0.exe"));
            Assert.Equal(H2, Sha256File.Parse(text, "other.exe"));
            Assert.Equal(null, Sha256File.Parse(text, "missing.exe"));
        }

        public static void TestBsdFormatAndPaths()
        {
            Assert.Equal(H1, Sha256File.Parse("SHA256 (dist/package/GamesHub-Setup-2.1.0.exe) = " + H1, "gameshub-setup-2.1.0.exe"));
            Assert.Equal(H1, Sha256File.Parse("﻿# comment\n" + H1 + "  dist\\GamesHub-Setup-2.1.0.exe", "GamesHub-Setup-2.1.0.exe"));
        }

        public static void TestGarbage()
        {
            Assert.Equal(null, Sha256File.Parse("", "a"));
            Assert.Equal(null, Sha256File.Parse("not a hash  file.exe", "file.exe"));
            Assert.Equal(null, Sha256File.Parse(H1.Substring(1) + "  file.exe", "file.exe"));
        }

        public static void TestComputeMatchesKnownVector()
        {
            string f = Path.Combine(Path.GetTempPath(), "gameshub-sha-test-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(f, "test");
            try { Assert.Equal(H1, UpdateChecker.ComputeSha256(f)); }
            finally { File.Delete(f); }
        }
    }
}
