// Canonical format: modifiers in the order Ctrl+Alt+Shift+Win, then one key, joined by "+",
// e.g. "Ctrl+Shift+Space", "Alt+F5", "Win+Num3". web/quick/hotkey-input.js emits the same format.
using System;
using System.Collections.Generic;

namespace GamesHub
{
    public sealed class QuickHotkey
    {
        public const uint ModAlt = 0x1, ModControl = 0x2, ModShift = 0x4, ModWin = 0x8;

        public uint Modifiers;
        public uint VirtualKey;
        /// <summary>Canonical key name ("A", "7", "F5", "Space", "Up", "Num3"...).</summary>
        public string KeyName = "";
        /// <summary>Canonical display string, e.g. "Ctrl+Shift+Space".</summary>
        public string Display = "";

        public override string ToString() => Display;

        // Canonical name -> virtual-key code.
        private static readonly Dictionary<string, uint> Named = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
        {
            ["Space"] = 0x20, ["Enter"] = 0x0D, ["Tab"] = 0x09,
            ["PageUp"] = 0x21, ["PageDown"] = 0x22, ["End"] = 0x23, ["Home"] = 0x24,
            ["Left"] = 0x25, ["Up"] = 0x26, ["Right"] = 0x27, ["Down"] = 0x28,
            ["PrintScreen"] = 0x2C, ["Insert"] = 0x2D, ["Delete"] = 0x2E, ["Pause"] = 0x13,
        };

        // Accepted aliases -> canonical name.
        private static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Espaço"] = "Space", ["Espaco"] = "Space", ["Spacebar"] = "Space", [" "] = "Space",
            ["Return"] = "Enter", ["PgUp"] = "PageUp", ["PgDn"] = "PageDown", ["PageDn"] = "PageDown",
            ["ArrowLeft"] = "Left", ["ArrowUp"] = "Up", ["ArrowRight"] = "Right", ["ArrowDown"] = "Down",
            ["Ins"] = "Insert", ["Del"] = "Delete", ["PrtSc"] = "PrintScreen", ["PrintScrn"] = "PrintScreen",
            ["Break"] = "Pause",
        };

        /// <summary>Parses "Ctrl+Shift+Space"-style text (case/spacing-insensitive, aliases accepted).
        /// Requires at least one modifier unless the key is F1–F24.</summary>
        public static bool TryParse(string text, out QuickHotkey hotkey)
        {
            hotkey = null;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string t = text.Trim();
            // "Ctrl++" style is not supported; a bare "Space" key typed as " " is handled via alias.
            string[] parts = t.Split('+');
            uint mods = 0, vk = 0;
            string keyName = null;
            foreach (string raw in parts)
            {
                string p = raw.Trim();
                if (p.Length == 0) return false;
                uint mod = ModifierOf(p);
                if (mod != 0)
                {
                    if ((mods & mod) != 0) return false;   // duplicated modifier
                    mods |= mod;
                    continue;
                }
                if (keyName != null) return false;         // two non-modifier keys
                if (!TryKey(p, out vk, out keyName)) return false;
            }
            if (keyName == null) return false;              // modifiers only
            if (mods == 0 && !IsFunctionKey(vk)) return false;
            hotkey = new QuickHotkey { Modifiers = mods, VirtualKey = vk, KeyName = keyName, Display = Format(mods, keyName) };
            return true;
        }

        /// <summary>Canonical form of a hotkey string, or null when invalid.</summary>
        public static string Normalize(string text) => TryParse(text, out QuickHotkey h) ? h.Display : null;

        public static string Format(uint mods, string keyName)
        {
            var parts = new List<string>(5);
            if ((mods & ModControl) != 0) parts.Add("Ctrl");
            if ((mods & ModAlt) != 0) parts.Add("Alt");
            if ((mods & ModShift) != 0) parts.Add("Shift");
            if ((mods & ModWin) != 0) parts.Add("Win");
            parts.Add(keyName);
            return string.Join("+", parts);
        }

        public static bool IsFunctionKey(uint vk) => vk >= 0x70 && vk <= 0x87;

        private static uint ModifierOf(string p)
        {
            switch (p.ToLowerInvariant())
            {
                case "ctrl": case "control": case "ctl": case "strg": return ModControl;
                case "alt": case "option": return ModAlt;
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
            if ((p[0] == 'F' || p[0] == 'f') && p.Length <= 3 && int.TryParse(p.Substring(1), out int n) && n >= 1 && n <= 24
                && p[1] != '0')
            {
                vk = (uint)(0x70 + n - 1);
                name = "F" + n;
                return true;
            }
            string lower = p.ToLowerInvariant();
            foreach (string prefix in new[] { "numpad", "num" })
            {
                if (lower.StartsWith(prefix) && lower.Length == prefix.Length + 1 && char.IsDigit(lower[prefix.Length]))
                {
                    int d = lower[prefix.Length] - '0';
                    vk = (uint)(0x60 + d);
                    name = "Num" + d;
                    return true;
                }
            }
            if (Aliases.TryGetValue(p, out string canonical)) p = canonical;
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
}
