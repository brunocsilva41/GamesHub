using System;
using System.Diagnostics;
using System.Threading;

namespace GamesHub
{
    /// <summary>
    /// Coalesces bursts of Signal() calls: the action runs on a thread-pool thread at most once per
    /// interval, never concurrently, and a signal is never lost (there is always a trailing run).
    /// </summary>
    public sealed class EventCoalescer : IDisposable
    {
        private readonly Action _action;
        private readonly int _intervalMs;
        private readonly object _gate = new object();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly Timer _timer;
        private long _lastRunMs = long.MinValue / 2;
        private bool _pending, _scheduled, _running, _disposed;

        public EventCoalescer(Action action, int intervalMs)
        {
            _action = action ?? throw new ArgumentNullException(nameof(action));
            _intervalMs = Math.Max(1, intervalMs);
            _timer = new Timer(_ => Fire(), null, Timeout.Infinite, Timeout.Infinite);
        }

        public void Signal()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _pending = true;
                if (_scheduled) return;
                _scheduled = true;
                long wait = _lastRunMs + _intervalMs - _clock.ElapsedMilliseconds;
                _timer.Change(wait > 0 ? wait : 0, Timeout.Infinite);
            }
        }

        private void Fire()
        {
            lock (_gate)
            {
                _scheduled = false;
                if (_disposed || !_pending) return;
                if (_running)
                {
                    _scheduled = true;
                    _timer.Change(_intervalMs, Timeout.Infinite);
                    return;
                }
                _pending = false;
                _running = true;
                _lastRunMs = _clock.ElapsedMilliseconds;
            }
            try
            {
                _action();
            }
            // Resilience boundary: thread-pool timer callback running an arbitrary action; an escape would kill the process.
            catch (Exception ex)
            {
                Log.Warn("Coalesced action failed", ex);
            }
            finally
            {
                lock (_gate) _running = false;
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _disposed = true;
                _timer.Dispose();
            }
        }
    }
}
