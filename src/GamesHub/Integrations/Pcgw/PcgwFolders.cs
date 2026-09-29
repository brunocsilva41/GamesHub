using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace GamesHub
{
    public sealed class PcgwFolders : IPcgwFolders
    {
        private static readonly Guid FolderLocalAppDataLow = new Guid("A520A1A4-1780-4FF6-BD18-167343C5AF16");
        private static readonly Guid FolderPublic = new Guid("DFDF76A2-C82A-4D63-906A-5644AC457385");
        private static readonly Guid FolderDocuments = new Guid("FDD39AD0-238F-46AF-ADB4-6C85480369C7");

        private readonly Game _game;
        public PcgwFolders(Game game) { _game = game; }

        public string Get(string key)
        {
            switch (PcgwPaths.NormalizeKey(key))
            {
                case "appdata": return Special(Environment.SpecialFolder.ApplicationData);
                case "localappdata": return Special(Environment.SpecialFolder.LocalApplicationData);
                case "userprofile": return Special(Environment.SpecialFolder.UserProfile);
                case "userprofile\\documents":
                    return KnownFolder(FolderDocuments) ?? Special(Environment.SpecialFolder.MyDocuments);
                case "userprofile\\appdata\\locallow":
                    return KnownFolder(FolderLocalAppDataLow)
                           ?? Combine(Special(Environment.SpecialFolder.UserProfile), @"AppData\LocalLow");
                case "public":
                    return KnownFolder(FolderPublic) ?? NullIfEmpty(Environment.GetEnvironmentVariable("PUBLIC"));
                case "allusersprofile":
                case "programdata": return Special(Environment.SpecialFolder.CommonApplicationData);
                case "windir": return Special(Environment.SpecialFolder.Windows);
                case "syswow64": return Special(Environment.SpecialFolder.SystemX86);
                case "programfiles": return Special(Environment.SpecialFolder.ProgramFiles);
                case "username": return NullIfEmpty(Environment.UserName);
                case "game": return GameDir(_game);
                case "steam": return SteamRoot();
                case "ubisoftconnect":
                case "uplay": return UbisoftRoot();
                default: return null; // osxhome, linuxhome, xdg*, unknown
            }
        }

        public static string GameDir(Game game)
        {
            if (game == null) return null;
            try
            {
                if (!string.IsNullOrWhiteSpace(game.InstallDir)) return game.InstallDir.TrimEnd('\\', '/');
                if (!string.IsNullOrWhiteSpace(game.Exe) && Path.IsPathRooted(game.Exe))
                    return Path.GetDirectoryName(game.Exe);
            }
            catch (Exception ex) { Log.Warn("PCGW: bad game path for " + game.Id, ex); }
            return null;
        }

        public static string SteamRoot()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    string p = k?.GetValue("SteamPath") as string;
                    if (!string.IsNullOrWhiteSpace(p)) return ExactCase(Path.GetFullPath(p.Replace('/', '\\')));
                }
            }
            catch (Exception ex) { Log.Warn("PCGW: cannot read SteamPath", ex); }
            return null;
        }

        /// <summary>Real on-disk casing of an existing directory (Steam stores "c:/program files (x86)/steam").</summary>
        public static string ExactCase(string dir)
        {
            try
            {
                var di = new DirectoryInfo(dir);
                if (!di.Exists) return dir;
                if (di.Parent == null) return di.FullName.ToUpperInvariant();
                DirectoryInfo match = di.Parent.GetDirectories(di.Name).FirstOrDefault();
                return Path.Combine(ExactCase(di.Parent.FullName), match?.Name ?? di.Name);
            }
            catch (Exception ex) { Log.Warn("PCGW: cannot get exact case of " + dir, ex); return dir; }
        }

        private static string UbisoftRoot()
        {
            try
            {
                using (RegistryKey k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32)
                                                  .OpenSubKey(@"SOFTWARE\Ubisoft\Launcher"))
                {
                    string p = k?.GetValue("InstallDir") as string;
                    if (!string.IsNullOrWhiteSpace(p)) return p.Replace('/', '\\').TrimEnd('\\');
                }
            }
            catch (Exception ex) { Log.Warn("PCGW: cannot read Ubisoft InstallDir", ex); }
            return null;
        }

        private static string Special(Environment.SpecialFolder f) => NullIfEmpty(Environment.GetFolderPath(f));
        private static string NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;
        private static string Combine(string a, string b) => a == null ? null : Path.Combine(a, b);

        [DllImport("shell32.dll")]
        private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint flags, IntPtr token, out IntPtr path);

        private static string KnownFolder(Guid id)
        {
            IntPtr p = IntPtr.Zero;
            try
            {
                if (SHGetKnownFolderPath(id, 0, IntPtr.Zero, out p) != 0) return null;
                return NullIfEmpty(Marshal.PtrToStringUni(p));
            }
            catch (Exception ex) { Log.Warn("PCGW: SHGetKnownFolderPath failed", ex); return null; }
            finally { if (p != IntPtr.Zero) Marshal.FreeCoTaskMem(p); }
        }
    }
}
