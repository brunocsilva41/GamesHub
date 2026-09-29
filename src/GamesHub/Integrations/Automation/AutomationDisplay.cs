// Changes are dynamic only (flags 0 = not written to the registry), so a reboot always restores the
// user's configured mode; the service restores the snapshot explicitly after the game exits.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace GamesHub
{
    public static class AutomationDisplay
    {
        private const int EnumCurrentSettings = -1;
        private const int DmPelsWidth = 0x80000, DmPelsHeight = 0x100000, DmDisplayFrequency = 0x400000;
        private const int DmInterlaced = 0x2;
        private const int CdsTest = 0x2, DispChangeSuccessful = 0;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
            public int dmFields;
            public int dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplaySettings(string device, int mode, ref DEVMODE dm);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int ChangeDisplaySettingsEx(string device, ref DEVMODE dm, IntPtr hwnd, int flags, IntPtr param);

        private static DEVMODE NewMode() => new DEVMODE { dmSize = (short)Marshal.SizeOf(typeof(DEVMODE)) };

        public static ResolutionSpec Current()
        {
            DEVMODE dm = NewMode();
            if (!EnumDisplaySettings(null, EnumCurrentSettings, ref dm))
                throw new InvalidOperationException("EnumDisplaySettings(current) failed");
            return new ResolutionSpec(dm.dmPelsWidth, dm.dmPelsHeight, dm.dmDisplayFrequency);
        }

        /// <summary>Distinct non-interlaced modes (≥ 32 bpp when available), sorted descending.</summary>
        public static List<ResolutionSpec> Modes()
        {
            var all = new List<KeyValuePair<ResolutionSpec, int>>();
            int maxBpp = 0;
            for (int i = 0; i < 4096; i++)
            {
                DEVMODE dm = NewMode();
                if (!EnumDisplaySettings(null, i, ref dm)) break;
                if ((dm.dmDisplayFlags & DmInterlaced) != 0 || dm.dmPelsWidth <= 0 || dm.dmPelsHeight <= 0) continue;
                all.Add(new KeyValuePair<ResolutionSpec, int>(new ResolutionSpec(dm.dmPelsWidth, dm.dmPelsHeight, dm.dmDisplayFrequency), dm.dmBitsPerPel));
                maxBpp = Math.Max(maxBpp, dm.dmBitsPerPel);
            }
            int minBpp = Math.Min(32, maxBpp);
            var modes = new List<ResolutionSpec>();
            foreach (var kv in all) if (kv.Value >= minBpp) modes.Add(kv.Key);
            return ResolutionSpec.SortDistinct(modes);
        }

        /// <summary>Tests the mode first (CDS_TEST) and only then applies it dynamically.</summary>
        public static void Apply(ResolutionSpec spec)
        {
            DEVMODE dm = NewMode();
            if (!EnumDisplaySettings(null, EnumCurrentSettings, ref dm))
                throw new InvalidOperationException("EnumDisplaySettings(current) failed");
            if (spec.Matches(new ResolutionSpec(dm.dmPelsWidth, dm.dmPelsHeight, dm.dmDisplayFrequency))) return;
            dm.dmPelsWidth = spec.Width;
            dm.dmPelsHeight = spec.Height;
            dm.dmFields = DmPelsWidth | DmPelsHeight;
            if (spec.Hz > 0) { dm.dmDisplayFrequency = spec.Hz; dm.dmFields |= DmDisplayFrequency; }
            int rc = ChangeDisplaySettingsEx(null, ref dm, IntPtr.Zero, CdsTest, IntPtr.Zero);
            if (rc != DispChangeSuccessful) throw new InvalidOperationException("display mode " + spec + " rejected by CDS_TEST: " + rc);
            rc = ChangeDisplaySettingsEx(null, ref dm, IntPtr.Zero, 0, IntPtr.Zero);
            if (rc != DispChangeSuccessful) throw new InvalidOperationException("ChangeDisplaySettingsEx(" + spec + ") failed: " + rc);
        }

        public static List<NamedOption> List()
        {
            ResolutionSpec cur = default(ResolutionSpec);
            try { cur = Current(); }
            catch (Exception ex) { Log.Warn("Automation: could not read current display mode", ex); }
            var r = new List<NamedOption>();
            foreach (ResolutionSpec m in Modes())
                r.Add(new NamedOption { Id = m.ToString(), Name = m.DisplayName, Current = m.Equals(cur) });
            return r;
        }
    }
}
