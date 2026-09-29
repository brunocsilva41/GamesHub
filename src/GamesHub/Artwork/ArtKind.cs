// OWNER: ART agent.
using System;

namespace GamesHub
{
    /// <summary>Artwork kinds and helpers shared by the artwork module.</summary>
    public static class ArtKind
    {
        public const string Header = "header";
        public const string Capsule = "capsule";
        public const string Hero = "hero";
        public const string Logo = "logo";
        public const string Icon = "icon";

        public static readonly string[] All = { Header, Capsule, Hero, Logo, Icon };

        /// <summary>Kinds that can come from Steam / SteamGridDB (icon is extracted locally).</summary>
        public static readonly string[] Remote = { Header, Capsule, Hero, Logo };

        public static bool IsValid(string kind) => Array.IndexOf(All, kind) >= 0;

        /// <summary>Logos and icons need transparency (PNG); the rest are photos (JPEG).</summary>
        public static bool NeedsAlpha(string kind) => kind == Logo || kind == Icon;

        public static string Get(Artwork art, string kind)
        {
            switch (kind)
            {
                case Header: return art.Header;
                case Capsule: return art.Capsule;
                case Hero: return art.Hero;
                case Logo: return art.Logo;
                case Icon: return art.Icon;
                default: return null;
            }
        }

        public static void Set(Artwork art, string kind, string value)
        {
            switch (kind)
            {
                case Header: art.Header = value; break;
                case Capsule: art.Capsule = value; break;
                case Hero: art.Hero = value; break;
                case Logo: art.Logo = value; break;
                case Icon: art.Icon = value; break;
            }
        }

        /// <summary>True when the id is a plausible Steam app id (digits only, non-zero).</summary>
        public static bool IsAppId(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length > 10) return false;
            foreach (char c in s) if (c < '0' || c > '9') return false;
            return s.TrimStart('0').Length > 0;
        }
    }
}
