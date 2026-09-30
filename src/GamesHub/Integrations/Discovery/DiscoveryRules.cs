using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace GamesHub
{
    /// <summary>Name rules of game discovery: normalization, negative evidence (utilities, drivers, runtimes,
    /// launchers, dev tools), game publishers and helper executables that are never the main game exe.</summary>
    public static class DiscoveryRules
    {
        private static readonly RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

        /// <summary>Registry DisplayName / folder name of software that is never a game (hard exclusion).</summary>
        private static readonly Regex NotAGame = new Regex(
            @"^(microsoft|windows|intel|realtek|nvidia|amd|radeon|oracle|adobe|autodesk|jetbrains|google|mozilla)\b|^(ubisoft|rockstar games)$" +
            @"|visual c\+\+|redistributable|redist\b|\bruntime\b|\.net (framework|runtime|sdk)|directx|\bdrivers?\s*$|\bdriver (package|software|installer)" +
            @"|\bsdk\b|webview2|\bupdater?$|\bupdate (assistant|service|helper)|\buninstall" +
            @"|^steam$|^steam\b.*(client|beta)|steamworks|epic games launcher|epic online services|^launcher$|^origin$|^ea app$|ea desktop" +
            @"|ubisoft connect|ubisoft game launcher|^uplay\b|battle\.net|gog galaxy|riot client|riot vanguard|^vanguard$|rockstar games launcher" +
            @"|^xbox( app)?$|gaming services|amazon games|^itch$|^heroic games launcher|^playnite\b|^gameshub\b|^jogos hub$|^hydra( launcher)?$|^launchbox\b" +
            @"|discord|overwolf|afterburner|rivatuner|logitech|razer (synapse|cortex|central)|corsair|steelseries|hyperx|elgato|nzxt|icue|armoury" +
            @"|anti-?cheat|easyanticheat|battleye|punkbuster|vulkan|physx|openal|\bxna\b|ds4windows|vigem|x360ce" +
            @"|visual studio|\bpython\b|node\.js|^nodejs$|^git\b|^java\b|\bjdk\b|^office\b|^chrome$|firefox|^opera\b|^brave\b" +
            @"|7-zip|winrar|\bvlc\b|videolan|obs studio|obs-studio|spotify|whatsapp|telegram|^zoom\b|teamviewer|anydesk|parsec" +
            @"|unity hub|^unity( \d|$)|unreal engine|^ue_\d|^godot|blender|cmake|docker|virtualbox|vmware|wireshark|npcap" +
            @"|^vortex$|mod organizer|nexus mods|wabbajack|cheat engine|^common files$|^package cache$|installshield|^windowsapps$" +
            @"|^modifiablewindowsapps$|^reference assemblies$|^msbuild$|^dotnet$|^internet explorer$|^uninstall information$",
            Opt);

        /// <summary>Publishers of hardware, drivers and desktop software (never games): excludes registry entries.</summary>
        private static readonly Regex UtilityPublisher = new Regex(
            @"^(microsoft corporation|nvidia corporation|intel( corporation)?|advanced micro devices.*|amd|realtek.*|google (llc|inc\.?)" +
            @"|mozilla.*|oracle.*|adobe.*|autodesk.*|jetbrains.*|python software foundation|the git development community|docker inc\.?" +
            @"|vmware.*|logitech.*|corsair.*|steelseries.*|discord inc\.?|overwolf.*|obs project|videolan|igor pavlov" +
            @"|win\.rar gmbh|dropbox.*|zoom video.*|spotify.*|anydesk.*|teamviewer.*|node\.js foundation|github.*)$", Opt);

        /// <summary>Publishers whose products are practically always games.</summary>
        private static readonly Regex GamePublisher = new Regex(
            @"\b(ubisoft|electronic arts|bethesda|cd projekt|rockstar games|square enix|capcom|bandai namco|sega|2k\b|take-two" +
            @"|activision|blizzard|devolver|paradox|thq|warner bros|wb games|konami|focus (home )?entertainment|deep silver|koch media" +
            @"|gog(\.com| sp)|mojang|xbox game studios|microsoft studios|team17|annapurna|id software|remedy|frontier developments" +
            @"|codemasters|riot games|hoyoverse|mihoyo|cognosphere|nexon|krafton|pearl abyss|ncsoft|gearbox|505 games|tinybuild" +
            @"|raw fury|chucklefish|re-logic|facepunch|klei|supergiant|larian|fromsoftware|kalypso|nacon|bigben|plaion|embracer" +
            @"|hello games|coffee stain|behaviour interactive|grinding gear|digital extremes|wargaming|gaijin|smilegate|playstation" +
            @"|sony interactive|ea games|ea sports|origin games)\b", Opt);

        /// <summary>Generic "this is a games company" words in a publisher name.</summary>
        private static readonly Regex GameishPublisher = new Regex(@"\b(games?|gaming|studios?|interactive|entertainment)\b", Opt);

        /// <summary>Executables that are never the game itself (installers, crash reporters, redistributables, helpers).</summary>
        private static readonly Regex HelperExe = new Regex(
            @"^(unins|uninst|setup|install|vc_?redist|vcredist|dxsetup|dxwebsetup|directx|dotnet|ndp\d|oalinst|physx|ue\d?prereq|prereq" +
            @"|unitycrashhandler|crashreport|crashpad|crash_?handler|bugsplat|bssndrpt|cefsharp|qtwebengineprocess|easyanticheat" +
            @"|start_protected_game|eac_|beservice|battleye|notification|touchup|activation|register|cleanup|repair|vc2010|vcrun|xnafx)" +
            @"|^(7z\w*|zip|dxdiag|ffmpeg|python\w*|node|java\w*|git|cmd|powershell|rundll32|regsvr32|msiexec|conhost)\.exe$" +
            @"|(crash|report(er)?|redist|setup|installer|uninstall|updater?|patcher|subprocess|webhelper|web_helper|overlay" +
            @"|diagnostics?|feedback|dump|symbol|elevat\w*|service)\.exe$" +
            @"|(?<!gamelaunch)helper\.exe$", Opt);

        private static readonly Regex Diacritics = new Regex(@"\p{Mn}+", RegexOptions.CultureInvariant);
        private static readonly Regex NonAlnum = new Regex(@"[^a-z0-9]+", RegexOptions.CultureInvariant);
        private static readonly Regex VersionSuffix = new Regex(@"(\s*[\(\[]?(v|version|build)?\s*\d+(\.\d+){1,3}[a-z]?[\)\]]?|\s*\((x64|x86|64-bit|32-bit)\))+\s*$", Opt);

        /// <summary>Release-group / download-site tags in folder names ("Game-SteamRIP.com", "Game [FitGirl Repack]").</summary>
        private static readonly Regex ReleaseNoise = new Regex(
            @"[\s._\-\[\(]*\b(steamrip(\.com)?|fitgirl|dodi|elamigos|repack|codex|plaza|skidrow|empress|tenoke|rune|razor1911|portable|gog|www\.\S+)\b[\]\)]*", Opt);

        /// <summary>Folder name without release tags, with "-"/"_"/"." as spaces when it has none:
        /// "Lethal-Company-SteamRIP.com" → "Lethal Company". Returns "" when nothing meaningful is left.</summary>
        public static string CleanFolderName(string folder, out bool hadNoise)
        {
            string s = (folder ?? "").Trim();
            string stripped = ReleaseNoise.Replace(s, " ").Trim(' ', '-', '_', '.');
            hadNoise = stripped != s;
            if (stripped.IndexOf(' ') < 0) stripped = stripped.Replace('-', ' ').Replace('_', ' ').Replace('.', ' ');
            return CleanDisplayName(stripped);
        }

        public static bool IsNotAGame(string name) => !string.IsNullOrWhiteSpace(name) && NotAGame.IsMatch(name.Trim());

        public static bool IsUtilityPublisher(string publisher) => !string.IsNullOrWhiteSpace(publisher) && UtilityPublisher.IsMatch(publisher.Trim());

        public static bool IsGamePublisher(string publisher) => !string.IsNullOrWhiteSpace(publisher) && GamePublisher.IsMatch(publisher);

        public static bool IsGameishPublisher(string publisher) => !string.IsNullOrWhiteSpace(publisher) && GameishPublisher.IsMatch(publisher);

        /// <summary>True for installers, crash handlers, redistributables and other helper executables.</summary>
        public static bool IsHelperExe(string fileName) => !string.IsNullOrEmpty(fileName) && HelperExe.IsMatch(fileName);

        /// <summary>"The Witcher® 3: Wild Hunt" → "the witcher 3 wild hunt" (lowercase, no accents/symbols).</summary>
        public static string NormalizeName(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            string d = Diacritics.Replace(s.Normalize(NormalizationForm.FormD), "");
            return NonAlnum.Replace(d.ToLowerInvariant(), " ").Trim();
        }

        /// <summary>Normalized name without spaces ("hollowknight"), used to compare exe and folder names.</summary>
        public static string Compact(string s) => NormalizeName(s).Replace(" ", "");

        /// <summary>Display name cleanup: trailing versions / bitness ("Game v1.2.3 (64-bit)" → "Game"), "_" and "." as spaces.</summary>
        public static string CleanDisplayName(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            string t = VersionSuffix.Replace(s.Trim(), "").Trim();
            if (t.Length == 0) t = s.Trim();
            if (t.IndexOf(' ') < 0) t = t.Replace('_', ' ').Replace('.', ' ');
            return Regex.Replace(t, @"\s{2,}", " ").Trim();
        }

        /// <summary>0..1 similarity of two names: 1 when the compact forms are equal, otherwise token overlap
        /// (Jaccard), with a floor of 0.6 when one compact form contains the other (≥ 4 chars).</summary>
        public static double NameSimilarity(string a, string b)
        {
            string ca = Compact(a), cb = Compact(b);
            if (ca.Length == 0 || cb.Length == 0) return 0;
            if (ca == cb) return 1;
            var ta = NormalizeName(a).Split(' ').Where(t => t.Length > 0).ToList();
            var tb = NormalizeName(b).Split(' ').Where(t => t.Length > 0).ToList();
            double inter = ta.Intersect(tb).Count(), union = ta.Union(tb).Count();
            double j = union > 0 ? inter / union : 0;
            bool contains = (ca.Length >= 4 && cb.Contains(ca)) || (cb.Length >= 4 && ca.Contains(cb));
            return Math.Max(j, contains ? 0.6 : 0);
        }

        internal static string Lower(string s) => (s ?? "").ToLower(CultureInfo.InvariantCulture);
    }
}
