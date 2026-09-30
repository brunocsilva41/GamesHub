using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace GamesHub
{
    /// <summary>What a shortcut (.lnk or .url) points to.</summary>
    internal sealed class ShortcutInfo
    {
        public string Target = "";       // .lnk: target path; .url: URL
        public string Arguments = "";
        public string WorkingDir = "";
        public string IconPath = "";     // expanded, without ",index"
        public int IconIndex;

        /// <summary>Text used for platform / app id detection.</summary>
        public string DetectionText => (Target + " " + Arguments).Trim();
    }

    internal static class LibShellLink
    {
        private const int MaxChars = 4096;
        private const int StgmRead = 0;

        public static ShortcutInfo ReadLnk(string path)
        {
            IShellLinkW link = (IShellLinkW)new CShellLink();
            try
            {
                ((IPersistFile)link).Load(path, StgmRead);
                var sb = new StringBuilder(MaxChars);
                var info = new ShortcutInfo();
                link.GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
                info.Target = sb.ToString();
                sb.Clear();
                link.GetArguments(sb, sb.Capacity);
                info.Arguments = sb.ToString();
                sb.Clear();
                link.GetWorkingDirectory(sb, sb.Capacity);
                info.WorkingDir = Environment.ExpandEnvironmentVariables(sb.ToString());
                sb.Clear();
                link.GetIconLocation(sb, sb.Capacity, out int iconIndex);
                info.IconPath = Environment.ExpandEnvironmentVariables(sb.ToString());
                info.IconIndex = iconIndex;
                return info;
            }
            finally { Marshal.FinalReleaseComObject(link); }
        }

        public static void WriteLnk(string path, ShortcutInfo info)
        {
            IShellLinkW link = (IShellLinkW)new CShellLink();
            try
            {
                link.SetPath(info.Target);
                if (!string.IsNullOrEmpty(info.Arguments)) link.SetArguments(info.Arguments);
                if (!string.IsNullOrEmpty(info.WorkingDir)) link.SetWorkingDirectory(info.WorkingDir);
                if (!string.IsNullOrEmpty(info.IconPath)) link.SetIconLocation(info.IconPath, info.IconIndex);
                ((IPersistFile)link).Save(path, true);
            }
            finally { Marshal.FinalReleaseComObject(link); }
        }

        /// <summary>Parses the [InternetShortcut] section of a .url file (URL=, IconFile=, IconIndex=).</summary>
        public static ShortcutInfo ParseUrlFile(string content)
        {
            var info = new ShortcutInfo();
            bool inSection = false, sawHeader = false;
            foreach (string line in (content ?? "").Split('\n').Select(raw => raw.Trim()))
            {
                if (line.StartsWith("["))
                {
                    inSection = line.Equals("[InternetShortcut]", StringComparison.OrdinalIgnoreCase);
                    sawHeader = true;
                    continue;
                }
                if (sawHeader && !inSection) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim(), val = line.Substring(eq + 1).Trim();
                if (key.Equals("URL", StringComparison.OrdinalIgnoreCase)) info.Target = val;
                else if (key.Equals("IconFile", StringComparison.OrdinalIgnoreCase)) info.IconPath = Environment.ExpandEnvironmentVariables(val);
                else if (key.Equals("IconIndex", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out info.IconIndex);
            }
            return info;
        }

        public static string BuildUrlFile(string url, string iconFile = "")
        {
            var sb = new StringBuilder("[InternetShortcut]\r\nURL=").Append(url).Append("\r\n");
            if (!string.IsNullOrEmpty(iconFile)) sb.Append("IconIndex=0\r\nIconFile=").Append(iconFile).Append("\r\n");
            return sb.ToString();
        }

        // ------------------------------------------------------------------ COM interop

        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        private class CShellLink { }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
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
