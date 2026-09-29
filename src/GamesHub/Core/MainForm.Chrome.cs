using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace GamesHub
{
    /// <summary>Window-message handling for the frameless window: resize band, maximize bounds, DPI.</summary>
    internal sealed partial class MainForm
    {
        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case CoreNative.WM_NCHITTEST:
                    base.WndProc(ref m);
                    // The WebView covers everything except the Padding band, so only the band reaches us.
                    if (m.Result.ToInt64() == CoreNative.HTCLIENT && WindowState == FormWindowState.Normal && !_fullscreen)
                    {
                        long lp = m.LParam.ToInt64();
                        var screen = new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF));
                        m.Result = (IntPtr)WindowPlacement.HitTestBorder(PointToClient(screen), ClientSize, Padding.Left);
                    }
                    return;

                case CoreNative.WM_GETMINMAXINFO:
                    base.WndProc(ref m);
                    ApplyMinMaxInfo(m.LParam);
                    return;

                case CoreNative.WM_DPICHANGED:
                    var r = Marshal.PtrToStructure<CoreNative.RECT>(m.LParam);
                    CoreNative.SetWindowPos(Handle, IntPtr.Zero, r.Left, r.Top, r.Width, r.Height,
                        CoreNative.SWP_NOZORDER | CoreNative.SWP_NOACTIVATE);
                    UpdateFrame();
                    m.Result = IntPtr.Zero;
                    return;
            }
            base.WndProc(ref m);
        }

        /// <summary>A borderless window would maximize over the taskbar; constrain it to the monitor's work area.</summary>
        private void ApplyMinMaxInfo(IntPtr lParam)
        {
            var mmi = Marshal.PtrToStructure<CoreNative.MINMAXINFO>(lParam);
            IntPtr monitor = CoreNative.MonitorFromWindow(Handle, CoreNative.MONITOR_DEFAULTTONEAREST);
            var info = new CoreNative.MONITORINFO { cbSize = Marshal.SizeOf<CoreNative.MONITORINFO>() };
            if (monitor != IntPtr.Zero && CoreNative.GetMonitorInfo(monitor, ref info))
            {
                CoreNative.RECT work = info.rcWork, mon = info.rcMonitor;
                mmi.ptMaxPosition.X = work.Left - mon.Left;
                mmi.ptMaxPosition.Y = work.Top - mon.Top;
                mmi.ptMaxSize.X = work.Width;
                mmi.ptMaxSize.Y = work.Height;
            }
            mmi.ptMinTrackSize.X = Px(MinLogicalSize.Width);
            mmi.ptMinTrackSize.Y = Px(MinLogicalSize.Height);
            Marshal.StructureToPtr(mmi, lParam, false);
        }
    }
}
