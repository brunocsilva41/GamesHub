using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using Microsoft.Win32;

namespace GamesHub
{
    public static class UninstallRegistry
    {
        private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";
        private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);
        private static readonly object Gate = new object();
        private static List<UninstallEntry> _cached;
        private static DateTime _cachedAt;

        public static List<UninstallEntry> ReadAll()
        {
            lock (Gate)
            {
                if (_cached != null && DateTime.UtcNow - _cachedAt < CacheTtl) return _cached;
                var list = new List<UninstallEntry>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (RegistryHive hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
                {
                    foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                        ReadHive(hive, view, list, seen);
                }
                _cached = list;
                _cachedAt = DateTime.UtcNow;
                return list;
            }
        }

        private static void ReadHive(RegistryHive hive, RegistryView view, List<UninstallEntry> list, HashSet<string> seen)
        {
            string label = (hive == RegistryHive.CurrentUser ? "HKCU" : "HKLM") + (view == RegistryView.Registry64 ? "64" : "32");
            try
            {
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view))
                using (RegistryKey root = baseKey.OpenSubKey(UninstallKey, false))
                {
                    if (root == null) return;
                    foreach (string name in root.GetSubKeyNames())
                    {
                        try
                        {
                            using (RegistryKey k = root.OpenSubKey(name, false))
                            {
                                if (k == null) continue;
                                var e = new UninstallEntry
                                {
                                    KeyPath = label + "\\" + UninstallKey + "\\" + name,
                                    DisplayName = Str(k, "DisplayName"),
                                    InstallLocation = Str(k, "InstallLocation"),
                                    DisplayIcon = Str(k, "DisplayIcon"),
                                    UninstallString = Str(k, "UninstallString"),
                                    SystemComponent = Str(k, "SystemComponent") == "1" || Str(k, "ParentKeyName").Length > 0,
                                };
                                if (e.DisplayName.Length == 0 || e.UninstallString.Length == 0) continue;
                                // HKCU is shared between views and HKLM 64/32 may overlap: dedupe.
                                if (seen.Add((hive == RegistryHive.CurrentUser ? "U|" : "M|") + name + "|" + e.DisplayName + "|" + e.UninstallString))
                                    list.Add(e);
                            }
                        }
                        catch (Exception ex) when (IsRegistryError(ex)) { Log.Warn("UninstallRegistry: cannot read " + label + "\\" + name, ex); }
                    }
                }
            }
            catch (Exception ex) when (IsRegistryError(ex)) { Log.Warn("UninstallRegistry: cannot open " + label, ex); }
        }

        private static bool IsRegistryError(Exception ex)
            => ex is SecurityException || ex is UnauthorizedAccessException || ex is IOException;

        private static string Str(RegistryKey k, string name)
        {
            object v = k.GetValue(name);
            return v == null ? "" : Convert.ToString(v).Trim();
        }
    }
}
