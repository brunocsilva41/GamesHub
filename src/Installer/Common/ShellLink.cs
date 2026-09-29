// OWNER: DIST agent. .lnk read/write via IShellLinkW + IPropertyStore (AppUserModelID), no WSH dependency.
using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace GamesHub.Installer
{
    internal static class ShellLink
    {
        public sealed class Spec
        {
            public string Target;
            public string Arguments = "";
            public string WorkingDir = "";
            public string IconPath;
            public int IconIndex;
            public string Description = "";
            public string AppUserModelId;
        }

        public static void Save(string lnkPath, Spec s)
        {
            IShellLinkW link = (IShellLinkW)new CShellLink();
            try
            {
                link.SetPath(s.Target);
                link.SetArguments(s.Arguments ?? "");
                link.SetWorkingDirectory(s.WorkingDir ?? "");
                link.SetDescription(s.Description ?? "");
                if (!string.IsNullOrEmpty(s.IconPath)) link.SetIconLocation(s.IconPath, s.IconIndex);
                if (!string.IsNullOrEmpty(s.AppUserModelId)) SetAppId(link, s.AppUserModelId);
                ((IPersistFile)link).Save(lnkPath, true);
            }
            finally { Marshal.FinalReleaseComObject(link); }
        }

        /// <summary>Target path of a .lnk, or null if unreadable.</summary>
        public static string GetTarget(string lnkPath)
        {
            IShellLinkW link = null;
            try
            {
                link = (IShellLinkW)new CShellLink();
                ((IPersistFile)link).Load(lnkPath, 0);
                var sb = new StringBuilder(1024);
                link.GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
                return sb.ToString();
            }
            catch (Exception ex)
            {
                InstallerLog.Warn("Could not read shortcut " + lnkPath, ex);
                return null;
            }
            finally { if (link != null) Marshal.FinalReleaseComObject(link); }
        }

        /// <summary>Re-targets an existing .lnk (keeps its name/location/pin), updating icon, args and AppUserModelID.</summary>
        public static void Retarget(string lnkPath, Spec s)
        {
            IShellLinkW link = (IShellLinkW)new CShellLink();
            try
            {
                ((IPersistFile)link).Load(lnkPath, 2 /* STGM_READWRITE */);
                link.SetPath(s.Target);
                link.SetWorkingDirectory(s.WorkingDir ?? "");
                if (s.Arguments != null) link.SetArguments(s.Arguments);
                if (!string.IsNullOrEmpty(s.IconPath)) link.SetIconLocation(s.IconPath, s.IconIndex);
                if (!string.IsNullOrEmpty(s.AppUserModelId)) SetAppId(link, s.AppUserModelId);
                ((IPersistFile)link).Save(lnkPath, true);
            }
            finally { Marshal.FinalReleaseComObject(link); }
        }

        private static void SetAppId(IShellLinkW link, string appId)
        {
            var store = (IPropertyStore)link;
            PropertyKey key = PKEY_AppUserModel_ID;
            var pv = new PropVariant { vt = 31 /* VT_LPWSTR */, p = Marshal.StringToCoTaskMemUni(appId) };
            try
            {
                store.SetValue(ref key, ref pv);
                store.Commit();
            }
            finally { Marshal.FreeCoTaskMem(pv.p); }
        }

        private static readonly PropertyKey PKEY_AppUserModel_ID =
            new PropertyKey { fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), pid = 5 };

        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        private class CShellLink { }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct PropertyKey { public Guid fmtid; public uint pid; }

        // PROPVARIANT is 16 bytes (x86) / 24 bytes (x64); a larger buffer is harmless for SetValue.
        [StructLayout(LayoutKind.Explicit, Size = 24)]
        private struct PropVariant
        {
            [FieldOffset(0)] public ushort vt;
            [FieldOffset(8)] public IntPtr p;
        }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
        private interface IPropertyStore
        {
            void GetCount(out uint cProps);
            void GetAt(uint iProp, out PropertyKey pkey);
            void GetValue(ref PropertyKey key, out PropVariant pv);
            void SetValue(ref PropertyKey key, ref PropVariant pv);
            void Commit();
        }

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
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
            void Resolve(IntPtr hwnd, uint fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }
    }
}
