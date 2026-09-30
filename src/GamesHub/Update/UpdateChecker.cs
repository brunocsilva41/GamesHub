// - Releases always come from the official repository (AppInfo.DefaultUpdateRepo); it is not configurable, because
//   it decides which executable runs.
// - CheckOnStartupAsync(): no-op unless settings.CheckUpdates. CheckAsync(): manual check; never throws, see LastError.
// - DownloadAndInstallAsync(): downloads the signed checksum manifest + signature, verifies them against the embedded
//   public key (UpdateVerifier), downloads GamesHub-Setup-X.Y.Z.exe into a fresh random folder under %TEMP%\GamesHub,
//   checks its SHA-256 while holding the file open without write/delete sharing, starts it with "/update /dir <AppDir>"
//   and returns true: the caller must then exit promptly (the installer waits up to 30 s for GamesHub.exe to exit).
//   Any missing or mismatching piece fails closed: nothing runs, and the release page is offered for a manual download.
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace GamesHub
{
    public sealed class UpdateChecker : IUpdateChecker
    {
        private const long MaxApiResponseBytes = 1024 * 1024;
        private const long MaxInstallerBytes = 256L * 1024 * 1024;
        private const long InstallerSizeMargin = 64 * 1024;
        private const int MaxRedirects = 5;
        private static readonly string[] ApiHosts = { "api.github.com" };
        private static int cleanedUp;

        private readonly AppSettings settings;
        private GitHubRelease lastRelease;

        /// <summary>pt-BR description of the last failure of CheckAsync/DownloadAndInstallAsync, or null.</summary>
        public string LastError { get; private set; }

        public UpdateChecker(AppSettings settings) { this.settings = settings ?? new AppSettings(); }

        public static string UpdateTempDir => Path.Combine(Path.GetTempPath(), AppInfo.Name);

        /// <summary>"owner/repo" whose Releases feed the updater: always the official one.</summary>
        public string Repo => AppInfo.DefaultUpdateRepo;

        /// <summary>Startup path: respects settings.CheckUpdates. Never throws.</summary>
        public Task<UpdateInfo> CheckOnStartupAsync()
        {
            if (!settings.CheckUpdates) return Task.FromResult(new UpdateInfo());
            return CheckAsync();
        }

        public async Task<UpdateInfo> CheckAsync()
        {
            LastError = null;
            // The installer of the previous update cannot delete itself: clean up once per run.
            if (System.Threading.Interlocked.Exchange(ref cleanedUp, 1) == 0) CleanDownloads();
            string repo = Repo;
            try
            {
                var url = new Uri("https://api.github.com/repos/" + repo + "/releases/latest");
                using (HttpClient http = NewClient(TimeSpan.FromSeconds(15)))
                using (HttpResponseMessage resp = await GetAsync(http, url, u => GitHubRelease.IsHttpsOn(u, ApiHosts),
                                                                 req => req.Headers.Accept.ParseAdd("application/vnd.github+json")).ConfigureAwait(false))
                {
                    if (resp.StatusCode == HttpStatusCode.NotFound)
                    {
                        LastError = "Nenhuma versão publicada foi encontrada em " + repo + ".";
                        Log.Info("Update check: no releases in " + repo);
                        return new UpdateInfo();
                    }
                    if ((int)resp.StatusCode == 403 || (int)resp.StatusCode == 429)
                    {
                        LastError = "O GitHub limitou as consultas por agora. Tente novamente mais tarde.";
                        Log.Warn("Update check rate-limited (" + (int)resp.StatusCode + ")");
                        return new UpdateInfo();
                    }
                    resp.EnsureSuccessStatusCode();
                    byte[] body = await ReadCappedAsync(resp, MaxApiResponseBytes).ConfigureAwait(false);
                    GitHubRelease rel = GitHubRelease.Parse(Encoding.UTF8.GetString(body));
                    lastRelease = rel;
                    UpdateInfo info = BuildInfo(rel, AppInfo.Version);
                    Log.Info("Update check: latest " + rel.TagName + ", current " + AppInfo.Version + ", available=" + info.Available);
                    return info;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is WebException || ex is OperationCanceledException
                                       || ex is IOException || ex is FormatException || ex is ArgumentException || ex is InvalidOperationException)
            {
                // Network failures, timeouts (TaskCanceledException), bad status codes/redirects, oversized or malformed JSON.
                LastError = "Não foi possível verificar atualizações. Verifique sua conexão.";
                Log.Warn("Update check failed", ex);
                return new UpdateInfo();
            }
        }

        /// <summary>Pure decision: is rel newer than currentVersion, and where is its installer?</summary>
        public static UpdateInfo BuildInfo(GitHubRelease rel, string currentVersion)
        {
            var info = new UpdateInfo();
            if (rel == null || rel.Draft) return info;
            SemanticVersion latest = rel.Version;
            if (latest == null || !SemanticVersion.TryParse(currentVersion, out SemanticVersion current)) return info;
            if (latest.CompareTo(current) <= 0) return info;
            ReleaseAsset installer = rel.FindInstaller();
            info.Available = true;
            info.Version = latest.ToString();
            info.Notes = Truncate(rel.Body ?? "", 4000);
            info.PageUrl = GitHubRelease.IsGitHubPage(rel.HtmlUrl) ? rel.HtmlUrl : "";
            info.DownloadUrl = installer != null && GitHubRelease.IsAllowedDownloadUrl(installer.DownloadUrl) ? installer.DownloadUrl : "";
            return info;
        }

        public async Task<bool> DownloadAndInstallAsync(UpdateInfo info, Action<int> progress)
        {
            LastError = null;
            progress = progress ?? (p => { });
            if (info == null || !info.Available || !GitHubRelease.IsAllowedDownloadUrl(info.DownloadUrl))
            {
                LastError = "Esta versão não tem um instalador para baixar. Abra a página da versão para baixá-la manualmente.";
                return false;
            }
            string dir = null;
            try
            {
                if (lastRelease == null || lastRelease.Version?.ToString() != info.Version) await CheckAsync().ConfigureAwait(false);
                GitHubRelease rel = lastRelease;
                if (rel == null || rel.Version?.ToString() != info.Version)
                    throw new InvalidOperationException("Release " + info.Version + " is no longer the latest one");
                ReleaseAsset installer = rel.FindInstaller();
                ReleaseAsset manifestAsset = rel.FindChecksum(installer);
                ReleaseAsset signatureAsset = rel.FindSignature(installer);
                if (installer == null || !GitHubRelease.IsAllowedDownloadUrl(installer.DownloadUrl))
                {
                    LastError = "Esta versão não tem um instalador para baixar. Abra a página da versão para baixá-la manualmente.";
                    return false;
                }
                if (manifestAsset == null) return RefuseUnverified(info, "release has no " + installer.Name + ".sha256");
                if (signatureAsset == null) return RefuseUnverified(info, "release has no " + installer.Name + ".sha256.sig");
                if (installer.Size <= 0 || installer.Size > MaxInstallerBytes) return RefuseUnverified(info, "installer size " + installer.Size);

                progress(0);
                using (HttpClient http = NewClient(TimeSpan.FromMinutes(10)))
                {
                    // 1) Authenticity first (small files): signature of the manifest, and the expected installer hash.
                    byte[] manifest = await DownloadBytesAsync(http, manifestAsset.DownloadUrl, UpdateVerifier.MaxManifestBytes).ConfigureAwait(false);
                    byte[] signature = await DownloadBytesAsync(http, signatureAsset.DownloadUrl, UpdateVerifier.MaxSignatureBytes).ConfigureAwait(false);
                    UpdateVerification v = UpdateVerifier.VerifyManifest(manifest, signature, installer.Name, UpdateSigningKey.Parameters, out string expected);
                    if (v != UpdateVerification.Ok) return RefuseUnverified(info, "manifest " + v + " (key " + UpdateSigningKey.KeyId + ")");

                    // 2) The installer, into a fresh random folder (older downloads are removed first).
                    CleanDownloads();
                    dir = Path.Combine(UpdateTempDir, Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(dir);
                    string file = Path.Combine(dir, installer.Name);
                    await DownloadFileAsync(http, installer.DownloadUrl, file, installer.Size + InstallerSizeMargin, installer.Size, progress).ConfigureAwait(false);

                    // 3) Hash and start while holding the file open: others may read (the loader) but not write/delete/rename.
                    string actual;
                    using (var locked = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        actual = ComputeSha256(locked);
                        if (string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                        {
                            progress(100);
                            string args = "/update /dir \"" + AppPaths.AppDir + "\"";
                            using (Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = true, WorkingDirectory = dir })) { }
                            Log.Info("Update installer verified (SHA-256 " + actual + ", key " + UpdateSigningKey.KeyId + ") and started: " + file + " " + args);
                            return true;
                        }
                    }
                    DeleteQuietly(dir);
                    return RefuseUnverified(info, "installer SHA-256 " + actual + " does not match the signed " + expected);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is WebException || ex is OperationCanceledException
                                       || ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException
                                       || ex is NotSupportedException || ex is FormatException || ex is ArgumentException
                                       || ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception)
            {
                LastError = "Não foi possível baixar a atualização. Verifique sua conexão e tente novamente.";
                Log.Warn("Update download/start failed", ex);
                if (dir != null) DeleteQuietly(dir);
                return false;
            }
        }

        /// <summary>Fail closed: nothing is executed; the release page is opened for a manual, user-driven download.</summary>
        private bool RefuseUnverified(UpdateInfo info, string reason)
        {
            Log.Warn("Update NOT installed, authenticity could not be verified: " + reason);
            string page = GitHubRelease.IsGitHubPage(info?.PageUrl) ? info.PageUrl : AppInfo.RepoUrl + "/releases";
            bool opened = ShellActions.OpenUrl(page);
            LastError = "Não foi possível confirmar que esta atualização é autêntica, por isso ela não foi instalada. "
                        + (opened ? "A página da versão foi aberta no navegador para você baixar o instalador manualmente."
                                  : "Baixe o instalador manualmente em " + page + ".");
            return false;
        }

        /// <summary>Best effort: removes earlier update folders and installers from %TEMP%\GamesHub.</summary>
        public static void CleanDownloads()
        {
            try
            {
                if (!Directory.Exists(UpdateTempDir)) return;
                foreach (string d in Directory.GetDirectories(UpdateTempDir)) DeleteQuietly(d);
                foreach (string f in Directory.GetFiles(UpdateTempDir, "GamesHub-Setup-*"))
                {
                    try { File.Delete(f); }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { Log.Warn("Could not remove old installer " + f, ex); }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { Log.Warn("Could not clean " + UpdateTempDir, ex); }
        }

        private static void DeleteQuietly(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { Log.Warn("Could not remove " + dir, ex); }
        }

        public static string ComputeSha256(string file)
        {
            using (FileStream fs = File.OpenRead(file)) return ComputeSha256(fs);
        }

        public static string ComputeSha256(Stream stream)
        {
            using (var sha = SHA256.Create())
            {
                var sb = new StringBuilder(64);
                foreach (byte b in sha.ComputeHash(stream)) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        // ------------------------------------------------------------------ HTTP

        private static HttpClient NewClient(TimeSpan timeout)
        {
            // Redirects are followed by GetAsync so every hop is checked against the host allowlist.
            var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate })
            {
                Timeout = timeout,
            };
            http.DefaultRequestHeaders.UserAgent.ParseAdd(AppInfo.Name + "/" + AppInfo.Version);
            return http;
        }

        /// <summary>GET that follows up to MaxRedirects redirects by hand; every URL (first and after each redirect) must
        /// satisfy <paramref name="allowed"/>, otherwise InvalidOperationException. Returns the final response (headers read).</summary>
        private static async Task<HttpResponseMessage> GetAsync(HttpClient http, Uri url, Func<Uri, bool> allowed, Action<HttpRequestMessage> prepare = null)
        {
            for (int hop = 0; ; hop++)
            {
                if (!allowed(url)) throw new InvalidOperationException("Update URL not allowed: " + url.GetLeftPart(UriPartial.Authority));
                var req = new HttpRequestMessage(HttpMethod.Get, url);
                prepare?.Invoke(req);
                HttpResponseMessage resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                int code = (int)resp.StatusCode;
                if (code < 300 || code > 399 || code == 304) return resp;
                Uri next = resp.Headers.Location;
                resp.Dispose();
                req.Dispose();
                if (next == null || hop >= MaxRedirects) throw new HttpRequestException("Invalid or too many redirects (" + code + ")");
                url = next.IsAbsoluteUri ? next : new Uri(url, next);
            }
        }

        private static async Task<byte[]> DownloadBytesAsync(HttpClient http, string url, long maxBytes)
        {
            using (HttpResponseMessage resp = await GetAsync(http, new Uri(url), GitHubRelease.IsAllowedDownloadUrl).ConfigureAwait(false))
            {
                resp.EnsureSuccessStatusCode();
                return await ReadCappedAsync(resp, maxBytes).ConfigureAwait(false);
            }
        }

        /// <summary>Reads the body, aborting (IOException) as soon as it exceeds maxBytes.</summary>
        private static async Task<byte[]> ReadCappedAsync(HttpResponseMessage resp, long maxBytes)
        {
            if (resp.Content.Headers.ContentLength > maxBytes) throw new IOException("Response larger than " + maxBytes + " bytes");
            using (Stream src = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false))
            using (var ms = new MemoryStream())
            {
                var buf = new byte[16384];
                int read;
                while ((read = await src.ReadAsync(buf, 0, buf.Length).ConfigureAwait(false)) > 0)
                {
                    if (ms.Length + read > maxBytes) throw new IOException("Response larger than " + maxBytes + " bytes");
                    ms.Write(buf, 0, read);
                }
                return ms.ToArray();
            }
        }

        private static async Task DownloadFileAsync(HttpClient http, string url, string file, long maxBytes, long expectedBytes, Action<int> progress)
        {
            using (HttpResponseMessage resp = await GetAsync(http, new Uri(url), GitHubRelease.IsAllowedDownloadUrl).ConfigureAwait(false))
            {
                resp.EnsureSuccessStatusCode();
                if (resp.Content.Headers.ContentLength > maxBytes) throw new IOException("Installer larger than announced");
                using (Stream src = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var dst = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var buf = new byte[81920];
                    long done = 0;
                    int read, last = -1;
                    while ((read = await src.ReadAsync(buf, 0, buf.Length).ConfigureAwait(false)) > 0)
                    {
                        done += read;
                        if (done > maxBytes) throw new IOException("Installer larger than announced");
                        await dst.WriteAsync(buf, 0, read).ConfigureAwait(false);
                        int pct = (int)Math.Min(99, done * 100 / expectedBytes);
                        if (pct != last) { last = pct; progress(pct); }
                    }
                }
            }
        }

        private static string Truncate(string s, int max) => s.Length <= max ? s : s.Substring(0, max) + "…";
    }
}
