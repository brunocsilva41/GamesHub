using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace GamesHub
{
    internal sealed class TrackTarget
    {
        public string Id;
        public string Exe;        // full path or ""
        public string DirPrefix;  // "<InstallDir>\" or ""
    }

    internal sealed class PlayTracker : IDisposable
    {
        public const int IntervalMs = 5000;

        private sealed class ProcEntry { public string Name; public string Path; }

        private readonly Func<List<TrackTarget>> _targets;
        private readonly Action<string, bool> _runningChanged;
        private readonly Action<string, long> _played;
        private readonly Dictionary<int, ProcEntry> _procCache = new Dictionary<int, ProcEntry>();
        private readonly HashSet<string> _running = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly object _pollGate = new object();
        private readonly Timer _timer;
        private readonly int _selfPid = Process.GetCurrentProcess().Id;
        private double _carrySeconds;
        private long _lastTickMs;
        private bool _disposed;

        /// <param name="played">(id, whole seconds played since the last report)</param>
        public PlayTracker(Func<List<TrackTarget>> targets, Action<string, bool> runningChanged, Action<string, long> played)
        {
            _targets = targets;
            _runningChanged = runningChanged;
            _played = played;
            _timer = new Timer(_ => SafePoll(), null, 1500, IntervalMs);
        }

        private void SafePoll()
        {
            try { Poll(); }
            catch (Exception ex) { Log.Warn("Play tracker poll failed", ex); }
        }

        /// <summary>One detection pass. Cheap: only processes not seen before are opened.</summary>
        public void Poll()
        {
            lock (_pollGate)
            {
                if (_disposed) return;
                long now = _clock.ElapsedMilliseconds;
                double elapsed = _lastTickMs == 0 ? 0 : Math.Min(now - _lastTickMs, 3 * IntervalMs) / 1000.0;
                _lastTickMs = now;

                List<TrackTarget> targets = _targets() ?? new List<TrackTarget>();
                var nowRunning = targets.Count == 0 ? new HashSet<string>() : Detect(targets);

                var started = new List<string>();
                var stopped = new List<string>();
                foreach (string id in nowRunning) if (!_running.Contains(id)) started.Add(id);
                foreach (string id in _running) if (!nowRunning.Contains(id)) stopped.Add(id);

                // Time for games that were running during the whole interval (whole seconds, remainder carried).
                var continuing = new List<string>();
                foreach (string id in _running) if (nowRunning.Contains(id)) continuing.Add(id);
                if (continuing.Count > 0)
                {
                    _carrySeconds += elapsed;
                    long whole = (long)_carrySeconds;
                    _carrySeconds -= whole;
                    if (whole > 0) foreach (string id in continuing) _played(id, whole);
                }
                else _carrySeconds = 0;

                foreach (string id in stopped) { _running.Remove(id); _runningChanged(id, false); }
                foreach (string id in started) { _running.Add(id); _runningChanged(id, true); }
            }
        }

        private HashSet<string> Detect(List<TrackTarget> targets)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var alive = new HashSet<int>();
            Process[] procs = Process.GetProcesses();
            try
            {
                foreach (Process p in procs)
                {
                    int pid = p.Id;
                    if (pid <= 4 || pid == _selfPid) continue;
                    alive.Add(pid);
                    string name = p.ProcessName;
                    if (!_procCache.TryGetValue(pid, out ProcEntry e) || e.Name != name)
                        _procCache[pid] = e = new ProcEntry { Name = name, Path = QueryImagePath(pid) };
                    if (e.Path != null) Match(e.Path, targets, result);
                }
            }
            finally { foreach (Process p in procs) p.Dispose(); }

            if (_procCache.Count > alive.Count)
            {
                var dead = new List<int>();
                foreach (int pid in _procCache.Keys) if (!alive.Contains(pid)) dead.Add(pid);
                foreach (int pid in dead) _procCache.Remove(pid);
            }
            return result;
        }

        /// <summary>An exact exe match wins; otherwise every game whose install dir contains the image matches
        /// (so two shortcuts into the same folder don't both show as running when one exe is targeted).</summary>
        internal static void Match(string imagePath, List<TrackTarget> targets, HashSet<string> result)
        {
            bool exact = false;
            foreach (TrackTarget t in targets)
                if (t.Exe.Length > 0 && string.Equals(imagePath, t.Exe, StringComparison.OrdinalIgnoreCase)) { result.Add(t.Id); exact = true; }
            if (exact) return;
            foreach (TrackTarget t in targets)
                if (t.DirPrefix.Length > 0 && imagePath.StartsWith(t.DirPrefix, StringComparison.OrdinalIgnoreCase)) result.Add(t.Id);
        }

        /// <summary>Full image path, or null when the process can't be queried (protected/elevated).</summary>
        private static string QueryImagePath(int pid)
        {
            IntPtr h = OpenProcess(ProcessQueryLimitedInformation, false, pid);
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
            }
            finally { CloseHandle(h); }
        }

        public void Dispose()
        {
            lock (_pollGate) _disposed = true;
            _timer.Dispose();
        }

        private const uint ProcessQueryLimitedInformation = 0x1000;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "QueryFullProcessImageNameW")]
        private static extern bool QueryFullProcessImageName(IntPtr hProcess, int flags, StringBuilder exeName, ref int size);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr h);
    }
}
