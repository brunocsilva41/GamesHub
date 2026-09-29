// OWNER: CORE agent.
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace GamesHub
{
    /// <summary>
    /// Taskbar Jump List with the most recent games as user tasks ("GamesHub.exe --launch &lt;id&gt;"),
    /// via ICustomDestinationList. Must be called on an STA thread (the UI thread).
    /// </summary>
    internal static class TaskbarJumpList
    {
        public static void Update(IList<Game> recent, string exePath)
        {
            var list = (ICustomDestinationList)new CDestinationList();
            try
            {
                list.SetAppID(AppInfo.AppUserModelId);
                Guid iidArray = typeof(IObjectArray).GUID;
                int hr = list.BeginList(out uint maxSlots, ref iidArray, out object removed);
                if (removed != null) Marshal.ReleaseComObject(removed);
                Marshal.ThrowExceptionForHR(hr);
                try
                {
                    var collection = (IObjectCollection)new CEnumerableObjectCollection();
                    int count = 0;
                    foreach (Game g in recent)
                    {
                        if (count >= Math.Max(1, (int)maxSlots)) break;
                        collection.AddObject(CreateLink(g, exePath));
                        count++;
                    }
                    if (count > 0) Marshal.ThrowExceptionForHR(list.AddUserTasks((IObjectArray)collection));
                    list.CommitList();
                    Marshal.ReleaseComObject(collection);
                }
                catch
                {
                    list.AbortList();
                    throw;
                }
            }
            finally
            {
                Marshal.ReleaseComObject(list);
            }
        }

        private static IShellLinkW CreateLink(Game g, string exePath)
        {
            var link = (IShellLinkW)new CShellLink();
            link.SetPath(exePath);
            link.SetArguments("--launch " + StartupArgs.Quote(g.Id));
            link.SetDescription(Clip(g.Name, 250));
            link.SetWorkingDirectory(Path.GetDirectoryName(exePath));
            string icon = !string.IsNullOrEmpty(g.Exe) && File.Exists(g.Exe) ? g.Exe : exePath;
            link.SetIconLocation(icon, 0);

            var store = (IPropertyStore)link;
            PropertyKey titleKey = PropertyKey.Title;
            var pv = new PropVariant { vt = 31 /* VT_LPWSTR */, pointer = Marshal.StringToCoTaskMemUni(Clip(g.Name, 250)) };
            try
            {
                store.SetValue(ref titleKey, ref pv);
                store.Commit();
            }
            finally
            {
                Marshal.FreeCoTaskMem(pv.pointer);
            }
            return link;
        }

        private static string Clip(string s, int max) => string.IsNullOrEmpty(s) ? "Jogo" : s.Length <= max ? s : s.Substring(0, max);

        // ------------------------------------------------------------------ COM interop

        [ComImport, Guid("77F10CF0-3DB5-4966-B520-B7C54FD35ED6")]
        private class CDestinationList { }

        [ComImport, Guid("2D3468C1-36A7-43B6-AC24-D3F02FD9607A")]
        private class CEnumerableObjectCollection { }

        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        private class CShellLink { }

        [ComImport, Guid("6332DEBF-87B5-4670-90C0-5E57B408A49E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ICustomDestinationList
        {
            void SetAppID([MarshalAs(UnmanagedType.LPWStr)] string pszAppID);
            [PreserveSig] int BeginList(out uint pcMinSlots, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);
            [PreserveSig] int AppendCategory([MarshalAs(UnmanagedType.LPWStr)] string pszCategory, IObjectArray poa);
            void AppendKnownCategory(int category);
            [PreserveSig] int AddUserTasks(IObjectArray poa);
            void CommitList();
            void GetRemovedDestinations(ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);
            void DeleteList([MarshalAs(UnmanagedType.LPWStr)] string pszAppID);
            void AbortList();
        }

        [ComImport, Guid("92CA9DCD-5622-4BBA-A805-5E9F541BD8C9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IObjectArray
        {
            void GetCount(out uint pcObjects);
            void GetAt(uint uiIndex, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);
        }

        [ComImport, Guid("5632B1A4-E38A-400A-928A-D4CD63230295"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IObjectCollection
        {
            // IObjectArray
            void GetCount(out uint pcObjects);
            void GetAt(uint uiIndex, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);
            // IObjectCollection
            void AddObject([MarshalAs(UnmanagedType.Interface)] object punk);
            void AddFromArray(IObjectArray poaSource);
            void RemoveObjectAt(uint uiIndex);
            void Clear();
        }

        [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
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

        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyStore
        {
            void GetCount(out uint cProps);
            void GetAt(uint iProp, out PropertyKey pkey);
            void GetValue(ref PropertyKey key, out PropVariant pv);
            void SetValue(ref PropertyKey key, ref PropVariant pv);
            void Commit();
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct PropertyKey
        {
            public Guid fmtid;
            public uint pid;
            public static PropertyKey Title => new PropertyKey { fmtid = new Guid("F29F85E0-4FF9-1068-AB91-08002B27B3D9"), pid = 2 };
        }

        /// <summary>Minimal PROPVARIANT holding a pointer value (16 bytes on x86, 24 on x64).</summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct PropVariant
        {
            public ushort vt;
            public ushort reserved1, reserved2, reserved3;
            public IntPtr pointer;
            public IntPtr padding;
        }
    }
}
