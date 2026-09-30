// globally, Cloudflare-challenge detection (PCGW answers 403 "Just a moment..." to non-browser clients).
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace GamesHub
{
    public static class PcgwHttp
    {
        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);
        private static DateTime _lastRequestUtc = DateTime.MinValue;
        private static DateTime _blockedUntilUtc = DateTime.MinValue;
        private static readonly TimeSpan BlockBackoff = TimeSpan.FromHours(6);
        private static readonly Lazy<HttpClient> Client = new Lazy<HttpClient>(Create);

        public static string UserAgent => "GamesHub/" + AppInfo.Version + " (+https://github.com)";

        /// <summary>True while PCGW is known to answer with a bot challenge (API unusable).</summary>
        public static bool IsBlocked => DateTime.UtcNow < _blockedUntilUtc;

        private static HttpClient Create()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                AllowAutoRedirect = true,
            };
            var c = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(3) };
            c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
            c.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json, */*;q=0.5");
            return c;
        }

        /// <summary>GET a PCGW URL (rate-limited). Returns the body, or null on any failure. Never throws.</summary>
        public static async Task<string> GetPcgwAsync(string url)
        {
            if (IsBlocked) return null;
            await Gate.WaitAsync().ConfigureAwait(false);
            try
            {
                TimeSpan wait = _lastRequestUtc.AddSeconds(1) - DateTime.UtcNow;
                if (wait > TimeSpan.Zero) await Task.Delay(wait).ConfigureAwait(false);
                _lastRequestUtc = DateTime.UtcNow;
                if (IsBlocked) return null;
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                using (HttpResponseMessage resp = await Client.Value.GetAsync(url, cts.Token).ConfigureAwait(false))
                {
                    string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (IsChallenge(resp, body))
                    {
                        _blockedUntilUtc = DateTime.UtcNow + BlockBackoff;
                        Log.Warn("PCGW: Cloudflare challenge (HTTP " + (int)resp.StatusCode + "); API disabled for "
                                 + BlockBackoff.TotalHours + "h, using the Ludusavi manifest instead");
                        return null;
                    }
                    if (!resp.IsSuccessStatusCode)
                    {
                        Log.Warn("PCGW: HTTP " + (int)resp.StatusCode + " for " + url);
                        return null;
                    }
                    return body;
                }
            }
            catch (Exception ex) when (ExpectedErrors.IsNetwork(ex))
            {
                Log.Warn("PCGW: request failed " + url, ex);
                return null;
            }
            finally { Gate.Release(); }
        }

        public static bool IsChallenge(HttpResponseMessage resp, string body)
        {
            int code = (int)resp.StatusCode;
            if (code != 403 && code != 503 && code != 429) return false;
            if (resp.Headers.TryGetValues("cf-mitigated", out var v) && v.Any(x => x.IndexOf("challenge", StringComparison.OrdinalIgnoreCase) >= 0))
                return true;
            return body != null && (body.Contains("_cf_chl_opt") || body.Contains("<title>Just a moment"));
        }

        /// <summary>Streams a (large) file to disk via a temp file. Returns false on failure. Never throws.</summary>
        public static async Task<bool> DownloadToFileAsync(string url, string file, TimeSpan timeout)
        {
            string tmp = file + ".download";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                using (var cts = new CancellationTokenSource(timeout))
                using (HttpResponseMessage resp = await Client.Value.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false))
                {
                    if (!resp.IsSuccessStatusCode)
                    {
                        Log.Warn("PCGW: download HTTP " + (int)resp.StatusCode + " for " + url);
                        return false;
                    }
                    using (Stream src = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var dst = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                        await src.CopyToAsync(dst, 81920, cts.Token).ConfigureAwait(false);
                }
                if (File.Exists(file)) File.Replace(tmp, file, null);
                else File.Move(tmp, file);
                return true;
            }
            catch (Exception ex) when (ExpectedErrors.IsNetwork(ex) || ExpectedErrors.IsFileSystem(ex))
            {
                Log.Warn("PCGW: download failed " + url, ex);
                try { if (File.Exists(tmp)) File.Delete(tmp); }
                catch (Exception ex2) when (ExpectedErrors.IsFileSystem(ex2)) { Log.Warn("PCGW: cannot delete " + tmp, ex2); }
                return false;
            }
        }
    }
}
