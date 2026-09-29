// OWNER: integration (lead). One shared WebView2 environment for every WebView in the process
// (main window + quick-launch palette). Environments sharing a user-data folder must use identical
// options, so everyone MUST go through this class instead of calling CoreWebView2Environment.CreateAsync.
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace GamesHub
{
    public static class WebViewEnv
    {
        public const string AppHost = "app.gameshub.example";
        public const string ArtHost = "art.gameshub.example";
        private static Task<CoreWebView2Environment> _env;
        private static readonly object Gate = new object();

        /// <summary>Must be first called on the UI thread.</summary>
        public static Task<CoreWebView2Environment> GetAsync()
        {
            lock (Gate)
            {
                if (_env == null)
                {
                    AppPaths.EnsureDirs();
                    _env = CoreWebView2Environment.CreateAsync(null, AppPaths.WebViewDir, new CoreWebView2EnvironmentOptions());
                }
                return _env;
            }
        }

        /// <summary>Applies the standard virtual-host mappings (app + art) to a WebView.</summary>
        public static void MapHosts(CoreWebView2 core)
        {
            core.SetVirtualHostNameToFolderMapping(AppHost, AppPaths.WebDir, CoreWebView2HostResourceAccessKind.Allow);
            core.SetVirtualHostNameToFolderMapping(ArtHost, AppPaths.ArtDir, CoreWebView2HostResourceAccessKind.Allow);
        }
    }
}
