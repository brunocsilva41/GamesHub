using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace GamesHub.Installer
{
    internal enum SetupMode { Interactive, Silent, Update }

    internal sealed class SetupOptions
    {
        public SetupMode Mode = SetupMode.Interactive;
        public string InstallDir;
        /// <summary>null = don't touch settings.json (update mode).</summary>
        public string GamesDir;
        public bool DesktopShortcut = true;
        public bool StartMenuShortcut = true;
        public bool StartWithWindows;
        public bool Help;
    }

    internal sealed class SetupResult
    {
        public string InstallDir;
        public string ExePath;
        public bool UpgradedFromV1;
        public int RepointedShortcuts;
    }

    /// <summary>What is currently installed in a folder.</summary>
    internal sealed class ExistingInstall
    {
        public bool HasV1, HasV2;
        public string Version = "";
        /// <summary>gamesDir from v1's config.json next to GamesLounge.exe, or null.</summary>
        public string LegacyGamesDir;

        public bool Any => HasV1 || HasV2;

        public static ExistingInstall Detect(string dir)
        {
            var e = new ExistingInstall();
            try
            {
                if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return e;
                e.HasV1 = File.Exists(Path.Combine(dir, Product.LegacyExeName));
                string exe = Path.Combine(dir, Product.ExeName);
                if (File.Exists(exe))
                {
                    e.HasV2 = true;
                    e.Version = System.Diagnostics.FileVersionInfo.GetVersionInfo(exe).ProductVersion ?? "";
                }
                string cfg = Path.Combine(dir, "config.json");
                if (File.Exists(cfg) && new JavaScriptSerializer().DeserializeObject(File.ReadAllText(cfg)) is Dictionary<string, object> d
                    && d.TryGetValue("gamesDir", out object g) && g is string gs && gs.Length > 0)
                    e.LegacyGamesDir = gs;
            }
            catch (Exception ex) { InstallerLog.Warn("Detect existing install failed", ex); }
            return e;
        }
    }

    /// <summary>The zipped app folder embedded in Setup.exe by build.ps1 (-resource:payload.zip,GamesHub.Payload.zip).</summary>
    internal static class Payload
    {
        public const string ResourceName = "GamesHub.Payload.zip";

        public static Stream Open()
        {
            Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
            if (s == null) throw new InvalidOperationException("O pacote de instalação está incompleto (payload ausente). Baixe o instalador novamente.");
            return s;
        }

        public static long UncompressedSize()
        {
            try
            {
                using (Stream s = Open())
                using (var zip = new ZipArchive(s, ZipArchiveMode.Read))
                {
                    long total = 0;
                    foreach (ZipArchiveEntry en in zip.Entries) total += en.Length;
                    return total;
                }
            }
            catch (Exception ex) { InstallerLog.Warn("Cannot read payload size", ex); return 0; }
        }

        /// <summary>App logo from the payload's gamehub-app.ico, or null.</summary>
        public static Image Logo()
        {
            try
            {
                using (Stream s = Open())
                using (var zip = new ZipArchive(s, ZipArchiveMode.Read))
                {
                    ZipArchiveEntry en = zip.GetEntry("gamehub-app.ico");
                    if (en == null) return null;
                    var ms = new MemoryStream();
                    using (Stream es = en.Open()) es.CopyTo(ms);
                    ms.Position = 0;
                    using (var icon = new Icon(ms, 128, 128)) return icon.ToBitmap();
                }
            }
            catch (Exception ex) { InstallerLog.Warn("Cannot load logo", ex); return null; }
        }
    }

    internal sealed class SetupEngine
    {
        /// <summary>(percent 0..100, pt-BR status)</summary>
        public Action<int, string> Progress = (p, s) => { };
        /// <summary>Asked once when the app is running; false cancels the install.</summary>
        public Func<bool> ConfirmCloseApp = () => true;
        /// <summary>Asked when the app didn't close gracefully; true = kill it.</summary>
        public Func<bool> ConfirmKill = () => true;

        private void Report(int p, string s) { InstallerLog.Info("[" + p + "%] " + s); Progress(p, s); }

        /// <summary>If dir is an existing non-empty folder that isn't a GamesHub install, returns dir\GamesHub.</summary>
        public static string ResolveInstallDir(string dir)
        {
            string full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(dir.Trim().Trim('"'))).TrimEnd('\\');
            if (PathSafety.IsDangerousDir(full)) return Path.Combine(full, Product.Name);
            if (Directory.Exists(full) && Directory.GetFileSystemEntries(full).Length > 0 && !ExistingInstall.Detect(full).Any
                && !File.Exists(Path.Combine(full, Product.ManifestFile)))
                return Path.Combine(full, Product.Name);
            return full;
        }

        public SetupResult Run(SetupOptions o)
        {
            string dir = ResolveInstallDir(o.InstallDir);
            if (PathSafety.IsDangerousDir(dir)) throw new InvalidOperationException("Pasta de instalação inválida: " + dir);
            var result = new SetupResult { InstallDir = dir, ExePath = Path.Combine(dir, Product.ExeName) };
            InstallerLog.Info("Installing " + Product.Version + " to " + dir + " (mode " + o.Mode + ")");

            Report(2, "Preparando…");
            try { Directory.CreateDirectory(dir); }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Não foi possível criar a pasta '" + dir + "'. Verifique as permissões ou escolha outra pasta.", ex);
            }

            // 1. running app
            if (o.Mode == SetupMode.Update)
            {
                Report(4, "Aguardando o GamesHub fechar…");
                AppProcesses.WaitForExit(dir, 30000);
            }
            if (AppProcesses.AnyRunning(dir))
            {
                if (!ConfirmCloseApp()) throw new OperationCanceledException();
                Report(5, "Fechando o GamesHub…");
                if (!AppProcesses.CloseAll(dir, 6000, ConfirmKill))
                    throw new InvalidOperationException("O GamesHub ainda está em execução. Feche-o (inclusive na bandeja do sistema) e tente novamente.");
            }

            // 2. v1 leftovers (config.json is kept: the app migrates it on first start)
            ExistingInstall existing = ExistingInstall.Detect(dir);
            if (existing.HasV1)
            {
                Report(8, "Removendo a versão anterior…");
                result.UpgradedFromV1 = true;
                DeleteFileRetry(Path.Combine(dir, Product.LegacyExeName));
                DeleteFileRetry(Path.Combine(dir, Product.LegacyExeName + ".config"));
            }

            // 3. files
            long bytes = Extract(dir, 10, 85);

            // 4. settings seed
            bool minimized = true;
            if (o.GamesDir != null)
            {
                Report(87, "Salvando preferências…");
                minimized = SeedSettings(o.GamesDir, o.StartWithWindows);
            }

            // 5. shortcuts
            Report(90, "Criando atalhos…");
            result.RepointedShortcuts = Shortcuts.RepointExisting(dir);
            TryStep("desktop shortcut", () => { if (o.DesktopShortcut) Shortcuts.Create(Shortcuts.DesktopLink, dir); });
            TryStep("start menu shortcut", () => { if (o.StartMenuShortcut) Shortcuts.Create(Shortcuts.StartMenuLink, dir); });

            // 6. autostart
            Report(93, "Configurando a inicialização…");
            TryStep("autostart", () => ConfigureAutostart(result.ExePath, dir, o.StartWithWindows, minimized));

            // 7. Apps & features entry
            Report(96, "Registrando no Windows…");
            RegisterUninstall(dir, result.ExePath, bytes);
            Shortcuts.NotifyShell();

            Report(100, "Concluído.");
            InstallerLog.Info("Install finished OK");
            return result;
        }

        private static void TryStep(string what, Action a)
        {
            try { a(); }
            catch (Exception ex) { InstallerLog.Warn("Step failed (non-fatal): " + what, ex); }
        }

        private long Extract(string dir, int from, int to)
        {
            Report(from, "Copiando arquivos…");
            // stale UI files from v1 / older builds would otherwise linger in web\
            DeleteDirRetry(Path.Combine(dir, "web"));

            var written = new List<string>();
            long total = 0, done = 0;
            using (Stream s = Payload.Open())
            using (var zip = new ZipArchive(s, ZipArchiveMode.Read))
            {
                foreach (ZipArchiveEntry en in zip.Entries) total += en.Length;
                int lastPct = -1;
                foreach (ZipArchiveEntry en in zip.Entries)
                {
                    string rel = en.FullName.Replace('/', '\\').TrimStart('\\');
                    string dest = Path.GetFullPath(Path.Combine(dir, rel));
                    if (!PathSafety.IsInside(dest, dir)) throw new InvalidOperationException("Entrada inválida no pacote: " + en.FullName);
                    if (rel.EndsWith("\\")) { Directory.CreateDirectory(dest); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    WriteEntry(en, dest);
                    written.Add(rel);
                    done += en.Length;
                    int pct = from + (int)((to - from) * (total == 0 ? 1.0 : (double)done / total));
                    if (pct != lastPct) { lastPct = pct; Progress(pct, "Copiando " + rel + "…"); }
                }
            }
            written.Add(Product.ManifestFile);
            File.WriteAllLines(Path.Combine(dir, Product.ManifestFile), written, new UTF8Encoding(false));
            InstallerLog.Info("Extracted " + written.Count + " files, " + total + " bytes");
            return total;
        }

        private static void WriteEntry(ZipArchiveEntry en, string dest)
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    using (Stream src = en.Open())
                    using (var dst = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None))
                        src.CopyTo(dst);
                    File.SetLastWriteTime(dest, en.LastWriteTime.LocalDateTime);
                    return;
                }
                catch (IOException ex) when (attempt < 8)
                {
                    InstallerLog.Warn("Retry " + attempt + " writing " + dest, ex);
                    Thread.Sleep(400);
                }
                catch (UnauthorizedAccessException ex) when (attempt < 8)
                {
                    InstallerLog.Warn("Retry " + attempt + " writing " + dest, ex);
                    Thread.Sleep(400);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("Não foi possível gravar '" + Path.GetFileName(dest)
                        + "'. O arquivo pode estar em uso — feche o GamesHub e tente novamente.", ex);
                }
            }
        }

        private static void DeleteFileRetry(string file)
        {
            for (int attempt = 1; File.Exists(file); attempt++)
            {
                try { File.Delete(file); InstallerLog.Info("Deleted " + file); return; }
                catch (Exception ex) when (attempt < 8) { InstallerLog.Warn("Retry delete " + file, ex); Thread.Sleep(400); }
            }
        }

        private static void DeleteDirRetry(string d)
        {
            for (int attempt = 1; Directory.Exists(d); attempt++)
            {
                try { Directory.Delete(d, true); InstallerLog.Info("Deleted dir " + d); return; }
                catch (Exception ex) when (attempt < 8) { InstallerLog.Warn("Retry delete dir " + d, ex); Thread.Sleep(400); }
            }
        }

        /// <summary>Creates settings.json with just gamesDir (plus startWithWindows/startMinimized when autostart was
        /// chosen) when it doesn't exist yet. Existing settings are never overwritten, except that choosing autostart
        /// flips startWithWindows on. Returns whether autostart should start minimized (settings.startMinimized).</summary>
        internal static bool SeedSettings(string gamesDir, bool startWithWindows)
        {
            try
            {
                var js = new JavaScriptSerializer();
                string file = Product.SettingsFile;
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                if (!File.Exists(file))
                {
                    var d = new Dictionary<string, object> { ["gamesDir"] = Path.GetFullPath(gamesDir) };
                    if (startWithWindows) { d["startWithWindows"] = true; d["startMinimized"] = true; }
                    File.WriteAllText(file, js.Serialize(d), new UTF8Encoding(false));
                    InstallerLog.Info("Seeded " + file);
                    return true;
                }
                if (js.DeserializeObject(File.ReadAllText(file, Encoding.UTF8)) is Dictionary<string, object> cur)
                {
                    if (startWithWindows && !(cur.TryGetValue("startWithWindows", out object sw) && sw is bool b && b))
                    {
                        cur["startWithWindows"] = true;
                        File.WriteAllText(file, js.Serialize(cur), new UTF8Encoding(false));
                        InstallerLog.Info("settings.json: startWithWindows=true");
                    }
                    return cur.TryGetValue("startMinimized", out object sm) && sm is bool m && m;
                }
            }
            catch (Exception ex) { InstallerLog.Warn("Could not seed settings.json", ex); }
            return true;
        }

        /// <summary>HKCU Run value, same format as the app writes: "exe" [--minimized].</summary>
        private static void ConfigureAutostart(string exe, string dir, bool enable, bool minimized)
        {
            using (RegistryKey run = Registry.CurrentUser.CreateSubKey(Product.RunKey))
            {
                string current = run.GetValue(Product.RunValue) as string;
                bool ours = current != null && current.IndexOf(dir, StringComparison.OrdinalIgnoreCase) >= 0;
                if (!enable && !ours) return;
                if (ours && !enable) minimized = current.IndexOf(Product.AutostartArgs, StringComparison.OrdinalIgnoreCase) >= 0;
                string value = Product.Quote(exe) + (minimized ? " " + Product.AutostartArgs : "");
                run.SetValue(Product.RunValue, value);
                InstallerLog.Info("Run key set: " + value);
            }
        }

        private static void RegisterUninstall(string dir, string exe, long bytes)
        {
            string uninst = Path.Combine(dir, Product.UninstallerName);
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Product.UninstallKey))
            {
                k.SetValue("DisplayName", Product.Name);
                k.SetValue("DisplayVersion", Product.Version);
                k.SetValue("Publisher", Product.Publisher);
                k.SetValue("DisplayIcon", exe + ",0");
                k.SetValue("InstallLocation", dir);
                k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                k.SetValue("UninstallString", Product.Quote(uninst));
                k.SetValue("QuietUninstallString", Product.Quote(uninst) + " /silent");
                k.SetValue("EstimatedSize", (int)Math.Min(int.MaxValue, bytes / 1024), RegistryValueKind.DWord);
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                string[] v = Product.Version.Split('.');
                k.SetValue("VersionMajor", int.Parse(v[0]), RegistryValueKind.DWord);
                k.SetValue("VersionMinor", int.Parse(v[1]), RegistryValueKind.DWord);
            }
            InstallerLog.Info("Uninstall key written");
        }
    }

    internal static class Prereqs
    {
        /// <summary>Installed WebView2 Runtime version, or null.</summary>
        public static string WebView2Version()
        {
            const string client = @"Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";
            string[] keys =
            {
                @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\" + client,
                @"HKEY_LOCAL_MACHINE\SOFTWARE\" + client,
                @"HKEY_CURRENT_USER\Software\" + client,
            };
            foreach (string k in keys)
            {
                try
                {
                    string v = Registry.GetValue(k, "pv", null) as string;
                    if (!string.IsNullOrEmpty(v) && v != "0.0.0.0") return v;
                }
                catch (Exception ex) { InstallerLog.Warn("WebView2 probe failed: " + k, ex); }
            }
            return null;
        }

        /// <summary>True when .NET Framework 4.8+ is installed (Release DWORD ≥ 528040).</summary>
        public static bool HasNet48()
        {
            try
            {
                object r = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full", "Release", null);
                return r is int i && i >= 528040;
            }
            catch (Exception ex) { InstallerLog.Warn(".NET probe failed", ex); return true; }
        }
    }
}
