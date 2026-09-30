using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GamesHub
{
    /// <summary>
    /// Picks the executable that starts the game among the ones in a <see cref="FolderSnapshot"/>. Helper exes
    /// (installers, uninstallers, crash handlers, redistributables, anti-cheat bootstrappers…) and anything under
    /// "Engine\" are never chosen. Scoring: name similar to the folder/display name (up to +3), Unity "&lt;exe&gt;_Data"
    /// sibling (+4), Xbox gamelaunchhelper.exe (+5), the registry DisplayIcon exe (+2), shallow location (+1.5 at the
    /// root, +0.8 one level down), relative size (largest +1.5), Unreal "-Shipping" (+0.5); launchers, servers,
    /// editors, benchmarks and configuration tools are penalized.
    /// </summary>
    public static class MainExePicker
    {
        private static readonly string[] Penalized = { "launcher", "server", "editor", "benchmark", "config", "settings", "tool", "mod", "32" };

        public static FolderSnapshot.Entry Pick(FolderSnapshot s, IEnumerable<string> names, string preferredExe = "")
        {
            if (s == null) return null;
            var candidates = s.Exes.Where(e => !DiscoveryRules.IsHelperExe(e.Name) && !e.Rel.StartsWith("engine\\", StringComparison.Ordinal)).ToList();
            if (candidates.Count == 0) return null;
            var hints = (names ?? Enumerable.Empty<string>()).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
            long max = Math.Max(1, candidates.Max(e => e.Size));
            return candidates
                .Select(e => new { e, score = Score(s, e, hints, max, preferredExe) })
                .OrderByDescending(x => x.score)
                .ThenBy(x => x.e.Depth)
                .ThenByDescending(x => x.e.Size)
                .ThenBy(x => x.e.Rel, StringComparer.Ordinal)
                .First().e;
        }

        private static double Score(FolderSnapshot s, FolderSnapshot.Entry e, List<string> hints, long max, string preferredExe)
        {
            string stem = Path.GetFileNameWithoutExtension(e.Name);
            double score = hints.Count == 0 ? 0 : hints.Max(h => DiscoveryRules.NameSimilarity(stem, h)) * 3;
            if (s.Dirs.Contains(Path.ChangeExtension(e.Rel, null) + "_data")) score += 4;
            if (e.Name.Equals("gamelaunchhelper.exe", StringComparison.OrdinalIgnoreCase)) score += 5;
            if (preferredExe.Length > 0 && string.Equals(e.FullPath, preferredExe, StringComparison.OrdinalIgnoreCase)) score += 2;
            score += e.Depth == 0 ? 1.5 : e.Depth == 1 ? 0.8 : 0;
            score += 1.5 * e.Size / max;
            if (stem.EndsWith("-Shipping", StringComparison.OrdinalIgnoreCase)) score += 0.5;
            string lower = stem.ToLowerInvariant();
            foreach (string p in Penalized)
            {
                if (!lower.Contains(p)) continue;
                score -= p == "launcher" ? 1.5 : p == "32" || p == "mod" ? 0.3 : 2;
            }
            return score;
        }
    }
}
