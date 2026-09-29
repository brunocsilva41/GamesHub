using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace GamesHub
{
    public sealed class SizeCacheEntry
    {
        public string Dir = "";
        public long Bytes = -1;
        public long DirWriteUtcTicks;
        public long ComputedUtcTicks;
    }

    /// <summary>Folder sizes cached in a JSON file keyed by lowercase dir, invalidated by the dir's
    /// LastWriteTime or age (7 days). One computation per dir at a time, at most 2 overall.</summary>
    public sealed class InstallSizeService
    {
        public static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);
        private readonly string _cacheFile;
        private readonly object _gate = new object();
        private readonly Dictionary<string, SizeCacheEntry> _cache;
        private readonly Dictionary<string, Task<long>> _inFlight = new Dictionary<string, Task<long>>();
        private readonly SemaphoreSlim _slots = new SemaphoreSlim(2, 2);

        public InstallSizeService(string cacheFile)
        {
            _cacheFile = cacheFile;
            _cache = Json.Load(cacheFile, new Dictionary<string, SizeCacheEntry>());
        }

        /// <summary>Install folder to size for a game: InstallDir, else the Exe's folder. "" when unknown.</summary>
        public static string DirOf(Game game)
        {
            if (game == null) return "";
            if (!string.IsNullOrWhiteSpace(game.InstallDir)) return InstallPaths.Normalize(game.InstallDir);
            if (!string.IsNullOrWhiteSpace(game.Exe))
            {
                string exe = InstallPaths.Normalize(game.Exe);
                return exe.Length == 0 ? "" : InstallPaths.Normalize(Path.GetDirectoryName(exe));
            }
            return "";
        }

        public long GetCached(string dir)
        {
            string key = InstallPaths.Key(dir);
            if (key.Length == 0) return -1;
            SizeCacheEntry e;
            lock (_gate) { if (!_cache.TryGetValue(key, out e)) return -1; }
            return IsFresh(e, key) ? e.Bytes : -1;
        }

        public Task<long> GetAsync(string dir)
        {
            string key = InstallPaths.Key(dir);
            if (key.Length == 0 || InstallPaths.IsForbiddenRoot(key)) return Task.FromResult(-1L);
            long cached = GetCached(key);
            if (cached >= 0) return Task.FromResult(cached);
            lock (_gate)
            {
                if (_inFlight.TryGetValue(key, out Task<long> running)) return running;
                Task<long> t = Task.Run(() => ComputeAndStoreAsync(key));
                _inFlight[key] = t;
                return t;
            }
        }

        private async Task<long> ComputeAndStoreAsync(string key)
        {
            await _slots.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!Directory.Exists(key)) return -1;
                DateTime stamp = Directory.GetLastWriteTimeUtc(key);
                long bytes = await Task.Factory.StartNew(() => ComputeLowPriority(key), CancellationToken.None,
                    TaskCreationOptions.LongRunning, TaskScheduler.Default).ConfigureAwait(false);
                if (bytes >= 0)
                {
                    lock (_gate)
                    {
                        _cache[key] = new SizeCacheEntry { Dir = key, Bytes = bytes, DirWriteUtcTicks = stamp.Ticks, ComputedUtcTicks = DateTime.UtcNow.Ticks };
                        SaveLocked();
                    }
                }
                return bytes;
            }
            catch (Exception ex)
            {
                Log.Warn("InstallSizeService: sizing failed for " + key, ex);
                return -1;
            }
            finally
            {
                _slots.Release();
                lock (_gate) _inFlight.Remove(key);
            }
        }

        private void SaveLocked()
        {
            try { Json.Save(_cacheFile, _cache); }
            catch (Exception ex) { Log.Warn("InstallSizeService: cannot save " + _cacheFile, ex); }
        }

        private static bool IsFresh(SizeCacheEntry e, string key)
        {
            if (e == null || e.Bytes < 0) return false;
            if (DateTime.UtcNow - new DateTime(e.ComputedUtcTicks, DateTimeKind.Utc) > MaxAge) return false;
            try
            {
                if (!Directory.Exists(key)) return false;
                return Directory.GetLastWriteTimeUtc(key).Ticks == e.DirWriteUtcTicks;
            }
            catch (Exception ex)
            {
                Log.Warn("InstallSizeService: stat failed " + key, ex);
                return false;
            }
        }

        private static long ComputeLowPriority(string dir)
        {
            IntPtr me = GetCurrentThread();
            bool bg = SetThreadPriority(me, THREAD_MODE_BACKGROUND_BEGIN); // lowers CPU + I/O priority
            try { return ComputeSize(dir); }
            finally { if (bg) SetThreadPriority(me, THREAD_MODE_BACKGROUND_END); }
        }

        /// <summary>Recursive size in bytes, skipping reparse points (junctions/symlinks) and unreadable folders.</summary>
        public static long ComputeSize(string dir)
        {
            long total = 0;
            int denied = 0;
            var stack = new Stack<DirectoryInfo>();
            stack.Push(new DirectoryInfo(dir));
            while (stack.Count > 0)
            {
                DirectoryInfo d = stack.Pop();
                IEnumerable<FileSystemInfo> items;
                try { items = d.EnumerateFileSystemInfos(); }
                catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException || ex is System.Security.SecurityException)
                {
                    denied++;
                    continue;
                }
                try
                {
                    foreach (FileSystemInfo fi in items)
                    {
                        if (fi is DirectoryInfo sub)
                        {
                            if ((sub.Attributes & FileAttributes.ReparsePoint) == 0) stack.Push(sub);
                        }
                        else if (fi is FileInfo f)
                        {
                            total += f.Length;
                        }
                    }
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException || ex is System.Security.SecurityException)
                {
                    denied++;
                }
            }
            if (denied > 0) Log.Warn("InstallSizeService: " + denied + " unreadable folder(s) under " + dir);
            return total;
        }

        private const int THREAD_MODE_BACKGROUND_BEGIN = 0x00010000, THREAD_MODE_BACKGROUND_END = 0x00020000;
        [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentThread();
        [DllImport("kernel32.dll")] private static extern bool SetThreadPriority(IntPtr hThread, int nPriority);
    }
}
