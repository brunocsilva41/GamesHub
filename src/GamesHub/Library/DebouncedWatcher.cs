using System;
using System.IO;
using System.Threading;

namespace GamesHub
{
    internal sealed class DebouncedWatcher : IDisposable
    {
        private readonly FileSystemWatcher _fsw;
        private readonly Timer _timer;
        private readonly int _delayMs;
        private readonly Func<string, bool> _filter;
        public string Dir { get; }

        /// <param name="filter">Optional predicate on the changed file name; events it rejects are ignored.</param>
        public DebouncedWatcher(string dir, int delayMs, Action onChange, Func<string, bool> filter = null)
        {
            Dir = dir;
            _delayMs = delayMs;
            _filter = filter;
            _timer = new Timer(_ => Fire(onChange), null, Timeout.Infinite, Timeout.Infinite);
            _fsw = new FileSystemWatcher(dir)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            _fsw.Created += OnEvent;
            _fsw.Deleted += OnEvent;
            _fsw.Changed += OnEvent;
            _fsw.Renamed += (s, e) => { if (Accept(e.OldName) || Accept(e.Name)) Kick(); };
            _fsw.Error += (s, e) => { Log.Warn("Watcher error on " + Dir, e.GetException()); Kick(); };
            _fsw.EnableRaisingEvents = true;
        }

        private bool Accept(string name) => _filter == null || _filter(name ?? "");
        private void OnEvent(object sender, FileSystemEventArgs e) { if (Accept(e.Name)) Kick(); }
        private void Kick() { try { _timer.Change(_delayMs, Timeout.Infinite); } catch (ObjectDisposedException ex) { Log.Warn("Timer used after dispose", ex); } }

        private static void Fire(Action onChange)
        {
            try { onChange(); }
            catch (Exception ex) { Log.Error("Watcher callback failed", ex); }
        }

        public void Dispose()
        {
            _fsw.EnableRaisingEvents = false;
            _fsw.Dispose();
            _timer.Dispose();
        }
    }
}
