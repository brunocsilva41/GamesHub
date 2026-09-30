// Authenticode checks for executables whose path comes from files other local users can write
// (e.g. C:\ProgramData\...): WinVerifyTrust validates the embedded signature, and the signer's subject is
// read from the same file. Results are cached per path + last write time + size.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;

namespace GamesHub
{
    internal static class Authenticode
    {
        private sealed class CacheEntry
        {
            public DateTime MTime;
            public long Size;
            public bool Valid;
            public string Subject;
        }

        private static readonly ConcurrentDictionary<string, CacheEntry> Cache =
            new ConcurrentDictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);

        /// <summary>True when the file carries a valid embedded Authenticode signature whose signer subject
        /// satisfies <paramref name="subjectOk"/>. Never throws; false for missing/unsigned/tampered files.</summary>
        public static bool IsSignedBy(string path, Func<string, bool> subjectOk)
        {
            if (subjectOk == null || !SafePath.IsLocalAbsolute(path)) return false;
            CacheEntry e = Check(path);
            return e != null && e.Valid && subjectOk(e.Subject ?? "");
        }

        /// <summary>RiotClientServices.exe signed by Riot Games.</summary>
        public static bool IsRiotSigned(string path) => IsSignedBy(path, IsRiotSubject);

        /// <summary>Pure: the certificate subject names Riot Games as its CN or O. Windows formats the subject with
        /// values containing commas quoted (CN="Riot Games, Inc.", O="Riot Games, Inc.", ...). The whole value must
        /// be the company name: "CN=Not Riot Games" or O="Riot Games, Inc. Evil" fail.</summary>
        public static bool IsRiotSubject(string subject)
        {
            foreach (var rdn in SplitDn(subject))
            {
                if ((rdn.Key.Equals("CN", StringComparison.OrdinalIgnoreCase) || rdn.Key.Equals("O", StringComparison.OrdinalIgnoreCase))
                    && RiotName.IsMatch(rdn.Value))
                    return true;
            }
            return false;
        }

        private static readonly Regex RiotName = new Regex(@"\ARiot Games(,? (Inc|LLC)\.?)?\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>Splits a distinguished name on commas outside double quotes; values are unquoted and trimmed.</summary>
        internal static List<KeyValuePair<string, string>> SplitDn(string dn)
        {
            var list = new List<KeyValuePair<string, string>>();
            if (string.IsNullOrEmpty(dn)) return list;
            var cur = new StringBuilder();
            bool quoted = false;
            void Flush()
            {
                string part = cur.ToString();
                cur.Clear();
                int eq = part.IndexOf('=');
                if (eq <= 0) return;
                list.Add(new KeyValuePair<string, string>(part.Substring(0, eq).Trim(), part.Substring(eq + 1).Trim()));
            }
            foreach (char c in dn)
            {
                if (c == '"') { quoted = !quoted; continue; }
                if (c == ',' && !quoted) { Flush(); continue; }
                cur.Append(c);
            }
            Flush();
            return list;
        }

        private static CacheEntry Check(string path)
        {
            DateTime mtime;
            long size;
            try
            {
                var fi = new FileInfo(path);
                if (!fi.Exists) return null;
                mtime = fi.LastWriteTimeUtc;
                size = fi.Length;
            }
            catch (Exception ex) when (ExpectedErrors.IsFileSystem(ex)) { Log.Warn("Authenticode: cannot stat " + path, ex); return null; }

            if (Cache.TryGetValue(path, out CacheEntry cached) && cached.MTime == mtime && cached.Size == size) return cached;

            var entry = new CacheEntry { MTime = mtime, Size = size, Valid = VerifyTrust(path) };
            if (entry.Valid) entry.Subject = SignerSubject(path);
            Cache[path] = entry;
            return entry;
        }

        private static string SignerSubject(string path)
        {
            try
            {
                using (var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path)))
                    return cert.Subject ?? "";
            }
            catch (CryptographicException ex) { Log.Warn("Authenticode: no signer certificate in " + path, ex); return ""; }
        }

        // ------------------------------------------------------------------ WinVerifyTrust

        private static readonly Guid GenericVerifyV2 = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        private const uint WTD_UI_NONE = 2;
        private const uint WTD_REVOKE_NONE = 0;
        private const uint WTD_CHOICE_FILE = 1;
        private const uint WTD_STATEACTION_VERIFY = 1;
        private const uint WTD_STATEACTION_CLOSE = 2;
        private const uint WTD_CACHE_ONLY_URL_RETRIEVAL = 0x1000;   // never go to the network from a library scan

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private sealed class WintrustFileInfo
        {
            public readonly uint cbStruct = (uint)Marshal.SizeOf(typeof(WintrustFileInfo));
            public string pcwszFilePath;
            public readonly IntPtr hFile = IntPtr.Zero;
            public readonly IntPtr pgKnownSubject = IntPtr.Zero;
        }

        [StructLayout(LayoutKind.Sequential)]
        private sealed class WintrustData
        {
            public readonly uint cbStruct = (uint)Marshal.SizeOf(typeof(WintrustData));
            public readonly IntPtr pPolicyCallbackData = IntPtr.Zero;
            public readonly IntPtr pSIPClientData = IntPtr.Zero;
            public readonly uint dwUIChoice = WTD_UI_NONE;
            public readonly uint fdwRevocationChecks = WTD_REVOKE_NONE;
            public readonly uint dwUnionChoice = WTD_CHOICE_FILE;
            public IntPtr pFile;
            public uint dwStateAction = WTD_STATEACTION_VERIFY;
            // Written by wintrust through the pinned (blittable) instance; readonly only restricts managed code.
            public readonly IntPtr hWVTStateData = IntPtr.Zero;
            public readonly IntPtr pwszURLReference = IntPtr.Zero;
            public readonly uint dwProvFlags = WTD_CACHE_ONLY_URL_RETRIEVAL;
            public readonly uint dwUIContext;
            public readonly IntPtr pSignatureSettings = IntPtr.Zero;
        }

        [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false, CharSet = CharSet.Unicode)]
        private static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionID, WintrustData pWVTData);

        private static bool VerifyTrust(string path)
        {
            var file = new WintrustFileInfo { pcwszFilePath = path };
            IntPtr pFile = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WintrustFileInfo)));
            var data = new WintrustData();
            try
            {
                Marshal.StructureToPtr(file, pFile, false);
                data.pFile = pFile;
                int hr = WinVerifyTrust(IntPtr.Zero, GenericVerifyV2, data);
                if (hr != 0) Log.Warn("Authenticode: signature not trusted for " + path + " (0x" + hr.ToString("X8") + ")");
                return hr == 0;
            }
            finally
            {
                data.dwStateAction = WTD_STATEACTION_CLOSE;
                WinVerifyTrust(IntPtr.Zero, GenericVerifyV2, data);
                Marshal.DestroyStructure(pFile, typeof(WintrustFileInfo));
                Marshal.FreeHGlobal(pFile);
            }
        }
    }
}
