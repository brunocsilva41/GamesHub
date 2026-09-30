using System;
using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GamesHub
{
    /// <summary>
    /// Single instance via a named mutex. The first instance listens on a per-user named pipe; later
    /// instances forward their command line to it and exit.
    /// </summary>
    internal sealed class SingleInstance : IDisposable
    {
        /// <summary>"" for the normal install; a stable suffix when GAMESHUB_DATA_DIR isolates a test/portable
        /// instance, so it never talks to (or is blocked by) the user's running app.</summary>
        private static readonly string Scope = ScopeSuffix();
        private static string MutexName => @"Local\GamesHub.SingleInstance" + Scope;

        private static string ScopeSuffix()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GAMESHUB_DATA_DIR"))) return "";
            uint h = 2166136261;
            foreach (char c in AppPaths.DataDir.ToLowerInvariant()) h = (h ^ c) * 16777619;
            return "." + h.ToString("x8");
        }
        private const int MaxPayload = 64 * 1024;

        private readonly Mutex _mutex;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private bool _disposed;

        /// <summary>Raised on a thread-pool thread with the arguments sent by a second instance.</summary>
        public event Action<string[]> ArgumentsReceived;

        public bool IsFirst { get; }

        private SingleInstance(Mutex mutex, bool isFirst)
        {
            _mutex = mutex;
            IsFirst = isFirst;
        }

        private static string PipeName => PipeNameFor(CurrentUserSid(), CurrentSessionId(), Scope);

        /// <summary>Pipe names live in one machine-wide namespace: the user's SID and the logon session keep
        /// every user/session (and every isolated scope) on its own pipe.</summary>
        internal static string PipeNameFor(string sid, int session, string scope)
        {
            string safe = new string((sid ?? "").Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '_').ToArray());
            return "GamesHub.Activate." + safe + ".s" + session + (scope ?? "");
        }

        private static string CurrentUserSid()
        {
            using (WindowsIdentity me = WindowsIdentity.GetCurrent())
                return me.User?.Value ?? Environment.UserName;
        }

        private static int CurrentSessionId()
        {
            using (Process me = Process.GetCurrentProcess()) return me.SessionId;
        }

        public static SingleInstance Acquire()
        {
            var mutex = new Mutex(false, MutexName);
            bool owned;
            try
            {
                owned = mutex.WaitOne(0, false);
            }
            catch (AbandonedMutexException)
            {
                Log.Warn("Previous instance ended without releasing the mutex; taking ownership.");
                owned = true;
            }
            return new SingleInstance(mutex, owned);
        }

        public void StartListening()
        {
            if (!IsFirst) return;
            Task.Run(() => ListenLoop(_cts.Token));
        }

        private async Task ListenLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    using (NamedPipeServerStream server = CreateServer())
                    {
                        await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                        string payload = await ReadPayload(server).ConfigureAwait(false);
                        string[] args = ParsePayload(payload);
                        if (args != null) ArgumentsReceived?.Invoke(args);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                // Resilience boundary: long-running background listener loop; it must survive any single bad connection.
                catch (Exception ex)
                {
                    Log.Warn("Activation pipe error", ex);
                    try { await Task.Delay(1000, ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) { break; }
                }
            }
        }

        /// <summary>Created through CreateNamedPipe because .NET Framework's NamedPipeServerStream cannot pass
        /// FILE_FLAG_FIRST_PIPE_INSTANCE (fail if someone else already owns the name: no squatting) nor
        /// PIPE_REJECT_REMOTE_CLIENTS. The DACL grants access to the current user only.</summary>
        private static NamedPipeServerStream CreateServer()
        {
            string sid = CurrentUserSid();
            var sd = new RawSecurityDescriptor("D:P(A;;GA;;;" + sid + ")");
            byte[] bin = new byte[sd.BinaryLength];
            sd.GetBinaryForm(bin, 0);
            GCHandle pin = GCHandle.Alloc(bin, GCHandleType.Pinned);
            try
            {
                var sa = new PipeNative.SECURITY_ATTRIBUTES
                {
                    nLength = Marshal.SizeOf(typeof(PipeNative.SECURITY_ATTRIBUTES)),
                    lpSecurityDescriptor = pin.AddrOfPinnedObject(),
                    bInheritHandle = 0,
                };
                SafePipeHandle h = PipeNative.CreateNamedPipe(@"\\.\pipe\" + PipeName,
                    PipeNative.PIPE_ACCESS_INBOUND | PipeNative.FILE_FLAG_OVERLAPPED | PipeNative.FILE_FLAG_FIRST_PIPE_INSTANCE,
                    PipeNative.PIPE_TYPE_BYTE | PipeNative.PIPE_READMODE_BYTE | PipeNative.PIPE_WAIT | PipeNative.PIPE_REJECT_REMOTE_CLIENTS,
                    1, 4096, 4096, 0, ref sa);
                bool owned = false;
                try
                {
                    if (h.IsInvalid) throw new IOException("CreateNamedPipe failed", new Win32Exception(Marshal.GetLastWin32Error()));
                    var server = new NamedPipeServerStream(PipeDirection.In, true, false, h);
                    owned = true; // the stream now owns the handle
                    return server;
                }
                finally
                {
                    if (!owned) h.Dispose();
                }
            }
            finally
            {
                pin.Free();
            }
        }

        /// <summary>True when the process serving the pipe runs as the same user in the same session as us
        /// (so arguments are never handed to a pipe squatted by another account).</summary>
        private static bool ServerIsTrusted(SafePipeHandle pipe)
        {
            if (!PipeNative.GetNamedPipeServerProcessId(pipe, out uint pid) || pid == 0)
            {
                Log.Warn("Activation pipe: cannot read the server process id (" + Marshal.GetLastWin32Error() + ")");
                return false;
            }
            if (PipeNative.GetNamedPipeServerSessionId(pipe, out uint session) && session != (uint)CurrentSessionId())
            {
                Log.Warn("Activation pipe: server runs in another session");
                return false;
            }
            string owner = ProcessUserSid(pid);
            string me = CurrentUserSid();
            if (!string.Equals(owner, me, StringComparison.OrdinalIgnoreCase))
            {
                Log.Warn("Activation pipe: server process " + pid + " belongs to another user");
                return false;
            }
            return true;
        }

        /// <summary>SID of the user a process runs as, or null when it cannot be read.</summary>
        private static string ProcessUserSid(uint pid)
        {
            IntPtr process = PipeNative.OpenProcess(PipeNative.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (process == IntPtr.Zero) return null;
            try
            {
                if (!PipeNative.OpenProcessToken(process, PipeNative.TOKEN_QUERY, out IntPtr token)) return null;
                try
                {
                    using (var id = new WindowsIdentity(token)) return id.User?.Value;
                }
                finally
                {
                    PipeNative.CloseHandle(token);
                }
            }
            catch (Exception ex) when (ex is ArgumentException || ex is System.Security.SecurityException || ex is UnauthorizedAccessException)
            {
                Log.Warn("Activation pipe: cannot read the server's user", ex);
                return null;
            }
            finally
            {
                PipeNative.CloseHandle(process);
            }
        }

        private static class PipeNative
        {
            public const uint PIPE_ACCESS_INBOUND = 0x00000001;
            public const uint FILE_FLAG_OVERLAPPED = 0x40000000;
            public const uint FILE_FLAG_FIRST_PIPE_INSTANCE = 0x00080000;
            public const uint PIPE_TYPE_BYTE = 0x0, PIPE_READMODE_BYTE = 0x0, PIPE_WAIT = 0x0;
            public const uint PIPE_REJECT_REMOTE_CLIENTS = 0x00000008;
            public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
            public const uint TOKEN_QUERY = 0x0008;

            [StructLayout(LayoutKind.Sequential)]
            public struct SECURITY_ATTRIBUTES
            {
                public int nLength;
                public IntPtr lpSecurityDescriptor;
                public int bInheritHandle;
            }

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            public static extern SafePipeHandle CreateNamedPipe(string name, uint openMode, uint pipeMode, uint maxInstances,
                uint outBufferSize, uint inBufferSize, uint defaultTimeout, ref SECURITY_ATTRIBUTES securityAttributes);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool GetNamedPipeServerSessionId(SafePipeHandle pipe, out uint sessionId);

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint processId);

            [DllImport("advapi32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool CloseHandle(IntPtr handle);
        }

        private static async Task<string> ReadPayload(Stream s)
        {
            var buffer = new byte[4096];
            using (var ms = new MemoryStream())
            {
                int n;
                while ((n = await s.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                {
                    ms.Write(buffer, 0, n);
                    if (ms.Length > MaxPayload) throw new InvalidDataException("Activation payload too large");
                }
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        private static string[] ParsePayload(string payload)
        {
            try
            {
                // JavaScriptSerializer yields object[] for JSON arrays (ArrayList only when the target type asks for it).
                if (Json.DeserializeObject(payload) is IEnumerable list && !(list is string))
                    return list.Cast<object>().Select(o => Convert.ToString(o) ?? "").ToArray();
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
            {
                Log.Warn("Invalid activation payload", ex);
                return null;
            }
            Log.Warn("Activation payload is not an array");
            return null;
        }

        /// <summary>Sends args to the running instance. Retries for a few seconds while it starts up.</summary>
        public static bool Forward(string[] args)
        {
            // We are the foreground process right now; let the first instance take the foreground.
            CoreNative.AllowSetForegroundWindow(CoreNative.ASFW_ANY);
            byte[] data = Encoding.UTF8.GetBytes(Json.Serialize(args ?? new string[0]));
            for (int attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    using (var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out))
                    {
                        client.Connect(500);
                        if (!ServerIsTrusted(client.SafePipeHandle))
                        {
                            Log.Warn("Activation pipe: refusing to send the command line to an untrusted server");
                            return false;
                        }
                        client.Write(data, 0, data.Length);
                        client.Flush();
                        return true;
                    }
                }
                catch (Exception ex) when (ex is TimeoutException || ex is IOException)
                {
                    Log.Warn("Activation forward attempt " + (attempt + 1) + " failed: " + ex.Message);
                    Thread.Sleep(300);
                }
            }
            return false;
        }

        public void StopListening()
        {
            if (!_cts.IsCancellationRequested) _cts.Cancel();
        }

        /// <summary>Releases the mutex. Must run on the thread that acquired it (Main).</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            StopListening();
            if (IsFirst)
            {
                try { _mutex.ReleaseMutex(); }
                catch (ApplicationException ex) { Log.Warn("Mutex release failed", ex); }
            }
            _mutex.Dispose();
        }
    }
}
