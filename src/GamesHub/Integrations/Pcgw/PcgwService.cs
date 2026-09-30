// Lookup order: disk cache → PCGW API (Cargo by Steam appid / opensearch by name, then wikitext) →
// Ludusavi manifest (PCGW-derived) when the API is unreachable (PCGW sits behind a Cloudflare challenge).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace GamesHub
{
    public sealed class PcgwService : IPcgwService
    {
        private const string Site = "https://www.pcgamingwiki.com";
        private readonly AppSettings _settings;
        private readonly PcgwCache _cache;
        private readonly LudusaviManifest _manifest;

        public PcgwService(AppSettings settings)
        {
            _settings = settings ?? new AppSettings();
            string dir = Path.Combine(AppPaths.CacheDir, "pcgw");
            _cache = new PcgwCache(dir);
            _manifest = new LudusaviManifest(Path.Combine(dir, "ludusavi-manifest.yaml"));
        }

        public string PageUrl(Game game, string steamAppId)
        {
            string id = AppIdOf(game, steamAppId);
            if (id != null) return Site + "/api/appid.php?appid=" + id;
            string name = PcgwNames.CleanForSearch(game?.Name);
            if (name.Length == 0) return Site + "/";
            return Site + "/w/index.php?search=" + Uri.EscapeDataString(name);
        }

        public async Task<PcgwInfo> GetInfoAsync(Game game, string steamAppId)
        {
            var info = new PcgwInfo();
            if (!_settings.PcgwEnabled || game == null && string.IsNullOrEmpty(steamAppId)) return info;
            try
            {
                string title = await FindTitleAsync(game, AppIdOf(game, steamAppId)).ConfigureAwait(false);
                if (title == null) return info;
                List<PcgwRow> rows = await GetRowsAsync(title).ConfigureAwait(false);
                if (rows == null) return info;
                info.Found = true;
                info.Title = title;
                info.PageUrl = PcgwApi.WikiUrl(title);
                var folders = new PcgwFolders(game);
                foreach (PcgwRow row in rows)
                {
                    ResolvedPath rp = PcgwPaths.ToResolved(row, folders);
                    List<ResolvedPath> list = row.Kind == "config" ? info.ConfigLocations : info.SaveLocations;
                    if (!list.Any(x => x.Raw == rp.Raw)) list.Add(rp);
                }
                // Existing locations first, then resolvable ones, then the rest (stable within groups).
                info.SaveLocations = Order(info.SaveLocations);
                info.ConfigLocations = Order(info.ConfigLocations);
            }
            // Resilience boundary: consumes untrusted PCGamingWiki / Ludusavi data (network, JSON, wikitext, YAML) and walks the file system with wiki-provided patterns; the UI gets an empty result instead.
            catch (Exception ex)
            {
                Log.Warn("PCGW: GetInfoAsync failed for " + (game?.Id ?? steamAppId), ex);
                return new PcgwInfo();
            }
            return info;
        }

        private static List<ResolvedPath> Order(List<ResolvedPath> l) =>
            l.OrderByDescending(p => p.Exists).ThenByDescending(p => p.Path.Length > 0).ToList();

        private static string AppIdOf(Game game, string steamAppId)
        {
            string id = !string.IsNullOrWhiteSpace(steamAppId) ? steamAppId.Trim() : game?.SteamAppId?.Trim();
            return !string.IsNullOrEmpty(id) && id.All(char.IsDigit) && id != "0" ? id : null;
        }

        // ---------------------------------------------------------------- page lookup

        private async Task<string> FindTitleAsync(Game game, string appId)
        {
            if (appId != null)
            {
                string t = await LookupAsync("appid:" + appId,
                    () => PcgwApi.FindByAppIdAsync(appId), m => m.FindTitleByAppId(appId)).ConfigureAwait(false);
                if (t != null) return t;
            }
            string name = game?.Name;
            string norm = PcgwNames.Normalize(PcgwNames.CleanForSearch(name));
            if (norm.Length == 0) return null;
            return await LookupAsync("name:" + norm,
                () => PcgwApi.FindByNameAsync(name), m => m.FindTitleByName(name)).ConfigureAwait(false);
        }

        /// <summary>Cache → PCGW API → Ludusavi manifest. Caches definitive answers (found / not found) only.</summary>
        private async Task<string> LookupAsync(string key, Func<Task<PcgwLookup>> api, Func<LudusaviManifest, string> manifest)
        {
            PcgwLookupEntry cached = _cache.GetLookup(key);
            if (cached != null) return cached.Title;

            PcgwLookup r = await api().ConfigureAwait(false);
            if (r.Ok)
            {
                _cache.PutLookup(key, r.Title, "pcgw");
                return r.Title;
            }
            if (await _manifest.EnsureAsync().ConfigureAwait(false))
            {
                string t = manifest(_manifest);
                _cache.PutLookup(key, t, "ludusavi");
                return t;
            }
            return null; // offline: nothing cached, try again next time
        }

        // ---------------------------------------------------------------- page rows

        private async Task<List<PcgwRow>> GetRowsAsync(string title)
        {
            PcgwPageEntry cached = _cache.GetPage(title);
            if (cached != null) return cached.Rows;

            Tuple<string, string> page = await PcgwApi.GetWikitextAsync(title).ConfigureAwait(false);
            if (page != null)
            {
                List<PcgwRow> rows = PcgwWikitext.ExtractRows(page.Item2);
                _cache.PutPage(title, "pcgw", rows);
                return rows;
            }
            if (await _manifest.EnsureAsync().ConfigureAwait(false))
            {
                LudusaviEntry e = _manifest.GetEntry(title);
                if (e != null)
                {
                    _cache.PutPage(title, "ludusavi", e.Rows);
                    return e.Rows;
                }
            }
            return null;
        }
    }
}
