using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace GamesHub
{
    public static class AutomationPower
    {
        private const uint AccessScheme = 16, ErrorNoMoreItems = 259;

        [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr guid);
        [DllImport("powrprof.dll")] private static extern uint PowerSetActiveScheme(IntPtr root, ref Guid guid);
        [DllImport("powrprof.dll")] private static extern uint PowerEnumerate(IntPtr root, IntPtr scheme, IntPtr sub, uint access, uint index, IntPtr buffer, ref uint size);
        [DllImport("powrprof.dll")] private static extern uint PowerReadFriendlyName(IntPtr root, ref Guid scheme, IntPtr sub, IntPtr setting, IntPtr buffer, ref uint size);
        [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr mem);

        public static Guid GetActive()
        {
            uint rc = PowerGetActiveScheme(IntPtr.Zero, out IntPtr p);
            if (rc != 0) throw new InvalidOperationException("PowerGetActiveScheme failed: " + rc);
            try { return (Guid)Marshal.PtrToStructure(p, typeof(Guid)); }
            finally { LocalFree(p); }
        }

        public static void SetActive(Guid scheme)
        {
            if (!Enumerate().Contains(scheme)) throw new ArgumentException("power plan not found: " + scheme);
            uint rc = PowerSetActiveScheme(IntPtr.Zero, ref scheme);
            if (rc != 0) throw new InvalidOperationException("PowerSetActiveScheme failed: " + rc);
        }

        public static List<Guid> Enumerate()
        {
            var r = new List<Guid>();
            IntPtr buf = Marshal.AllocHGlobal(16);
            try
            {
                for (uint i = 0; i < 64; i++)
                {
                    uint size = 16;
                    uint rc = PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, AccessScheme, i, buf, ref size);
                    if (rc == ErrorNoMoreItems) break;
                    if (rc != 0) throw new InvalidOperationException("PowerEnumerate failed: " + rc);
                    r.Add((Guid)Marshal.PtrToStructure(buf, typeof(Guid)));
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
            return r;
        }

        public static string FriendlyName(Guid scheme)
        {
            uint size = 0;
            PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ref size);
            if (size == 0) return scheme.ToString("D");
            IntPtr buf = Marshal.AllocHGlobal((int)size);
            try
            {
                uint rc = PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, buf, ref size);
                if (rc != 0) return scheme.ToString("D");
                string s = Marshal.PtrToStringUni(buf);
                return string.IsNullOrWhiteSpace(s) ? scheme.ToString("D") : s;
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        public static List<NamedOption> List()
        {
            var r = new List<NamedOption>();
            Guid active = Guid.Empty;
            try { active = GetActive(); }
            catch (Exception ex) when (ExpectedErrors.IsOsCall(ex)) { Log.Warn("Automation: could not read active power plan", ex); }
            foreach (Guid g in Enumerate())
                r.Add(new NamedOption { Id = g.ToString("D"), Name = FriendlyName(g), Current = g == active });
            return r;
        }
    }
}
