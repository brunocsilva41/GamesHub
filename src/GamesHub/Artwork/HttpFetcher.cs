using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace GamesHub
{
    public enum FetchStatus
    {
        Ok,
        /// <summary>Definitive "not there" (404/403/410...) — safe to negative-cache.</summary>
        NotFound,
        /// <summary>Server refused our credentials (401).</summary>
        Unauthorized,
        /// <summary>Temporary failure (timeout, 5xx, 429, connection error) — retry later.</summary>
        Transient,
        /// <summary>Not attempted: the network is in back-off after repeated failures.</summary>
        Offline,
    }

    public sealed class FetchResult
    {
        public FetchStatus Status;
        public int Code;
        public byte[] Body;
        public bool Ok => Status == FetchStatus.Ok;
        public bool Retryable => Status == FetchStatus.Transient || Status == FetchStatus.Offline;
        public string Text => Body == null ? "" : System.Text.Encoding.UTF8.GetString(Body);
    }

    /// <summary>
    /// Exponential back-off after consecutive network failures, so being offline does not
    /// produce a request storm (or a log storm). Thread-safe; clock injectable for tests.
    /// </summary>
    public sealed class NetworkBackoff
    {
        public const int FailuresBeforePause = 3;
        public static readonly TimeSpan FirstPause = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan MaxPause = TimeSpan.FromMinutes(15);

        private readonly Func<DateTime> _now;
        private readonly object _gate = new object();
        private int _failures;
        private TimeSpan _pause = FirstPause;
        private DateTime _pausedUntil = DateTime.MinValue;

        public NetworkBackoff(Func<DateTime> utcNow = null) { _now = utcNow ?? (() => DateTime.UtcNow); }

        public bool IsPaused { get { lock (_gate) return _now() < _pausedUntil; } }
        public DateTime PausedUntil { get { lock (_gate) return _pausedUntil; } }

        public void RecordSuccess()
        {
            lock (_gate)
            {
                if (_pausedUntil != DateTime.MinValue) Log.Info("Art: network is back, resuming downloads");
                _failures = 0;
                _pause = FirstPause;
                _pausedUntil = DateTime.MinValue;
            }
        }

        /// <summary>Returns true when this failure started a pause.</summary>
        public bool RecordFailure()
        {
            lock (_gate)
            {
                if (_now() < _pausedUntil) return false;
                if (++_failures < FailuresBeforePause) return false;
                _pausedUntil = _now() + _pause;
                Log.Warn("Art: network unavailable, pausing downloads for " + (int)_pause.TotalSeconds + "s");
                _pause = TimeSpan.FromTicks(Math.Min(_pause.Ticks * 2, MaxPause.Ticks));
                _failures = FailuresBeforePause - 1;   // one more failure after the pause re-arms it
                return true;
            }
        }
    }

    /// <summary>Reads HTTP bodies with a byte cap enforced while reading (after gzip/deflate decoding), so neither a
    /// missing/lying Content-Length nor a compression bomb can make us buffer an unbounded response.</summary>
    public static class BoundedRead
    {
        /// <summary>The whole stream, or null when it exceeds <paramref name="maxBytes"/>.</summary>
        public static async Task<byte[]> ReadAllAsync(Stream s, int maxBytes)
        {
            if (s == null) return new byte[0];
            using (var ms = new MemoryStream())
            {
                var buf = new byte[81920];
                int n;
                while ((n = await s.ReadAsync(buf, 0, buf.Length).ConfigureAwait(false)) > 0)
                {
                    if (ms.Length + n > maxBytes) return null;
                    ms.Write(buf, 0, n);
                }
                return ms.ToArray();
            }
        }

        /// <summary>Synchronous variant of <see cref="ReadAllAsync"/>.</summary>
        public static byte[] ReadAll(Stream s, int maxBytes)
        {
            if (s == null) return new byte[0];
            using (var ms = new MemoryStream())
            {
                var buf = new byte[81920];
                int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0)
                {
                    if (ms.Length + n > maxBytes) return null;
                    ms.Write(buf, 0, n);
                }
                return ms.ToArray();
            }
        }
    }

    /// <summary>
    /// TLS protocol selection. GamesHub is compiled by csc without a TargetFrameworkAttribute, so the .NET 4.8
    /// runtime applies its pre-4.7 compatibility default (Ssl3 | Tls 1.0) instead of SystemDefault — measured:
    /// ServicePointManager.SecurityProtocol reads "Ssl3, Tls" and store.steampowered.com fails the handshake, and
    /// assigning SystemDefault alone still fails (the legacy SChannel defaults stay in effect). So: when the
    /// runtime already uses SystemDefault (e.g. a future build that targets 4.7+), leave it to the OS; otherwise
    /// enable only TLS 1.2 and TLS 1.3 (1.3 is used where the OS supports it), dropping SSL 3 / TLS 1.0 / 1.1.
    /// </summary>
    public static class TlsPolicy
    {
        private const SecurityProtocolType Modern = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;

        public static void Ensure()
        {
            SecurityProtocolType cur = ServicePointManager.SecurityProtocol;
            SecurityProtocolType next = Choose(cur);
            if (next != cur) ServicePointManager.SecurityProtocol = next;
        }

        /// <summary>Pure: the protocol set to use given the current one.</summary>
        public static SecurityProtocolType Choose(SecurityProtocolType current)
            => current == SecurityProtocolType.SystemDefault ? current : Modern;
    }

    /// <summary>Shared HTTP client for artwork: max 3 concurrent requests, timeout, UA, back-off.</summary>
    public sealed class HttpFetcher : IDisposable
    {
        public const int MaxConcurrent = 3;
        public const int MaxBytes = 20 * 1024 * 1024;
        private readonly HttpClient _client;
        private readonly SemaphoreSlim _slots = new SemaphoreSlim(MaxConcurrent, MaxConcurrent);
        public NetworkBackoff Backoff { get; }

        public HttpFetcher(NetworkBackoff backoff = null)
        {
            TlsPolicy.Ensure();
            Backoff = backoff ?? new NetworkBackoff();
            var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate };
            _client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(25) };
            _client.DefaultRequestHeaders.UserAgent.ParseAdd(AppInfo.Name + "/" + AppInfo.Version);
        }

        public async Task<FetchResult> GetAsync(string url, string bearerToken = null)
        {
            if (Backoff.IsPaused) return new FetchResult { Status = FetchStatus.Offline };
            await _slots.WaitAsync().ConfigureAwait(false);
            try
            {
                if (Backoff.IsPaused) return new FetchResult { Status = FetchStatus.Offline };
                using (var req = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    if (!string.IsNullOrEmpty(bearerToken))
                        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
                    using (HttpResponseMessage resp = await _client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
                    {
                        int code = (int)resp.StatusCode;
                        if (code == 429 || code >= 500)
                        {
                            Backoff.RecordFailure();
                            return new FetchResult { Status = FetchStatus.Transient, Code = code };
                        }
                        Backoff.RecordSuccess();
                        if (code == 401) return new FetchResult { Status = FetchStatus.Unauthorized, Code = code };
                        if (!resp.IsSuccessStatusCode) return new FetchResult { Status = FetchStatus.NotFound, Code = code };
                        long? len = resp.Content.Headers.ContentLength;
                        if (len > MaxBytes) return new FetchResult { Status = FetchStatus.NotFound, Code = code };
                        byte[] body;
                        using (Stream s = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false))
                            body = await BoundedRead.ReadAllAsync(s, MaxBytes).ConfigureAwait(false);
                        if (body == null)
                        {
                            Log.Info("Art: response over " + MaxBytes + " bytes ignored: " + url);
                            return new FetchResult { Status = FetchStatus.NotFound, Code = code };
                        }
                        return new FetchResult { Status = FetchStatus.Ok, Code = code, Body = body };
                    }
                }
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is WebException || ex is System.IO.IOException)
            {
                // Connection/DNS/timeout failures: counted by the back-off (which logs once per pause).
                if (!Backoff.RecordFailure() && !Backoff.IsPaused)
                    Log.Info("Art: request failed (" + ex.GetType().Name + "): " + url);
                return new FetchResult { Status = FetchStatus.Transient };
            }
            finally
            {
                _slots.Release();
            }
        }

        public void Dispose()
        {
            _client.Dispose();
            _slots.Dispose();
        }
    }
}
