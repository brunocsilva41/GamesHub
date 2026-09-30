// IPolicyConfig is undocumented (used by the Windows Sound control panel); the GUIDs below are the
// Windows 10/11 ones. If Microsoft changes them, SetDefault fails with a logged error (listing still works).
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace GamesHub
{
    public static class AutomationAudio
    {
        private const int ERender = 0, EConsole = 0, EMultimedia = 1, ECommunications = 2;
        private const int DeviceStateActive = 1, StgmRead = 0;
        private static readonly Guid FriendlyNameFmt = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0");

        [StructLayout(LayoutKind.Sequential)]
        private struct PropertyKey { public Guid fmtid; public int pid; }

        [StructLayout(LayoutKind.Sequential)]
        private struct PropVariant { public ushort vt; public ushort r1, r2, r3; public IntPtr p; public IntPtr p2; }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
            [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
            [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
            [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
        }

        [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceCollection
        {
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int Item(int index, out IMMDevice device);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
            [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore store);
            [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
            [PreserveSig] int GetState(out int state);
        }

        [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyStore
        {
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int GetAt(int index, out PropertyKey key);
            [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
            [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
            [PreserveSig] int Commit();
        }

        // Only SetDefaultEndpoint is used; the preceding methods just keep the vtable layout.
        [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPolicyConfig
        {
            [PreserveSig] int GetMixFormat(IntPtr id, IntPtr format);
            [PreserveSig] int GetDeviceFormat(IntPtr id, int def, IntPtr format);
            [PreserveSig] int ResetDeviceFormat(IntPtr id);
            [PreserveSig] int SetDeviceFormat(IntPtr id, IntPtr endpointFormat, IntPtr mixFormat);
            [PreserveSig] int GetProcessingPeriod(IntPtr id, int def, IntPtr defPeriod, IntPtr minPeriod);
            [PreserveSig] int SetProcessingPeriod(IntPtr id, IntPtr period);
            [PreserveSig] int GetShareMode(IntPtr id, IntPtr mode);
            [PreserveSig] int SetShareMode(IntPtr id, IntPtr mode);
            [PreserveSig] int GetPropertyValue(IntPtr id, int fxStore, IntPtr key, IntPtr value);
            [PreserveSig] int SetPropertyValue(IntPtr id, int fxStore, IntPtr key, IntPtr value);
            [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
            [PreserveSig] int SetEndpointVisibility(IntPtr id, int visible);
        }

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] private class MMDeviceEnumeratorCom { }
        [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")] private class PolicyConfigClientCom { }

        [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant pv);

        private static void Check(int hr, string what)
        {
            if (hr < 0) throw new COMException(what + " failed", hr);
        }

        private static void Release(object o)
        {
            if (o != null && Marshal.IsComObject(o)) Marshal.ReleaseComObject(o);
        }

        /// <summary>Endpoint id of the default playback device (console role), or "" when none.</summary>
        public static string GetDefaultId()
        {
            var en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            IMMDevice dev = null;
            try
            {
                int hr = en.GetDefaultAudioEndpoint(ERender, EConsole, out dev);
                if (hr < 0 || dev == null) return "";
                Check(dev.GetId(out string id), "IMMDevice.GetId");
                return id ?? "";
            }
            finally { Release(dev); Release(en); }
        }

        public static List<NamedOption> List()
        {
            string current = "";
            try { current = GetDefaultId(); }
            catch (Exception ex) when (ExpectedErrors.IsOsCall(ex)) { Log.Warn("Automation: could not read default audio device", ex); }
            var r = new List<NamedOption>();
            var en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            IMMDeviceCollection col = null;
            try
            {
                Check(en.EnumAudioEndpoints(ERender, DeviceStateActive, out col), "EnumAudioEndpoints");
                Check(col.GetCount(out int n), "IMMDeviceCollection.GetCount");
                for (int i = 0; i < n; i++)
                {
                    IMMDevice dev = null;
                    try
                    {
                        Check(col.Item(i, out dev), "IMMDeviceCollection.Item");
                        Check(dev.GetId(out string id), "IMMDevice.GetId");
                        r.Add(new NamedOption { Id = id, Name = FriendlyName(dev, id), Current = id == current });
                    }
                    catch (Exception ex) when (ExpectedErrors.IsOsCall(ex)) { Log.Warn("Automation: skipping audio device #" + i, ex); }
                    finally { Release(dev); }
                }
            }
            finally { Release(col); Release(en); }
            return r;
        }

        private static string FriendlyName(IMMDevice dev, string fallback)
        {
            IPropertyStore store = null;
            try
            {
                Check(dev.OpenPropertyStore(StgmRead, out store), "OpenPropertyStore");
                var key = new PropertyKey { fmtid = FriendlyNameFmt, pid = 14 };
                Check(store.GetValue(ref key, out PropVariant pv), "IPropertyStore.GetValue");
                try
                {
                    const ushort VtLpwstr = 31;
                    string s = pv.vt == VtLpwstr && pv.p != IntPtr.Zero ? Marshal.PtrToStringUni(pv.p) : null;
                    return string.IsNullOrWhiteSpace(s) ? fallback : s;
                }
                finally { PropVariantClear(ref pv); }
            }
            finally { Release(store); }
        }

        /// <summary>Makes the device the default for console, multimedia and communications roles.</summary>
        public static void SetDefault(string deviceId)
        {
            var en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            IMMDevice dev = null;
            try
            {
                Check(en.GetDevice(deviceId, out dev), "GetDevice(" + deviceId + ")");
                Check(dev.GetState(out int state), "IMMDevice.GetState");
                if (state != DeviceStateActive) throw new InvalidOperationException("audio device is not active: " + deviceId);
            }
            finally { Release(dev); Release(en); }

            var pc = (IPolicyConfig)new PolicyConfigClientCom();
            try
            {
                foreach (int role in new[] { EConsole, EMultimedia, ECommunications })
                    Check(pc.SetDefaultEndpoint(deviceId, role), "IPolicyConfig.SetDefaultEndpoint(role " + role + ")");
            }
            finally { Release(pc); }
        }
    }
}
