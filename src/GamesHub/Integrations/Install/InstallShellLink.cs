using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace GamesHub
{
    /// <summary>Reads shortcut targets without resolving/searching (never modifies the shortcut).</summary>
    public static class InstallShellLink
    {
        private const int SLGP_RAWPATH = 0x4;
        private const int STGM_READ = 0x0;

        /// <summary>Target path of a .lnk with environment variables expanded. "" when the link has no
        /// file-system target (shell items, URLs) or cannot be read.</summary>
        public static string ReadLnkTarget(string lnkPath)
        {
            IShellLinkW link = null;
            try
            {
                link = (IShellLinkW)new CShellLink();
                ((IPersistFile)link).Load(lnkPath, STGM_READ);
                var sb = new StringBuilder(1024);
                link.GetPath(sb, sb.Capacity, IntPtr.Zero, SLGP_RAWPATH);
                string raw = sb.ToString();
                if (raw.Length == 0)
                {
                    sb.Clear();
                    link.GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
                    raw = sb.ToString();
                }
                return Environment.ExpandEnvironmentVariables(raw.Trim().Trim('"'));
            }
            catch (Exception ex) when (ExpectedErrors.IsInterop(ex) || ExpectedErrors.IsFileSystem(ex))
            {
                Log.Warn("InstallShellLink: cannot read " + lnkPath, ex);
                return "";
            }
            finally
            {
                if (link != null) Marshal.ReleaseComObject(link);
            }
        }

        /// <summary>URL= value of an Internet Shortcut (.url) file, "" when missing.</summary>
        public static string ReadUrl(string urlFile)
        {
            try
            {
                bool inSection = false;
                foreach (string t in File.ReadAllLines(urlFile).Select(line => line.Trim()))
                {
                    if (t.StartsWith("[")) { inSection = t.Equals("[InternetShortcut]", StringComparison.OrdinalIgnoreCase); continue; }
                    if (inSection && t.StartsWith("URL=", StringComparison.OrdinalIgnoreCase)) return t.Substring(4).Trim();
                }
            }
            catch (Exception ex) when (ExpectedErrors.IsFileSystem(ex))
            {
                Log.Warn("InstallShellLink: cannot read " + urlFile, ex);
            }
            return "";
        }

        /// <summary>Local path of a file: URL ("" when the URL is not file:).</summary>
        public static string FileUrlToPath(string url)
        {
            if (string.IsNullOrEmpty(url) || !url.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) return "";
            try { return new Uri(url).LocalPath; }
            catch (Exception ex) when (ex is UriFormatException || ex is InvalidOperationException)
            {
                Log.Warn("InstallShellLink: bad file URL " + url, ex);
                return "";
            }
        }

        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        private class CShellLink { }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, int fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
            void Resolve(IntPtr hwnd, int fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }
    }
}
