using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace GamesHub
{
    public static class RiotCatalog
    {
        private static readonly Dictionary<string, string> Names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["league_of_legends"] = "League of Legends",
            ["valorant"] = "VALORANT",
            ["bacon"] = "Legends of Runeterra",
            ["lion"] = "2XKO",
            ["teamfighttactics"] = "Teamfight Tactics",
        };

        /// <summary>Real game process, relative to product_install_full_path (first existing wins), so
        /// play-time tracking follows the game and not the Riot Client / LeagueClient.</summary>
        private static readonly Dictionary<string, string[]> Exes = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["league_of_legends"] = new[] { @"Game\League of Legends.exe" },
            ["valorant"] = new[] { @"ShooterGame\Binaries\Win64\VALORANT-Win64-Shipping.exe",
                                    @"live\ShooterGame\Binaries\Win64\VALORANT-Win64-Shipping.exe" },
            ["lion"] = new[] { @"Lion\Binaries\Win64\Lion-Win64-Shipping.exe" },
            ["teamfighttactics"] = new[] { @"TFT\Binaries\Win64\TFTClient-Win64-Shipping.exe" },
            ["bacon"] = new[] { "LoR.exe" },
        };

        /// <summary>"league_of_legends.live" → ("league_of_legends", "live"). Null when malformed.</summary>
        public static bool TrySplitProductDir(string dirName, out string product, out string patchline)
        {
            product = patchline = "";
            if (string.IsNullOrEmpty(dirName)) return false;
            int dot = dirName.IndexOf('.');
            if (dot <= 0 || dot == dirName.Length - 1) return false;
            product = dirName.Substring(0, dot);
            patchline = dirName.Substring(dot + 1);
            return patchline.IndexOf('.') < 0;   // "league_of_legends.live.game_patch" is not a product
        }

        public static string DisplayName(string product, string patchline, string shortcutName = "")
        {
            string name;
            if (!Names.TryGetValue(product ?? "", out name))
            {
                string sc = (shortcutName ?? "").Trim();
                if (sc.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) sc = sc.Substring(0, sc.Length - 4).Trim();
                name = sc.Length > 0 ? sc : TitleCase(product);
            }
            if (!IsLive(patchline)) name += " (" + (patchline ?? "").ToUpperInvariant() + ")";
            return name;
        }

        public static string TitleCase(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            string spaced = s.Replace('_', ' ').Replace('-', ' ').Trim();
            return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(spaced.ToLowerInvariant());
        }

        public static bool IsLive(string patchline) => string.Equals(patchline, "live", StringComparison.OrdinalIgnoreCase);

        public static string GameId(string product, string patchline)
            => "riot:" + (product ?? "").ToLowerInvariant() + (IsLive(patchline) ? "" : "." + (patchline ?? "").ToLowerInvariant());

        public static string LaunchArgs(string product, string patchline)
            => "--launch-product=" + product + " --launch-patchline=" + patchline;

        /// <summary>Candidate game executables (absolute) for a product installed at installDir.</summary>
        public static List<string> ExeCandidates(string product, string installDir)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(installDir)) return list;
            if (Exes.TryGetValue(product ?? "", out string[] rel))
                foreach (string r in rel) list.Add(Path.Combine(installDir, r));
            return list;
        }

        /// <summary>Riot writes forward slashes and trailing separators ("F:/Riot Games/2XKO/Live/").</summary>
        public static string NormalizePath(string p)
        {
            if (string.IsNullOrWhiteSpace(p)) return "";
            string s = p.Trim().Replace('/', '\\');
            if (s.Length > 3) s = s.TrimEnd('\\');
            return s;
        }
    }
}
