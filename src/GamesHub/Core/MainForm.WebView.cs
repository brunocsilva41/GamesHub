using System;
using Microsoft.Web.WebView2.Core;

namespace GamesHub
{
    /// <summary>WebView2 setup: environment, virtual hosts, lock-down and navigation policy.</summary>
    internal sealed partial class MainForm
    {
        /// <summary>Raised once the CoreWebView2 is configured, right before navigating to the UI.</summary>
        public event Action<CoreWebView2> WebViewReady;
        /// <summary>Raised if WebView2 cannot be initialized (argument: technical detail).</summary>
        public event Action<Exception> WebViewFailed;

        public CoreWebView2 Core => _web.CoreWebView2;

        public async void InitializeWebView()
        {
            try
            {
                // Shared environment: the quick-launch palette uses the same user-data folder and options.
                CoreWebView2Environment env = await WebViewEnv.GetAsync();
                await _web.EnsureCoreWebView2Async(env);
                CoreWebView2 core = _web.CoreWebView2;

                Configure(core.Settings);
                WebViewEnv.MapHosts(core);

                core.NavigationStarting += OnNavigationStarting;
                core.FrameNavigationStarting += OnFrameNavigationStarting;
                core.NewWindowRequested += OnNewWindowRequested;
                core.PermissionRequested += (s, e) => e.State = CoreWebView2PermissionState.Deny;
                core.LaunchingExternalUriScheme += (s, e) =>
                {
                    e.Cancel = true;
                    Log.Warn("Blocked external scheme: " + ShellActions.Truncate(e.Uri));
                };
                core.DownloadStarting += (s, e) =>
                {
                    e.Cancel = true;
                    Log.Warn("Blocked download: " + ShellActions.Truncate(e.DownloadOperation.Uri));
                };
                core.ContainsFullScreenElementChanged += (s, e) => SetFullscreen(core.ContainsFullScreenElement);
                core.ProcessFailed += OnProcessFailed;
                core.NavigationCompleted += (s, e) =>
                {
                    if (!e.IsSuccess) Log.Warn("Navigation failed: " + e.WebErrorStatus);
                };

                WebViewReady?.Invoke(core);
                core.Navigate(BridgeDto.StartUrl);
                Log.Info("WebView2 ready (runtime " + env.BrowserVersionString + ")");
            }
            // Resilience boundary: async void entry point; any failure must reach WebViewFailed (fallback UI).
            catch (Exception ex)
            {
                Log.Error("WebView2 initialization failed", ex);
                WebViewFailed?.Invoke(ex);
            }
        }

        private void Configure(CoreWebView2Settings s)
        {
            s.AreDevToolsEnabled = _debug;
            s.AreDefaultContextMenusEnabled = _debug;
            s.AreBrowserAcceleratorKeysEnabled = _debug; // blocks Ctrl+P, Ctrl+R/F5, Ctrl+F, F12... in release
            s.IsZoomControlEnabled = false;
            s.IsPinchZoomEnabled = false;
            s.IsSwipeNavigationEnabled = false;
            s.IsStatusBarEnabled = false;
            s.IsGeneralAutofillEnabled = false;
            s.IsPasswordAutosaveEnabled = false;
            s.AreHostObjectsAllowed = false;
            s.IsWebMessageEnabled = true;
            s.IsNonClientRegionSupportEnabled = true; // CSS app-region: drag moves the window
        }

        private void OnNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (BridgeDto.IsAppUri(e.Uri)) return;
            e.Cancel = true;
            if (e.IsUserInitiated && ShellActions.IsSafeExternalUrl(e.Uri)) ShellActions.OpenUrl(e.Uri);
            else Log.Warn("Blocked navigation: " + ShellActions.Truncate(e.Uri));
        }

        private void OnFrameNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (BridgeDto.IsAppUri(e.Uri) || e.Uri == "about:blank") return;
            e.Cancel = true;
            Log.Warn("Blocked frame navigation: " + ShellActions.Truncate(e.Uri));
        }

        private void OnNewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true; // never open WebView popups
            if (ShellActions.IsSafeExternalUrl(e.Uri)) ShellActions.OpenUrl(e.Uri);
            else Log.Warn("Blocked new window: " + ShellActions.Truncate(e.Uri));
        }

        private void OnProcessFailed(object sender, CoreWebView2ProcessFailedEventArgs e)
        {
            Log.Error("WebView2 process failed: " + e.ProcessFailedKind + " (" + e.Reason + ")");
            bool rendererGone = e.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessExited
                                || e.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessUnresponsive;
            if (!rendererGone) return;
            try
            {
                _web.CoreWebView2?.Navigate(BridgeDto.StartUrl);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException
                                       || ex is System.Runtime.InteropServices.COMException)
            {
                Log.Warn("Reload after renderer failure failed", ex);
            }
        }
    }
}
