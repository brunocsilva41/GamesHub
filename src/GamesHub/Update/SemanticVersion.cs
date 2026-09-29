// OWNER: DIST agent. Minimal SemVer 2.0 parsing/comparison for release tags ("v2.1.0", "2.1.0-beta.2", "2.1").
using System;
using System.Globalization;

namespace GamesHub
{
    public sealed class SemanticVersion : IComparable<SemanticVersion>
    {
        public int Major, Minor, Patch, Revision;
        /// <summary>Pre-release identifiers without the leading '-' ("" = release).</summary>
        public string PreRelease = "";

        public bool IsPreRelease => PreRelease.Length > 0;

        public static SemanticVersion Parse(string s)
        {
            if (!TryParse(s, out SemanticVersion v)) throw new FormatException("Invalid version: " + s);
            return v;
        }

        /// <summary>Accepts an optional 'v'/'V' prefix, 1–4 numeric parts, "-prerelease" and "+build" (ignored).</summary>
        public static bool TryParse(string s, out SemanticVersion v)
        {
            v = null;
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim();
            if (s[0] == 'v' || s[0] == 'V') s = s.Substring(1);
            int plus = s.IndexOf('+');
            if (plus >= 0) s = s.Substring(0, plus);
            string pre = "";
            int dash = s.IndexOf('-');
            if (dash >= 0) { pre = s.Substring(dash + 1); s = s.Substring(0, dash); if (pre.Length == 0) return false; }
            string[] parts = s.Split('.');
            if (parts.Length < 1 || parts.Length > 4) return false;
            var nums = new int[4];
            for (int i = 0; i < parts.Length; i++)
                if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out nums[i])) return false;
            foreach (string id in pre.Split('.'))
                if (pre.Length > 0 && id.Length == 0) return false;
            v = new SemanticVersion { Major = nums[0], Minor = nums[1], Patch = nums[2], Revision = nums[3], PreRelease = pre };
            return true;
        }

        public int CompareTo(SemanticVersion o)
        {
            if (o == null) return 1;
            int c = Major.CompareTo(o.Major);
            if (c == 0) c = Minor.CompareTo(o.Minor);
            if (c == 0) c = Patch.CompareTo(o.Patch);
            if (c == 0) c = Revision.CompareTo(o.Revision);
            if (c != 0) return c;
            if (PreRelease.Length == 0 || o.PreRelease.Length == 0)
                return PreRelease.Length == 0 ? (o.PreRelease.Length == 0 ? 0 : 1) : -1; // release > pre-release
            string[] a = PreRelease.Split('.'), b = o.PreRelease.Split('.');
            for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
            {
                bool an = long.TryParse(a[i], NumberStyles.None, CultureInfo.InvariantCulture, out long ai);
                bool bn = long.TryParse(b[i], NumberStyles.None, CultureInfo.InvariantCulture, out long bi);
                if (an && bn) c = ai.CompareTo(bi);
                else if (an) c = -1;            // numeric identifiers sort before alphanumeric ones
                else if (bn) c = 1;
                else c = string.CompareOrdinal(a[i], b[i]);
                if (c != 0) return Math.Sign(c);
            }
            return a.Length.CompareTo(b.Length);
        }

        public static int Compare(string a, string b) => Parse(a).CompareTo(Parse(b));

        public override string ToString()
        {
            string s = Major + "." + Minor + "." + Patch + (Revision != 0 ? "." + Revision : "");
            return PreRelease.Length > 0 ? s + "-" + PreRelease : s;
        }
    }
}
