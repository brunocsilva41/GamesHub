using System;
using System.Collections.Generic;
using System.Linq;

namespace GamesHub
{
    internal static class VariantApplier
    {
        public static List<Game> Apply(List<Game> games, List<VariantGroup> groups)
        {
            var result = new List<Game>();
            if (games == null) return result;

            var byId = new Dictionary<string, Game>(StringComparer.OrdinalIgnoreCase);
            foreach (Game g in games)
                if (g != null && !string.IsNullOrEmpty(g.Id) && !byId.ContainsKey(g.Id)) byId[g.Id] = g;

            var collapsed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);   // non-primary members to drop
            var merged = new Dictionary<string, Game>(StringComparer.OrdinalIgnoreCase); // primary id -> clone
            var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (VariantGroup grp in groups)
            {
                List<Game> present = grp.MemberIds
                    .Where(id => byId.ContainsKey(id) && !claimed.Contains(id))
                    .Select(id => byId[id]).ToList();
                if (present.Count < 2) continue; // all/most members missing: leave untouched, keep the group

                Game primary = present.FirstOrDefault(g => string.Equals(g.Id, grp.PrimaryId, StringComparison.OrdinalIgnoreCase))
                               ?? present[0];
                var ordered = new List<Game> { primary };
                ordered.AddRange(present.Where(g => g != primary));

                Game card = CloneGame(primary);
                card.Variants = BuildVariants(grp, ordered, primary);
                card.PlaySeconds = ordered.Sum(g => Math.Max(0, g.PlaySeconds));
                card.LastPlayed = ordered.Where(g => g.LastPlayed.HasValue).Select(g => g.LastPlayed).DefaultIfEmpty(null).Max();
                card.Running = ordered.Any(g => g.Running);
                card.Favorite = ordered.Any(g => g.Favorite);
                FillMissingArt(card.Art, ordered.Skip(1));

                foreach (Game g in ordered) claimed.Add(g.Id);
                foreach (Game g in ordered.Skip(1)) collapsed.Add(g.Id);
                merged[primary.Id] = card;
            }

            var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Game g in games)
            {
                if (g == null) continue;
                if (g.Id != null && collapsed.Contains(g.Id)) continue;
                if (g.Id != null && merged.TryGetValue(g.Id, out Game card))
                {
                    if (emitted.Add(g.Id)) result.Add(card);
                    continue;
                }
                result.Add(g);
            }
            return result;
        }

        private static List<GameVariant> BuildVariants(VariantGroup grp, List<Game> ordered, Game primary)
        {
            var list = new List<GameVariant>();
            var taken = new List<string>();
            foreach (Game g in ordered)
            {
                string label = grp.Labels != null && grp.Labels.TryGetValue(g.Id, out string l) && !string.IsNullOrWhiteSpace(l)
                    ? l.Trim()
                    : VariantLabels.Default(g, g == primary, taken.Concat(ExplicitLabels(grp, g.Id)));
                taken.Add(label);
                list.Add(new GameVariant { Id = g.Id, Label = label });
            }
            return list;
        }

        private static IEnumerable<string> ExplicitLabels(VariantGroup grp, string except)
            => grp.Labels == null ? Enumerable.Empty<string>()
               : grp.Labels.Where(kv => !string.Equals(kv.Key, except, StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Value);

        private static void FillMissingArt(Artwork art, IEnumerable<Game> others)
        {
            foreach (Game o in others)
            {
                if (o.Art == null) continue;
                if (string.IsNullOrEmpty(art.Header)) art.Header = o.Art.Header;
                if (string.IsNullOrEmpty(art.Capsule)) art.Capsule = o.Art.Capsule;
                if (string.IsNullOrEmpty(art.Hero)) art.Hero = o.Art.Hero;
                if (string.IsNullOrEmpty(art.Logo)) art.Logo = o.Art.Logo;
                if (string.IsNullOrEmpty(art.Icon)) art.Icon = o.Art.Icon;
            }
        }

        /// <summary>Deep copy of every field (lists and Artwork included).</summary>
        public static Game CloneGame(Game g) => new Game
        {
            Id = g.Id,
            Name = g.Name,
            Source = g.Source,
            Platform = g.Platform,
            LaunchTarget = g.LaunchTarget,
            LaunchArgs = g.LaunchArgs,
            FilePath = g.FilePath,
            Ext = g.Ext,
            InstallDir = g.InstallDir,
            Exe = g.Exe,
            SteamAppId = g.SteamAppId,
            Favorite = g.Favorite,
            Hidden = g.Hidden,
            Collections = g.Collections == null ? new List<string>() : new List<string>(g.Collections),
            LastPlayed = g.LastPlayed,
            PlaySeconds = g.PlaySeconds,
            AddedAt = g.AddedAt,
            Running = g.Running,
            Art = g.Art == null ? new Artwork() : new Artwork
            {
                Header = g.Art.Header, Capsule = g.Art.Capsule, Hero = g.Art.Hero, Logo = g.Art.Logo, Icon = g.Art.Icon,
            },
            SizeBytes = g.SizeBytes,
            UpdatePending = g.UpdatePending,
            Broken = g.Broken,
            BrokenReason = g.BrokenReason,
            Genres = g.Genres == null ? new List<string>() : new List<string>(g.Genres),
            Variants = g.Variants == null ? new List<GameVariant>()
                : g.Variants.Select(v => new GameVariant { Id = v?.Id ?? "", Label = v?.Label ?? "" }).ToList(),
        };
    }
}
