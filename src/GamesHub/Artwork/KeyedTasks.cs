// OWNER: ART agent.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GamesHub
{
    /// <summary>
    /// De-duplicates concurrent work by key: while a task for a key is in flight, every caller
    /// asking for the same key awaits that same task (so e.g. two games sharing a Steam app id
    /// trigger one download and both get notified).
    /// </summary>
    public sealed class KeyedTasks
    {
        private readonly object _gate = new object();
        private readonly Dictionary<string, Task> _inFlight = new Dictionary<string, Task>(StringComparer.Ordinal);

        public Task<T> Run<T>(string key, Func<Task<T>> work)
        {
            lock (_gate)
            {
                if (_inFlight.TryGetValue(key, out Task existing)) return (Task<T>)existing;
                Task<T> task = Task.Run(work);
                _inFlight[key] = task;
                task.ContinueWith(_ => { lock (_gate) _inFlight.Remove(key); }, TaskContinuationOptions.ExecuteSynchronously);
                return task;
            }
        }

        public int Count { get { lock (_gate) return _inFlight.Count; } }
    }
}
