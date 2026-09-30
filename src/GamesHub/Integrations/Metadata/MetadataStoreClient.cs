using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace GamesHub
{
    /// <summary>Raw HTTP outcome. Status 0 = network error / timeout (see Error).</summary>
    public sealed class MetadataHttpResult
    {
        public int Status;
        public string Body = "";
        public Exception Error;
    }

    public static class MetadataStoreClient
    {
        // "brazilian" is Steam's code for pt-BR (verified: genres come back as "Ação", "Aventura"...).
        // "portuguese" (pt-PT) is used only if the store rejects the request with HTTP 400.
        public const string Language = "brazilian";
        public const string FallbackLanguage = "portuguese";

        private static readonly Lazy<HttpClient> Client = new Lazy<HttpClient>(Create);

        public static string Url(string appId, string lang = Language)
            => "https://store.steampowered.com/api/appdetails?appids=" + Uri.EscapeDataString(appId) + "&l=" + lang + "&cc=br";

        private static HttpClient Create()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate };
            var c = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("GamesHub/" + AppInfo.Version);
            c.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            return c;
        }

        /// <summary>Never throws.</summary>
        public static async Task<MetadataHttpResult> GetAsync(string appId)
        {
            MetadataHttpResult r = await GetUrlAsync(Url(appId)).ConfigureAwait(false);
            if (r.Status == 400) r = await GetUrlAsync(Url(appId, FallbackLanguage)).ConfigureAwait(false);
            return r;
        }

        private static async Task<MetadataHttpResult> GetUrlAsync(string url)
        {
            try
            {
                using (HttpResponseMessage resp = await Client.Value.GetAsync(url).ConfigureAwait(false))
                {
                    byte[] bytes = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    return new MetadataHttpResult { Status = (int)resp.StatusCode, Body = Encoding.UTF8.GetString(bytes) };
                }
            }
            catch (Exception ex) when (ExpectedErrors.IsNetwork(ex))
            {
                return new MetadataHttpResult { Status = 0, Error = ex };
            }
        }
    }
}
