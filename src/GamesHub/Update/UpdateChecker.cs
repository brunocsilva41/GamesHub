// - CheckOnStartupAsync(): no-op unless settings.CheckUpdates && settings.UpdateRepo.
// - CheckAsync(): works whenever UpdateRepo is set (manual "check for updates"); never throws, see LastError.
// - DownloadAndInstallAsync(): downloads GamesHub-Setup-*.exe to %TEMP%\GamesHub, verifies .sha256 when published,
//   starts it with "/update /dir <AppDir>" and returns true — the caller (CORE) must then exit the app promptly
//   (the installer waits up to 30 s for GamesHub.exe to exit, then closes it).
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace GamesHub
{
    public sealed class UpdateChecker : IUpdateChecker
    {
        private static readonly Regex RepoPattern = new Regex(@"^[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})/[A-Za-z0-9._-]{1,100}$");
        private readonly AppSettings settings;
        private GitHubRelease lastRelease;

        /// <summary>pt-BR description of the last failure of CheckAsync/DownloadAndInstallAsync, or null.</summary>
        public string LastError { get; private set; }

        public UpdateChecker(AppSettings settings) { this.settings = settings ?? new AppSettings(); }

        public static string UpdateTempDir => Path.Combine(Path.GetTempPath(), AppInfo.Name);

        /// <summary>"owner/repo" from settings (also accepts a https://github.com/owner/repo URL); null if unset/invalid.</summary>
        public string Repo => NormalizeRepo(settings.UpdateRepo);

        public static string NormalizeRepo(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = s.Trim();
            Match m = Regex.Match(s, @"^(?:https?://)?(?:www\.)?github\.com/([^/\s]+/[^/\s#?]+)", RegexOptions.IgnoreCase);
            if (m.Success) s = m.Groups[1].Value;
            if (s.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - 4);
            s = s.Trim('/');
            return RepoPattern.IsMatch(s) ? s : null;
        }

        /// <summary>Startup path: respects settings.CheckUpdates. Never throws.</summary>
        public Task<UpdateInfo> CheckOnStartupAsync()
        {
            if (!settings.CheckUpdates || Repo == null) return Task.FromResult(new UpdateInfo());
            return CheckAsync();
        }

        public async Task<UpdateInfo> CheckAsync()
        {
            LastError = null;
            string repo = Repo;
            if (repo == null)
            {
                if (!string.IsNullOrWhiteSpace(settings.UpdateRepo)) LastError = "Repositório de atualizações inválido (use o formato dono/repositório).";
                return new UpdateInfo();
            }
            try
            {
                string url = "https://api.github.com/repos/" + repo + "/releases/latest";
                using (HttpClient http = NewClient(TimeSpan.FromSeconds(15)))
                using (var req = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    req.Headers.Accept.ParseAdd("application/vnd.github+json");
                    HttpResponseMessage resp = await http.SendAsync(req).ConfigureAwait(false);
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
                    string json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    GitHubRelease rel = GitHubRelease.Parse(json);
                    lastRelease = rel;
                    UpdateInfo info = BuildInfo(rel, AppInfo.Version);
                    Log.Info("Update check: latest " + rel.TagName + ", current " + AppInfo.Version + ", available=" + info.Available);
                    return info;
                }
            }
            catch (Exception ex)
            {
                LastError = "Não foi possível verificar atualizações. Verifique sua conexão.";
                Log.Warn("Update check failed", ex is AggregateException ae ? ae.Flatten().InnerException ?? ex : ex);
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
            info.PageUrl = GitHubRelease.IsHttps(rel.HtmlUrl) ? rel.HtmlUrl : "";
            info.DownloadUrl = installer?.DownloadUrl ?? "";
            return info;
        }

        public async Task<bool> DownloadAndInstallAsync(UpdateInfo info, Action<int> progress)
        {
            LastError = null;
            progress = progress ?? (p => { });
            if (info == null || !info.Available || !GitHubRelease.IsHttps(info.DownloadUrl))
            {
                LastError = "Esta versão não tem um instalador para baixar. Abra a página da versão para baixá-la manualmente.";
                return false;
            }
            string file = null;
            try
            {
                if (lastRelease == null || lastRelease.Version?.ToString() != info.Version) await CheckAsync().ConfigureAwait(false);
                GitHubRelease rel = lastRelease;
                ReleaseAsset installer = rel?.FindInstaller();
                ReleaseAsset sums = rel?.FindChecksum(installer);

                string name = Path.GetFileName(new Uri(info.DownloadUrl).AbsolutePath);
                if (!Regex.IsMatch(name, @"^GamesHub-Setup-[A-Za-z0-9._-]+\.exe$", RegexOptions.IgnoreCase)) name = "GamesHub-Setup-update.exe";
                Directory.CreateDirectory(UpdateTempDir);
                file = Path.Combine(UpdateTempDir, name);
                string part = file + ".part";

                progress(0);
                using (HttpClient http = NewClient(TimeSpan.FromMinutes(10)))
                {
                    using (HttpResponseMessage resp = await http.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
                    {
                        resp.EnsureSuccessStatusCode();
                        long total = resp.Content.Headers.ContentLength ?? installer?.Size ?? 0;
                        using (Stream src = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var dst = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            var buf = new byte[81920];
                            long done = 0;
                            int read, last = -1;
                            while ((read = await src.ReadAsync(buf, 0, buf.Length).ConfigureAwait(false)) > 0)
                            {
                                await dst.WriteAsync(buf, 0, read).ConfigureAwait(false);
                                done += read;
                                int pct = total > 0 ? (int)Math.Min(99, done * 100 / total) : 0;
                                if (pct != last) { last = pct; progress(pct); }
                            }
                        }
                    }

                    if (sums != null)
                    {
                        string text = await http.GetStringAsync(sums.DownloadUrl).ConfigureAwait(false);
                        string expected = Sha256File.Parse(text, name) ?? Sha256File.Parse(text, installer?.Name);
                        string actual = ComputeSha256(part);
                        if (expected == null || !string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                        {
                            File.Delete(part);
                            LastError = "O arquivo baixado não passou na verificação de integridade (SHA-256). Tente novamente.";
                            Log.Warn("Update SHA256 mismatch: expected " + expected + ", got " + actual);
                            return false;
                        }
                        Log.Info("Update SHA256 verified: " + actual);
                    }
                    else Log.Warn("Release has no .sha256 asset; installer not verified");
                }

                if (File.Exists(file)) File.Delete(file);
                File.Move(part, file);
                progress(100);

                string args = "/update /dir \"" + AppPaths.AppDir + "\"";
                using (Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = true, WorkingDirectory = UpdateTempDir })) { }
                Log.Info("Update installer started: " + file + " " + args);
                return true;
            }
            catch (Exception ex)
            {
                LastError = "Não foi possível baixar a atualização. Verifique sua conexão e tente novamente.";
                Log.Warn("Update download/start failed", ex);
                try { if (file != null && File.Exists(file + ".part")) File.Delete(file + ".part"); }
                catch (Exception e2) { Log.Warn("Could not remove partial download", e2); }
                return false;
            }
        }

        public static string ComputeSha256(string file)
        {
            using (var sha = SHA256.Create())
            using (FileStream fs = File.OpenRead(file))
            {
                var sb = new StringBuilder(64);
                foreach (byte b in sha.ComputeHash(fs)) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        private static HttpClient NewClient(TimeSpan timeout)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true, AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate })
            {
                Timeout = timeout,
            };
            http.DefaultRequestHeaders.UserAgent.ParseAdd(AppInfo.Name + "/" + AppInfo.Version);
            return http;
        }

        private static string Truncate(string s, int max) => s.Length <= max ? s : s.Substring(0, max) + "…";
    }
}
