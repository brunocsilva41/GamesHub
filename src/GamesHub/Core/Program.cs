using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace GamesHub
{
    public static class Program
    {
        private const string WebView2DownloadUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";
        private static readonly IntPtr DpiPerMonitorV2 = new IntPtr(-4);
        private static DateTime _lastErrorDialog = DateTime.MinValue;

        [STAThread]
        public static int Main(string[] argv)
        {
            StartupArgs args = StartupArgs.Parse(argv);
            ConfigureProcess();

            using (SingleInstance instance = SingleInstance.Acquire())
            {
                if (!instance.IsFirst)
                {
                    bool sent = SingleInstance.Forward(argv);
                    if (!sent) Log.Warn("Another instance is running but did not answer the activation request");
                    return sent ? 0 : 1;
                }

                if (args.Quit) return 0; // nothing running to close
                AppPaths.EnsureDirs();
                InstallExceptionHandlers();
                Log.Info(AppInfo.Name + " " + AppInfo.Version + " starting (" + string.Join(" ", argv) + ")");

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                if (!IsWebViewRuntimeAvailable())
                {
                    ShowRuntimeMissing();
                    return 2;
                }

                // Composition root (docs/ARCHITECTURE.md).
                AppSettings settings = SettingsStore.Load();
                var art = new ArtworkService(settings);
                var library = new LibraryService(settings, art);
                var updater = new UpdateChecker(settings);
                var catalog = new GameCatalog(settings, library, art);

                using (var app = new AppController(settings, art, library, catalog, updater, args, instance))
                {
                    Application.Run(app);
                }
                Log.Info("Exited cleanly");
                return 0;
            }
        }

        private static void ConfigureProcess()
        {
            try
            {
                if (!CoreNative.SetProcessDpiAwarenessContext(DpiPerMonitorV2))
                    Log.Warn("SetProcessDpiAwarenessContext refused (already set by manifest?)");
            }
            catch (EntryPointNotFoundException)
            {
                CoreNative.SetProcessDPIAware(); // Windows older than 10 1703
            }

            // An isolated instance gets its own taskbar identity so it never shares the real app's Jump List.
            string appId = AppPaths.IsIsolated ? AppInfo.AppUserModelId + ".Isolated" : AppInfo.AppUserModelId;
            int hr = CoreNative.SetCurrentProcessExplicitAppUserModelID(appId);
            if (hr != 0) Log.Warn("SetCurrentProcessExplicitAppUserModelID failed: 0x" + hr.ToString("X8"));

            TlsPolicy.Ensure();   // TLS 1.2/1.3 only (see TlsPolicy: no TargetFrameworkAttribute → legacy Ssl3|Tls default)
            ServicePointManager.Expect100Continue = false;
        }

        private static void InstallExceptionHandlers()
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) =>
            {
                Log.Error("Unhandled UI exception", e.Exception);
                // Non-fatal: keep running, but don't bury the user in dialogs.
                if ((DateTime.Now - _lastErrorDialog).TotalSeconds < 30) return;
                _lastErrorDialog = DateTime.Now;
                MessageBox.Show("Ocorreu um erro inesperado. O GamesHub vai continuar funcionando.\n\n" + e.Exception.Message,
                    AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                Log.Error("Fatal unhandled exception (terminating=" + e.IsTerminating + ")", e.ExceptionObject as Exception);
                MessageBox.Show("O GamesHub encontrou um erro grave e precisa ser fechado.\n\nOs detalhes foram salvos em:\n" +
                                AppPaths.LogDir, AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                Log.Error("Unobserved task exception", e.Exception?.GetBaseException());
                e.SetObserved();
            };
        }

        private static bool IsWebViewRuntimeAvailable()
        {
            try
            {
                string version = CoreWebView2Environment.GetAvailableBrowserVersionString();
                return !string.IsNullOrEmpty(version);
            }
            catch (WebView2RuntimeNotFoundException)
            {
                return false;
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException || ex is BadImageFormatException
                                       || ex is System.IO.IOException || ex is System.Runtime.InteropServices.ExternalException
                                       || ex is InvalidOperationException || ex is UnauthorizedAccessException)
            {
                // e.g. WebView2Loader.dll missing: let initialization report the real error later.
                Log.Warn("WebView2 runtime probe failed", ex);
                return true;
            }
        }

        internal static void ShowRuntimeMissing()
        {
            Log.Warn("WebView2 Runtime not found");
            DialogResult choice = MessageBox.Show(
                "O GamesHub precisa do Microsoft Edge WebView2 Runtime para funcionar, mas ele não foi encontrado neste computador.\n\n" +
                "Deseja abrir a página de download da Microsoft agora? Depois de instalar, abra o GamesHub novamente.",
                AppInfo.Name + " — componente necessário", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (choice == DialogResult.Yes) ShellActions.OpenUrl(WebView2DownloadUrl);
        }
    }
}
