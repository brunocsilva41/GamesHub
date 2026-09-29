// OWNER: META agent. Minimum-interval rate limiter with a back-off window (pure logic, injectable clock).
using System;

namespace GamesHub
{
    public sealed class MetadataRateLimiter
    {
        private readonly TimeSpan _interval;
        private readonly Func<DateTime> _utcNow;
        private readonly object _gate = new object();
        private DateTime _next = DateTime.MinValue;       // earliest time the next request may start
        private DateTime _backoffUntil = DateTime.MinValue;

        public MetadataRateLimiter(TimeSpan interval, Func<DateTime> utcNow = null)
        {
            _interval = interval;
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        /// <summary>How long the caller must wait before starting a request (zero = go now).</summary>
        public TimeSpan Delay()
        {
            lock (_gate)
            {
                DateTime now = _utcNow();
                DateTime at = _next > _backoffUntil ? _next : _backoffUntil;
                return at > now ? at - now : TimeSpan.Zero;
            }
        }

        public bool InBackoff { get { lock (_gate) return _utcNow() < _backoffUntil; } }

        /// <summary>Atomically claims the next request slot. Returns false (with the remaining wait) when
        /// the caller must wait first; true means "start the request now" (and the slot is consumed).</summary>
        public bool TryAcquire(out TimeSpan wait)
        {
            lock (_gate)
            {
                wait = Delay();
                if (wait > TimeSpan.Zero) return false;
                MarkRequest();
                return true;
            }
        }

        /// <summary>Records that a request starts now; the next one may start after the interval.</summary>
        public void MarkRequest()
        {
            lock (_gate) _next = _utcNow() + _interval;
        }

        /// <summary>Blocks all requests for <paramref name="duration"/> from now (e.g. after HTTP 429).</summary>
        public void Backoff(TimeSpan duration)
        {
            lock (_gate)
            {
                DateTime until = _utcNow() + duration;
                if (until > _backoffUntil) _backoffUntil = until;
            }
        }
    }
}
