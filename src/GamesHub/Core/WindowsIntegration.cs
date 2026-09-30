using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace GamesHub
{
    public sealed class Hotkey
    {
        public uint Modifiers;
        public uint VirtualKey;
        /// <summary>Normalized text, e.g. "Ctrl+Alt+G".</summary>
        public string Display = "";
    }

    /// <summary>Parses hotkey strings like "Ctrl+Alt+G", "Win+Shift+F5" (pure, testable).</summary>
    public static class HotkeyParser
    {
        public const uint ModAlt = 0x1, ModControl = 0x2, ModShift = 0x4, ModWin = 0x8;

        private static readonly Dictionary<string, uint> Named = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
        {
            ["Space"] = 0x20, ["PageUp"] = 0x21, ["PageDown"] = 0x22, ["End"] = 0x23, ["Home"] = 0x24,
            ["Left"] = 0x25, ["Up"] = 0x26, ["Right"] = 0x27, ["Down"] = 0x28,
            ["Insert"] = 0x2D, ["Delete"] = 0x2E, ["Pause"] = 0x13,
        };

        public static bool TryParse(string text, out Hotkey hotkey)
        {
            hotkey = null;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string[] parts = text.Split('+');
            uint mods = 0;
            string keyName = null;
            uint vk = 0;
            foreach (string p in parts.Select(raw => raw.Trim()))
            {
                if (p.Length == 0) return false;
                uint mod = ModifierOf(p);
                if (mod != 0)
                {
                    if ((mods & mod) != 0) return false; // duplicated modifier
                    mods |= mod;
                    continue;
                }
                if (keyName != null) return false;   // two non-modifier keys
                if (!TryKey(p, out vk, out keyName)) return false;
            }
            if (keyName == null) return false;
            bool isFunctionKey = vk >= 0x70 && vk <= 0x87;
            if (mods == 0 && !isFunctionKey) return false; // a bare letter would swallow normal typing

            var display = new List<string>();
            if ((mods & ModControl) != 0) display.Add("Ctrl");
            if ((mods & ModAlt) != 0) display.Add("Alt");
            if ((mods & ModShift) != 0) display.Add("Shift");
            if ((mods & ModWin) != 0) display.Add("Win");
            display.Add(keyName);
            hotkey = new Hotkey { Modifiers = mods, VirtualKey = vk, Display = string.Join("+", display) };
            return true;
        }

        private static uint ModifierOf(string p)
        {
            switch (p.ToLowerInvariant())
            {
                case "ctrl": case "control": case "ctl": return ModControl;
                case "alt": return ModAlt;
                case "shift": return ModShift;
                case "win": case "windows": case "meta": case "super": case "cmd": return ModWin;
                default: return 0;
            }
        }

        private static bool TryKey(string p, out uint vk, out string name)
        {
            vk = 0;
            name = null;
            if (p.Length == 1)
            {
                char c = char.ToUpperInvariant(p[0]);
                if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))
                {
                    vk = c;
                    name = c.ToString();
                    return true;
                }
                return false;
            }
            if ((p[0] == 'F' || p[0] == 'f') && int.TryParse(p.Substring(1), out int n) && n >= 1 && n <= 24)
            {
                vk = (uint)(0x70 + n - 1);
                name = "F" + n;
                return true;
            }
            foreach (KeyValuePair<string, uint> kv in Named)
            {
                if (string.Equals(kv.Key, p, StringComparison.OrdinalIgnoreCase))
                {
                    vk = kv.Value;
                    name = kv.Key;
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>System-wide hotkey registered on a message-only window.</summary>
    internal sealed class GlobalHotkey : NativeWindow, IDisposable
    {
        private const int HotkeyId = 0x4748; // "GH"
        private static readonly IntPtr HwndMessage = new IntPtr(-3);
        private bool _registered;

        public event Action Pressed;

        public GlobalHotkey()
        {
            CreateHandle(new CreateParams { Caption = "GamesHub.Hotkey", Parent = HwndMessage });
        }

        /// <summary>Registers the hotkey (replacing any previous one). Returns false if it is taken.</summary>
        public bool Register(Hotkey hk)
        {
            Unregister();
            if (!CoreNative.RegisterHotKey(Handle, HotkeyId, hk.Modifiers | CoreNative.MOD_NOREPEAT, hk.VirtualKey))
            {
                Log.Warn("RegisterHotKey failed for " + hk.Display + ": " + new Win32Exception().Message);
                return false;
            }
            _registered = true;
            Log.Info("Global hotkey registered: " + hk.Display);
            return true;
        }

        public void Unregister()
        {
            if (!_registered) return;
            if (!CoreNative.UnregisterHotKey(Handle, HotkeyId))
                Log.Warn("UnregisterHotKey failed: " + new Win32Exception().Message);
            _registered = false;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == CoreNative.WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
            {
                Pressed?.Invoke();
                return;
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            Unregister();
            DestroyHandle();
        }
    }

    /// <summary>"Start with Windows" through HKCU\...\Run (no admin rights needed).</summary>
    public static class Autostart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = AppInfo.Name;

        public static string BuildCommand(string exePath, bool minimized)
            => "\"" + exePath + "\"" + (minimized ? " --minimized" : "");

        /// <summary>Creates/updates or removes the Run value. Only writes when something differs.</summary>
        public static void Apply(bool enabled, bool minimized)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (key == null) throw new InvalidOperationException("Run key unavailable");
                string current = key.GetValue(ValueName) as string;
                if (enabled)
                {
                    string desired = BuildCommand(Application.ExecutablePath, minimized);
                    if (!string.Equals(current, desired, StringComparison.OrdinalIgnoreCase))
                    {
                        key.SetValue(ValueName, desired, RegistryValueKind.String);
                        Log.Info("Autostart enabled: " + desired);
                    }
                }
                else if (current != null)
                {
                    key.DeleteValue(ValueName, false);
                    Log.Info("Autostart disabled");
                }
            }
        }
    }

    /// <summary>Opening things through the Windows shell.</summary>
    public static class ShellActions
    {
        /// <summary>True for absolute https URLs (the only scheme we hand to the shell).</summary>
        public static bool IsSafeExternalUrl(string url)
            => Uri.TryCreate(url, UriKind.Absolute, out Uri u) && u.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(u.UserInfo);

        public static bool OpenUrl(string url)
        {
            if (!IsSafeExternalUrl(url))
            {
                Log.Warn("Refused to open non-https URL: " + Truncate(url));
                return false;
            }
            return Start(new ProcessStartInfo(new Uri(url).AbsoluteUri) { UseShellExecute = true });
        }

        public static bool OpenFolder(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return false;
            return Start(new ProcessStartInfo("explorer.exe", "\"" + dir + "\"") { UseShellExecute = true });
        }

        /// <summary>Opens Explorer with the file selected, or the folder itself.</summary>
        public static bool Reveal(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (File.Exists(path))
                return Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true });
            return OpenFolder(path);
        }

        private static bool Start(ProcessStartInfo psi)
        {
            try
            {
                using (Process.Start(psi)) { }
                return true;
            }
            catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException || ex is IOException)
            {
                Log.Warn("Shell start failed: " + psi.FileName + " " + psi.Arguments, ex);
                return false;
            }
        }

        internal static string Truncate(string s, int max = 200)
            => s == null ? "" : s.Length <= max ? s : s.Substring(0, max) + "…";
    }
}
