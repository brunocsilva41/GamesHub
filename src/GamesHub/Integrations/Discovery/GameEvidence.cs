using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;

namespace GamesHub
{
    /// <summary>Evidence that a folder holds a game, with the pt-BR reasons behind the score.</summary>
    public sealed class Evidence
    {
        public double Score;
        public List<string> Reasons = new List<string>();
        /// <summary>Xbox PC / Microsoft Store package (Content\MicrosoftGame.config).</summary>
        public bool Xbox;
        /// <summary>DefaultDisplayName from MicrosoftGame.config ("" when absent).</summary>
        public string XboxName = "";
    }

    /// <summary>
    /// Scores a <see cref="FolderSnapshot"/> from file evidence. Weights are additive and grouped:
    ///   engine (cap 0.8): Unity (UnityPlayer.dll 0.55, &lt;exe&gt;_Data 0.25, GameAssembly.dll 0.15), Unreal (Engine\Binaries
    ///     0.45, *-Shipping.exe 0.5, *.pak 0.3), Godot (*.pck 0.45), RPG Maker / GameMaker / Ren'Py / MonoGame / LÖVE;
    ///   store markers (cap 0.6): goggame-*.info 0.6, MicrosoftGame.config 0.6, .egstore 0.5, uplay/upc 0.4, steam_appid.txt 0.3;
    ///   middleware (cap 0.5): steam_api 0.35, EOSSDK 0.3, Bink 0.3, GOG Galaxy SDK 0.3, FMOD 0.25, Wwise 0.25, PhysX 0.2,
    ///     Discord Game SDK 0.2, XInput 0.15, d3dx9 0.15, OpenAL 0.15;
    ///   negative: Chromium/Electron app without engine evidence −0.4.
    /// Location/publisher/registry priors are added by <see cref="GameDiscovery"/>. The final confidence is clamped
    /// to 0..1; candidates below <see cref="Threshold"/> are dropped, the UI preselects those ≥ <see cref="Preselect"/>.
    /// </summary>
    public static class GameEvidence
    {
        public const double Threshold = 0.5;
        public const double Preselect = 0.8;

        private sealed class Acc
        {
            public readonly Dictionary<string, double> Groups = new Dictionary<string, double>();
            public readonly List<KeyValuePair<string, double>> Items = new List<KeyValuePair<string, double>>();
            public void Add(string group, string reason, double w)
            {
                if (Items.Any(i => i.Key == reason)) return;
                Groups[group] = (Groups.TryGetValue(group, out double v) ? v : 0) + w;
                Items.Add(new KeyValuePair<string, double>(reason, w));
            }
        }

        private static readonly Dictionary<string, double> Caps = new Dictionary<string, double>
        {
            ["engine"] = 0.8, ["store"] = 0.6, ["middleware"] = 0.5, ["negative"] = 1,
        };

        public static Evidence Analyze(FolderSnapshot s)
        {
            var ev = new Evidence();
            if (s == null || !s.Readable) return ev;
            var acc = new Acc();
            var names = new HashSet<string>(s.Files.Select(f => f.Name.ToLowerInvariant()));
            bool Has(Func<string, bool> p) => names.Any(p);
            var exes = s.Exes.ToList();

            // ---- engines
            if (names.Contains("unityplayer.dll")) acc.Add("engine", "Unity", 0.55);
            if (exes.Any(e => s.Dirs.Contains(Path.ChangeExtension(e.Rel, null) + "_data"))) acc.Add("engine", "Pasta de dados Unity", 0.25);
            if (names.Contains("gameassembly.dll")) acc.Add("engine", "Unity IL2CPP", 0.15);
            if (s.Dirs.Contains("engine\\binaries")) acc.Add("engine", "Unreal Engine", 0.45);
            if (exes.Any(e => e.Name.IndexOf("-shipping.exe", StringComparison.OrdinalIgnoreCase) > 0)) acc.Add("engine", "Unreal (-Shipping.exe)", 0.5);
            if (Has(n => n.EndsWith(".pak"))) acc.Add("engine", "Pacotes .pak", 0.3);
            if (Has(n => n.EndsWith(".pck"))) acc.Add("engine", "Godot (.pck)", 0.45);
            bool nw = names.Contains("nw.dll") && (s.Dirs.Contains("www") || names.Contains("package.nw"));
            if (nw) acc.Add("engine", "RPG Maker MV/MZ", 0.5);
            if (Has(n => n.EndsWith(".rgssad") || n.EndsWith(".rgss2a") || n.EndsWith(".rgss3a") || n.StartsWith("rgss")))
                acc.Add("engine", "RPG Maker (RGSS)", 0.5);
            if (names.Contains("data.win")) acc.Add("engine", "GameMaker", 0.5);
            if (s.Dirs.Contains("renpy")) acc.Add("engine", "Ren'Py", 0.5);
            if (names.Contains("monogame.framework.dll") || names.Contains("fna.dll")) acc.Add("engine", "MonoGame/FNA", 0.4);
            if (names.Contains("love.dll")) acc.Add("engine", "LÖVE", 0.4);

            // ---- store markers
            if (Has(n => n.StartsWith("goggame-") && n.EndsWith(".info"))) acc.Add("store", "GOG", 0.6);
            string config = s.Files.FirstOrDefault(f => f.Name.Equals("MicrosoftGame.config", StringComparison.OrdinalIgnoreCase))?.FullPath;
            if (config != null)
            {
                acc.Add("store", "Xbox / Microsoft Store", 0.6);
                ev.Xbox = true;
                ev.XboxName = XboxDisplayName(config);
            }
            if (s.Dirs.Contains(".egstore")) acc.Add("store", "Epic Games Store", 0.5);
            if (Has(n => n.StartsWith("uplay_") || n.StartsWith("upc_r") || n.StartsWith("uplay_r"))) acc.Add("store", "Ubisoft (uplay)", 0.4);
            if (names.Contains("steam_appid.txt")) acc.Add("store", "Steam (steam_appid.txt)", 0.3);

            // ---- middleware
            if (Has(n => n.StartsWith("steam_api") && n.EndsWith(".dll"))) acc.Add("middleware", "Steamworks", 0.35);
            if (Has(n => n.StartsWith("eossdk") && n.EndsWith(".dll"))) acc.Add("middleware", "Epic Online Services", 0.3);
            if (Has(n => n.StartsWith("bink") && n.EndsWith(".dll"))) acc.Add("middleware", "Bink Video", 0.3);
            if (Has(n => (n == "galaxy.dll" || n == "galaxy64.dll"))) acc.Add("middleware", "GOG Galaxy SDK", 0.3);
            if (Has(n => n.StartsWith("fmod") && n.EndsWith(".dll"))) acc.Add("middleware", "FMOD", 0.25);
            if (Has(n => n.StartsWith("aksoundengine") || n.StartsWith("wwise"))) acc.Add("middleware", "Wwise", 0.25);
            if (Has(n => n.StartsWith("physx") && n.EndsWith(".dll"))) acc.Add("middleware", "PhysX", 0.2);
            if (names.Contains("discord_game_sdk.dll")) acc.Add("middleware", "Discord Game SDK", 0.2);
            if (Has(n => n.StartsWith("xinput") && n.EndsWith(".dll"))) acc.Add("middleware", "XInput", 0.15);
            if (Has(n => n.StartsWith("d3dx9") && n.EndsWith(".dll"))) acc.Add("middleware", "DirectX 9", 0.15);
            if (names.Contains("openal32.dll")) acc.Add("middleware", "OpenAL", 0.15);

            // ---- negative: desktop apps built on Chromium/Electron
            bool chromium = names.Contains("libcef.dll") || names.Contains("chrome_elf.dll") || s.HasFile("resources\\app.asar")
                            || names.Contains("v8_context_snapshot.bin");
            if (chromium && !acc.Groups.ContainsKey("engine")) acc.Add("negative", "Aplicativo Chromium/Electron", -0.4);

            double score = acc.Groups.Sum(g => Caps.TryGetValue(g.Key, out double cap) ? Math.Min(cap, g.Value) : g.Value);
            ev.Score = score;
            ev.Reasons = acc.Items.Where(i => i.Value > 0).OrderByDescending(i => i.Value).Select(i => i.Key).ToList();
            return ev;
        }

        /// <summary>ShellVisuals/@DefaultDisplayName of a MicrosoftGame.config ("" when missing or malformed).
        /// Localized "ms-resource:" names fall back to the folder name.</summary>
        public static string XboxDisplayName(string configPath)
        {
            try
            {
                if (new FileInfo(configPath).Length > 1024 * 1024) return "";
                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 2 * 1024 * 1024 };
                using (XmlReader r = XmlReader.Create(configPath, settings))
                {
                    while (r.Read())
                    {
                        if (r.NodeType != XmlNodeType.Element || r.LocalName != "ShellVisuals") continue;
                        string name = (r.GetAttribute("DefaultDisplayName") ?? "").Trim();
                        return name.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase) ? "" : name;
                    }
                }
            }
            catch (Exception ex) when (DiscoveryErrors.IsIo(ex) || ex is XmlException || ex is InvalidOperationException)
            {
                Log.Warn("Discovery: cannot read " + configPath, ex);
            }
            return "";
        }
    }
}
