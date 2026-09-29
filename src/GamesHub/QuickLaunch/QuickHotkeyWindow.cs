// OWNER: QUICK agent. Message-only window that owns the palette's global hotkey registration.
using System;
using System.ComponentModel;
using System.Windows.Forms;

namespace GamesHub
{
    internal sealed class QuickHotkeyWindow : NativeWindow, IDisposable
    {
        /// <summary>Distinct from CORE's main-window hotkey id (0x4748).</summary>
        private const int HotkeyId = 0x4751; // "GQ"
        private static readonly IntPtr HwndMessage = new IntPtr(-3);

        public QuickHotkey Current { get; private set; }
        public event Action Pressed;

        public QuickHotkeyWindow()
        {
            CreateHandle(new CreateParams { Caption = "GamesHub.QuickLaunch.Hotkey", Parent = HwndMessage });
        }

        /// <summary>Registers the hotkey (replacing the previous one). On failure the previous one is restored.</summary>
        public bool Register(QuickHotkey hk)
        {
            QuickHotkey previous = Current;
            Unregister();
            if (QuickNative.RegisterHotKey(Handle, HotkeyId, hk.Modifiers | QuickNative.MOD_NOREPEAT, hk.VirtualKey))
            {
                Current = hk;
                Log.Info("Quick-launch hotkey registered: " + hk.Display);
                return true;
            }
            Log.Warn("Quick-launch RegisterHotKey failed for " + hk.Display + ": " + new Win32Exception().Message);
            if (previous != null && QuickNative.RegisterHotKey(Handle, HotkeyId, previous.Modifiers | QuickNative.MOD_NOREPEAT, previous.VirtualKey))
                Current = previous;
            return false;
        }

        public void Unregister()
        {
            if (Current == null) return;
            if (!QuickNative.UnregisterHotKey(Handle, HotkeyId))
                Log.Warn("Quick-launch UnregisterHotKey failed: " + new Win32Exception().Message);
            Current = null;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == QuickNative.WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
            {
                try { Pressed?.Invoke(); }
                catch (Exception ex) { Log.Error("Quick-launch hotkey handler failed", ex); }
                return;
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            Unregister();
            if (Handle != IntPtr.Zero) DestroyHandle();
        }
    }
}
