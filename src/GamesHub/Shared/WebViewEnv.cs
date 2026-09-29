// One shared WebView2 environment for every WebView in the process
// (main window + quick-launch palette). Environments sharing a user-data folder must use identical
// options, so everyone MUST go through this class instead of calling CoreWebView2Environment.CreateAsync.
using System;
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
                    var options = new CoreWebView2EnvironmentOptions();
                    int port = DevToolsPort();
                    if (port > 0)
                    {
                        options.AdditionalBrowserArguments = "--remote-debugging-port=" + port + " --remote-allow-origins=*";
                        Log.Info("DevTools protocol enabled on 127.0.0.1:" + port + " (isolated test instance)");
                    }
                    _env = CoreWebView2Environment.CreateAsync(null, AppPaths.WebViewDir, options);
                }
                return _env;
            }
        }

        /// <summary>
        /// Automated tests observe the UI through the DevTools protocol. The port is honoured ONLY for isolated
        /// instances (GAMESHUB_DATA_DIR set), so a normal installation can never be told to expose it; setting it
        /// through the API also works where machine policy ignores WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS.
        /// </summary>
        private static int DevToolsPort()
        {
            if (!AppPaths.IsIsolated) return 0;
            string v = Environment.GetEnvironmentVariable("GAMESHUB_DEVTOOLS_PORT");
            return int.TryParse(v, out int p) && p >= 1024 && p <= 65535 ? p : 0;
        }

        /// <summary>Applies the standard virtual-host mappings (app + art) to a WebView.</summary>
        public static void MapHosts(CoreWebView2 core)
        {
            core.SetVirtualHostNameToFolderMapping(AppHost, AppPaths.WebDir, CoreWebView2HostResourceAccessKind.Allow);
            core.SetVirtualHostNameToFolderMapping(ArtHost, AppPaths.ArtDir, CoreWebView2HostResourceAccessKind.Allow);
        }
    }
}
