using System;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security;

namespace GamesHub
{
    /// <summary>Exception filters for the failures an operation is expected to hit (used in
    /// <c>catch (Exception ex) when (...)</c>) so that programming errors are not swallowed.</summary>
    internal static class ExpectedErrors
    {
        /// <summary>File/directory access: missing or locked files, denied access, invalid or unsupported paths.</summary>
        public static bool IsFileSystem(Exception ex)
            => ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException
               || ex is ArgumentException || ex is NotSupportedException;

        /// <summary>Malformed or unexpected JSON (JavaScriptSerializer) and value conversions of its content.</summary>
        public static bool IsJson(Exception ex)
            => ex is ArgumentException || ex is InvalidOperationException || ex is FormatException
               || ex is OverflowException || ex is InvalidCastException;

        /// <summary>Reading a JSON file: file-system or content errors.</summary>
        public static bool IsFileOrJson(Exception ex) => IsFileSystem(ex) || IsJson(ex);

        /// <summary>Registry access: denied or unavailable keys.</summary>
        public static bool IsRegistry(Exception ex)
            => ex is SecurityException || ex is UnauthorizedAccessException || ex is IOException;

        /// <summary>HTTP calls: transport errors, timeouts/cancellation and bad responses.</summary>
        public static bool IsNetwork(Exception ex)
            => ex is HttpRequestException || ex is WebException || ex is OperationCanceledException
               || ex is IOException || ex is InvalidOperationException;

        /// <summary>Processes: start/query/close failures (missing file, denied access, process already exited).</summary>
        public static bool IsProcess(Exception ex)
            => ex is Win32Exception || ex is InvalidOperationException || ex is FileNotFoundException
               || ex is ObjectDisposedException || ex is NotSupportedException;

        /// <summary>Native OS calls (P/Invoke, COM): HRESULT/Win32 failures, failed QueryInterface, rejected values.</summary>
        public static bool IsOsCall(Exception ex)
            => ex is ExternalException || ex is InvalidOperationException || ex is ArgumentException
               || ex is InvalidCastException || ex is UnauthorizedAccessException || ex is NotSupportedException;

        /// <summary>COM interop / shell / GDI+ failures.</summary>
        public static bool IsInterop(Exception ex)
            => ex is COMException || ex is ExternalException || ex is InvalidCastException
               || ex is ArgumentException || ex is UnauthorizedAccessException;
    }
}
