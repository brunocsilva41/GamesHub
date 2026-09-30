using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace GamesHub
{
    /// <summary>One "Uninstall" registry entry, as far as discovery cares.</summary>
    public sealed class RegistryApp
    {
        public string Name = "";
        public string InstallLocation = "";
        public string DisplayIcon = "";
        public string Publisher = "";
        /// <summary>EstimatedSize in bytes; -1 when absent.</summary>
        public long SizeBytes = -1;
    }

    /// <summary>Read-only scan of HKLM (64/32-bit views) and HKCU ...\CurrentVersion\Uninstall. Never throws.</summary>
    public static class DiscoveryRegistry
    {
        private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";

        public static List<RegistryApp> ReadAll()
        {
            var list = new List<RegistryApp>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (RegistryHive hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
            {
                foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                    ReadView(hive, view, list, seen);
            }
            return list;
        }

        private static void ReadView(RegistryHive hive, RegistryView view, List<RegistryApp> list, HashSet<string> seen)
        {
            try
            {
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view))
                using (RegistryKey root = baseKey.OpenSubKey(UninstallKey, false))
                {
                    if (root == null) return;
                    list.AddRange(root.GetSubKeyNames().Select(sub => ReadEntry(root, sub))
                        .Where(app => app != null && seen.Add(app.Name + "|" + app.InstallLocation + "|" + app.DisplayIcon)));
                }
            }
            catch (Exception ex) when (DiscoveryErrors.IsIo(ex)) { Log.Warn("Discovery: cannot open Uninstall key " + hive + "/" + view, ex); }
        }

        private static RegistryApp ReadEntry(RegistryKey root, string sub)
        {
            try
            {
                using (RegistryKey k = root.OpenSubKey(sub, false))
                {
                    if (k == null) return null;
                    string name = Str(k, "DisplayName");
                    // Updates/components of another product and hidden system components are not applications.
                    if (name.Length == 0 || Str(k, "SystemComponent") == "1" || Str(k, "ParentKeyName").Length > 0
                        || Str(k, "ReleaseType").Length > 0) return null;
                    long kb = k.GetValue("EstimatedSize") is int size && size > 0 ? size : -1;
                    return new RegistryApp
                    {
                        Name = name,
                        InstallLocation = Str(k, "InstallLocation"),
                        DisplayIcon = Str(k, "DisplayIcon"),
                        Publisher = Str(k, "Publisher"),
                        SizeBytes = kb > 0 ? kb * 1024 : -1,
                    };
                }
            }
            catch (Exception ex) when (DiscoveryErrors.IsIo(ex))
            {
                Log.Warn("Discovery: cannot read Uninstall\\" + sub, ex);
                return null;
            }
        }

        /// <summary>DisplayIcon "C:\Game\game.exe,0" / "\"C:\Game\game.exe\"" → the .exe path, "" when it is not an exe.</summary>
        public static string IconExe(string displayIcon)
        {
            if (string.IsNullOrWhiteSpace(displayIcon)) return "";
            string s = displayIcon.Trim();
            if (s.StartsWith("\"", StringComparison.Ordinal))
            {
                int end = s.IndexOf('"', 1);
                s = end > 0 ? s.Substring(1, end - 1) : s.Trim('"');
            }
            else
            {
                int comma = s.LastIndexOf(',');
                if (comma > 2) s = s.Substring(0, comma);
            }
            s = s.Trim();
            return s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && s.IndexOfAny(Path.GetInvalidPathChars()) < 0 ? s : "";
        }

        private static string Str(RegistryKey k, string name)
        {
            object v = k.GetValue(name);
            return v == null ? "" : Convert.ToString(v).Trim();
        }
    }
}
