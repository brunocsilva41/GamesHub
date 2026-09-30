using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Threading;

namespace GamesHub
{
    /// <summary>A game installed on the PC that is not in the library yet (serialized to the UI by Bridge.Camel).</summary>
    public sealed class DiscoveredGame
    {
        /// <summary>Display name (registry DisplayName, MicrosoftGame.config or the folder name).</summary>
        public string Name = "";
        /// <summary>Main game executable (what AddFromFile turns into a shortcut).</summary>
        public string Exe = "";
        public string InstallDir = "";
        /// <summary>"registry" | "folder" | "xbox"</summary>
        public string Source = "folder";
        /// <summary>0..1; only candidates at or above GameEvidence.Threshold are returned.</summary>
        public double Confidence;
        /// <summary>Short pt-BR evidence labels, strongest first (e.g. "Unity", "GOG").</summary>
        public List<string> Reasons = new List<string>();
        /// <summary>Registry EstimatedSize in bytes; -1 when unknown (folders are not measured).</summary>
        public long SizeBytes = -1;
        public string Publisher = "";
    }

    /// <summary>Global limits of one discovery run: entries examined, wall-clock deadline and cancellation.
    /// Thread-safe; once exhausted every further Spend() fails so the scan winds down quickly.</summary>
    public sealed class ScanBudget
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly long _maxEntries;
        private readonly TimeSpan _deadline;
        private readonly CancellationToken _ct;
        private long _entries;

        public ScanBudget(long maxEntries = 200000, TimeSpan? deadline = null, CancellationToken ct = default)
        {
            _maxEntries = maxEntries;
            _deadline = deadline ?? TimeSpan.FromSeconds(12);
            _ct = ct;
        }

        public long Entries => Interlocked.Read(ref _entries);

        public bool Exhausted => _ct.IsCancellationRequested || Entries >= _maxEntries || _clock.Elapsed >= _deadline;

        /// <summary>Counts <paramref name="n"/> examined entries; false when the budget is used up.</summary>
        public bool Spend(int n = 1)
        {
            if (_ct.IsCancellationRequested || _clock.Elapsed >= _deadline) return false;
            return Interlocked.Add(ref _entries, n) <= _maxEntries;
        }
    }

    internal static class DiscoveryErrors
    {
        /// <summary>Expected file-system / registry failures while probing someone else's folders.</summary>
        public static bool IsIo(Exception ex)
            => ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException
               || ex is ArgumentException || ex is NotSupportedException;
    }
}
