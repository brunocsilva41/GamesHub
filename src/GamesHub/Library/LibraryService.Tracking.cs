using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GamesHub
{
    public sealed partial class LibraryService
    {
        // ------------------------------------------------------------------ watchers / tracker

        private void EnsureInfrastructure()
        {
            if (_disposed) return;
            lock (_infraGate)
            {
                string dir = _settings.GamesDir ?? "";
                if (_folderWatcher != null && !string.Equals(_folderWatcher.Dir, dir, StringComparison.OrdinalIgnoreCase))
                { _folderWatcher.Dispose(); _folderWatcher = null; }
                if (_folderWatcher == null && Directory.Exists(dir))
                    _folderWatcher = TryWatch(dir, ScanFolder, n => GameRules.IsGameFileExt(Path.GetExtension(n)), FolderDebounceMs);

                var storeDirs = new List<string>();
                if (_settings.ImportSteam) storeDirs.AddRange(SteamSource.WatchDirs(SteamSource.LibraryFolders(SteamSource.FindSteamPath())));
                if (_settings.ImportEpic) storeDirs.Add(EpicSource.ManifestsDir);
                string key = string.Join("|", storeDirs).ToLowerInvariant();
                if (key != _storeWatchKey)
                {
                    foreach (DebouncedWatcher w in _storeWatchers) w.Dispose();
                    _storeWatchers = storeDirs.Where(Directory.Exists)
                        .Select(d => TryWatch(d, ScanStores, n => n.EndsWith(".acf", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".item", StringComparison.OrdinalIgnoreCase), StoreDebounceMs))
                        .Where(w => w != null).ToList();
                    _storeWatchKey = key;
                }

                if (_settings.TrackPlaytime && _tracker == null)
                    _tracker = new PlayTracker(() => { lock (_gate) return _trackTargets; }, OnRunningChanged, OnPlayed);
                else if (!_settings.TrackPlaytime && _tracker != null)
                {
                    _tracker.Dispose();
                    _tracker = null;
                    List<string> wasRunning;
                    lock (_gate) wasRunning = _running.ToList();
                    foreach (string id in wasRunning) OnRunningChanged(id, false);
                }
            }
        }

        private static DebouncedWatcher TryWatch(string dir, Action onChange, Func<string, bool> filter, int delayMs)
        {
            try { return new DebouncedWatcher(dir, delayMs, onChange, filter); }
            catch (Exception ex) when (ExpectedErrors.IsFileSystem(ex) || ex is System.ComponentModel.Win32Exception)
            {
                Log.Warn("Cannot watch " + dir, ex);
                return null;
            }
        }

        private void OnRunningChanged(string id, bool running)
        {
            lock (_gate) { if (running) _running.Add(id); else _running.Remove(id); }
            DateTime now = DateTime.Now;
            if (!running) _store.Edit(id, m => m.LastPlayed = now, 0);
            Patch(id, g => { g.Running = running; if (!running) g.LastPlayed = now; });
            Log.Info("Game " + (running ? "started: " : "stopped: ") + id);
            try { RunningChanged?.Invoke(id, running); }
            // Resilience boundary: raises an event to arbitrary subscribers from the play-tracker timer thread.
            catch (Exception ex) { Log.Error("RunningChanged handler failed", ex); }
        }

        private void OnPlayed(string id, long seconds)
        {
            _store.Edit(id, m => m.PlaySeconds += seconds, 60000);
            Patch(id, g => g.PlaySeconds += seconds);
        }
    }
}
