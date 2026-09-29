// OWNER: CORE agent.
using System;
using System.Collections;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
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
        private const string MutexName = @"Local\GamesHub.SingleInstance";
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

        private static string PipeName
        {
            get
            {
                string user = new string(Environment.UserName.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
                return "GamesHub.Activate." + user;
            }
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
                catch (Exception ex)
                {
                    Log.Warn("Activation pipe error", ex);
                    try { await Task.Delay(1000, ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) { break; }
                }
            }
        }

        private static NamedPipeServerStream CreateServer()
        {
            var security = new PipeSecurity();
            security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User,
                PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, AccessControlType.Allow));
            return new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous, 4096, 4096, security);
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
                if (Json.DeserializeObject(payload) is ArrayList list)
                    return list.Cast<object>().Select(o => Convert.ToString(o) ?? "").ToArray();
            }
            catch (Exception ex)
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
