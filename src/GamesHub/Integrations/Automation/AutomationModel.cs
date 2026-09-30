using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace GamesHub
{
    /// <summary>Canonical action type names + which ones change a system setting that must be restored.</summary>
    public static class AutomationTypes
    {
        public const string Run = "run", Close = "close", PowerPlan = "powerPlan",
                            AudioDevice = "audioDevice", Resolution = "resolution", Wait = "wait";

        public static readonly string[] All = { Run, Close, PowerPlan, AudioDevice, Resolution, Wait };

        /// <summary>Settings snapshotted before and restored after. Restore order = this order.</summary>
        public static readonly string[] Settings = { Resolution, AudioDevice, PowerPlan };

        public static string Canonical(string type)
            => All.FirstOrDefault(t => string.Equals(t, (type ?? "").Trim(), StringComparison.OrdinalIgnoreCase));

        public static bool IsSetting(string type) => Settings.Contains(type);
    }

    /// <summary>Validation and defensive copies for profiles coming from the UI or from disk.</summary>
    public static class AutomationValidation
    {
        public const int MaxActions = 20;
        public const int MaxWaitSeconds = 30;
        private const int MaxTarget = 1024, MaxArgs = 2048;

        /// <summary>Returns a clean copy: unknown/invalid actions dropped, values normalized, lists capped.</summary>
        public static AutomationProfile Sanitize(AutomationProfile p, out int dropped)
        {
            dropped = 0;
            var r = new AutomationProfile();
            if (p == null) return r;
            r.Enabled = p.Enabled;
            r.UseDefault = p.UseDefault;
            r.Before = SanitizeList(p.Before, ref dropped);
            r.After = SanitizeList(p.After, ref dropped);
            return r;
        }

        public static AutomationProfile Sanitize(AutomationProfile p) => Sanitize(p, out _);

        private static List<AutomationAction> SanitizeList(List<AutomationAction> list, ref int dropped)
        {
            var r = new List<AutomationAction>();
            if (list == null) return r;
            foreach (AutomationAction s in list.Select(SanitizeAction))
            {
                if (s == null || r.Count >= MaxActions) { dropped++; continue; }
                r.Add(s);
            }
            return r;
        }

        /// <summary>Null when the action is invalid (unknown type, missing/invalid target, blocked process).</summary>
        public static AutomationAction SanitizeAction(AutomationAction a)
        {
            if (a == null) return null;
            string type = AutomationTypes.Canonical(a.Type);
            if (type == null) return null;
            string target = (a.Target ?? "").Trim();
            string args = (a.Args ?? "").Trim();
            if (target.Length > MaxTarget || args.Length > MaxArgs) return null;
            var r = new AutomationAction { Type = type, Enabled = a.Enabled };
            switch (type)
            {
                case AutomationTypes.Run:
                    if (target.Length == 0) return null;
                    r.Target = target; r.Args = args;
                    break;
                case AutomationTypes.Close:
                    string name = AutomationProcessRules.NormalizeName(target);
                    if (name.Length == 0 || AutomationProcessRules.IsProtected(name)) return null;
                    r.Target = name;
                    break;
                case AutomationTypes.PowerPlan:
                    if (!Guid.TryParse(target, out Guid g)) return null;
                    r.Target = g.ToString("D");
                    break;
                case AutomationTypes.AudioDevice:
                    if (target.Length == 0) return null;
                    r.Target = target;
                    break;
                case AutomationTypes.Resolution:
                    if (!ResolutionSpec.TryParse(target, out ResolutionSpec spec)) return null;
                    r.Target = spec.ToString();
                    break;
                case AutomationTypes.Wait:
                    if (a.Seconds <= 0) return null;
                    r.Seconds = Math.Min(a.Seconds, MaxWaitSeconds);
                    break;
            }
            return r;
        }

        public static AutomationProfile Clone(AutomationProfile p)
        {
            if (p == null) return new AutomationProfile();
            return new AutomationProfile
            {
                Enabled = p.Enabled,
                UseDefault = p.UseDefault,
                Before = (p.Before ?? new List<AutomationAction>()).Select(CloneAction).ToList(),
                After = (p.After ?? new List<AutomationAction>()).Select(CloneAction).ToList(),
            };
        }

        private static AutomationAction CloneAction(AutomationAction a) => new AutomationAction
        { Type = a.Type, Target = a.Target, Args = a.Args, Seconds = a.Seconds, Enabled = a.Enabled };

        /// <summary>Profile equal to the implicit default (nothing worth persisting for a game).</summary>
        public static bool IsTrivial(AutomationProfile p)
            => p.Enabled && p.UseDefault && p.Before.Count == 0 && p.After.Count == 0;

        /// <summary>Effective ordered action list for a game: default profile's actions first (when the
        /// game profile is enabled and uses the default), then the game's own. Disabled actions skipped.</summary>
        public static List<AutomationAction> Compose(AutomationProfile def, AutomationProfile game, bool before)
        {
            var r = new List<AutomationAction>();
            if (game == null || !game.Enabled) return r;
            if (game.UseDefault && def != null && def.Enabled)
                r.AddRange((before ? def.Before : def.After) ?? new List<AutomationAction>());
            r.AddRange((before ? game.Before : game.After) ?? new List<AutomationAction>());
            return r.Where(a => a != null && a.Enabled).ToList();
        }
    }

    /// <summary>Display mode "WIDTHxHEIGHT@HZ" (Hz = 0 means "keep current refresh rate").</summary>
    public struct ResolutionSpec : IEquatable<ResolutionSpec>
    {
        public int Width, Height, Hz;

        public ResolutionSpec(int w, int h, int hz) { Width = w; Height = h; Hz = hz; }

        private static readonly Regex Rx = new Regex(@"^\s*(\d{3,5})\s*[x×X*]\s*(\d{3,5})\s*(?:@\s*(\d{1,3})\s*(?:hz)?)?\s*$",
                                                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static bool TryParse(string s, out ResolutionSpec spec)
        {
            spec = default(ResolutionSpec);
            Match m = Rx.Match(s ?? "");
            if (!m.Success) return false;
            int w = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            int h = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            int hz = m.Groups[3].Success ? int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) : 0;
            if (w < 320 || h < 200 || w > 16384 || h > 16384 || hz > 1000) return false;
            spec = new ResolutionSpec(w, h, hz);
            return true;
        }

        /// <summary>Id form, e.g. "1920x1080@60" (or "1920x1080" when Hz is 0).</summary>
        public override string ToString()
            => Width.ToString(CultureInfo.InvariantCulture) + "x" + Height.ToString(CultureInfo.InvariantCulture)
               + (Hz > 0 ? "@" + Hz.ToString(CultureInfo.InvariantCulture) : "");

        /// <summary>User-facing label, e.g. "1920 × 1080 (60 Hz)".</summary>
        public string DisplayName => Width + " × " + Height + (Hz > 0 ? " (" + Hz + " Hz)" : "");

        /// <summary>True when this spec is satisfied by <paramref name="actual"/> (Hz 0 matches any rate).</summary>
        public bool Matches(ResolutionSpec actual)
            => Width == actual.Width && Height == actual.Height && (Hz == 0 || Hz == actual.Hz);

        public bool Equals(ResolutionSpec o) => Width == o.Width && Height == o.Height && Hz == o.Hz;
        public override bool Equals(object obj) => obj is ResolutionSpec o && Equals(o);
        public override int GetHashCode() => (Width * 397) ^ (Height * 31) ^ Hz;

        /// <summary>Distinct modes sorted by width, height, refresh rate — all descending.</summary>
        public static List<ResolutionSpec> SortDistinct(IEnumerable<ResolutionSpec> modes)
            => modes.Distinct().OrderByDescending(m => m.Width).ThenByDescending(m => m.Height)
                    .ThenByDescending(m => m.Hz).ToList();
    }
}
