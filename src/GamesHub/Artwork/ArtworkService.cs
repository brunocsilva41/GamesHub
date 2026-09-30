using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace GamesHub
{
    /// <summary>
    /// Artwork for the library: Steam CDN art (header/capsule/hero/logo), fuzzy Steam matching for
    /// non-Steam games, optional SteamGridDB, high-quality icon extraction and user custom images.
    /// Resolve() only looks at the disk cache; everything else happens in background work
    /// (see ArtworkService.Pipeline.cs) which raises ArtworkUpdated(gameId) once per batch.
    /// </summary>
    public sealed partial class ArtworkService : IArtworkService, IDisposable
    {
        private const long MaxCustomBytes = 50L * 1024 * 1024;

        private readonly AppSettings _settings;
        private readonly ArtCache _cache;
        private readonly ArtIndex _index;
        private readonly HttpFetcher _http;
        private readonly SteamClient _steam;
        private readonly SteamGridDbClient _sgdb;
        private readonly Lazy<IconExtractor> _icons = new Lazy<IconExtractor>(() => new IconExtractor(), LazyThreadSafetyMode.ExecutionAndPublication);
        private readonly KeyedTasks _tasks = new KeyedTasks();

        public event Action<string> ArtworkUpdated;

        public ArtworkService(AppSettings settings) : this(settings, AppPaths.ArtDir) { }

        /// <summary>Uses a specific cache folder (tests, tools).</summary>
        public ArtworkService(AppSettings settings, string artDir)
        {
            _settings = settings ?? new AppSettings();
            Directory.CreateDirectory(artDir);
            _cache = new ArtCache(artDir);
            _index = new ArtIndex(Path.Combine(artDir, "index.json"));
            _http = new HttpFetcher();
            _steam = new SteamClient(_http);
            // The user's own key wins; otherwise the key embedded in this build (if any).
            _sgdb = new SteamGridDbClient(_http, () =>
                string.IsNullOrWhiteSpace(_settings.SteamGridDbKey) ? BuildSecrets.SteamGridDbKey : _settings.SteamGridDbKey);
            _retryTimer = new Timer(_ => RetryDeferred(), null, Timeout.Infinite, Timeout.Infinite);
        }

        public ArtCache Cache => _cache;

        // ------------------------------------------------------------ resolve

        public Artwork Resolve(Game game)
        {
            if (game == null || string.IsNullOrEmpty(game.Id)) return new Artwork();
            GameRef g = GameRef.From(game);
            string appId = EffectiveAppId(g);
            TrackAppUser(appId, g.Id);
            Artwork art = _cache.Resolve(g.Id, appId);
            if (NeedsWork(g, appId, art)) Schedule(g);
            return art;
        }

        /// <summary>
        /// Steam app id used for this game's art: game.SteamAppId when set, else the cached fuzzy-match
        /// result, else "". Never blocks on the network (the match is found by the background work that
        /// Resolve() schedules; ArtworkUpdated fires when it lands).
        /// </summary>
        public string GetMatchedSteamAppId(Game game)
            => game == null || string.IsNullOrEmpty(game.Id) ? "" : EffectiveAppId(GameRef.From(game));

        /// <summary>The user/source app id, else the cached fuzzy-match result, else "".</summary>
        private string EffectiveAppId(GameRef g)
        {
            if (g.SteamAppId == ArtKind.NotOnSteam) return "";
            if (ArtKind.IsAppId(g.SteamAppId)) return g.SteamAppId;
            return _index.TryGetMatch(MatchKey(g.Name), out string id) ? id : "";
        }

        private static string MatchKey(string name) => NameMatcher.Normalize(name);

        private bool NeedsWork(GameRef g, string appId, Artwork art)
        {
            string gk = ArtCache.GameKey(g.Id);
            if (art.Icon == null && g.HasIconSource && !_index.IsNegative(NegIcon(gk))) return true;
            if (!_settings.AutoArtwork) return false;
            bool knownApp = ArtKind.IsAppId(g.SteamAppId) && g.SteamAppId != ArtKind.NotOnSteam;
            if (!knownApp && NameMatcher.IsLikelyNonGame(g.Name)) return false; // launchers, tools…
            bool steamSearchable = !knownApp && g.SteamAppId != ArtKind.NotOnSteam && !NameMatcher.IsNonSteamPlatform(g.Platform);
            if (steamSearchable && appId.Length == 0 && !_index.TryGetMatch(MatchKey(g.Name), out _)) return true;
            foreach (string kind in ArtKind.Remote.Where(k => ArtKind.Get(art, k) == null))
            {
                bool steamDone = appId.Length == 0 || _index.IsNegative(NegSteam(appId, kind));
                if (!steamDone) return true;
                if (_sgdb.Enabled && !_index.IsNegative(NegSgdb(gk, kind))) return true;
            }
            return false;
        }

        private static string NegIcon(string gameKey) => "icon:" + gameKey;
        private static string NegSteam(string appId, string kind) => "steam:" + appId + ":" + kind;
        private static string NegSgdb(string gameKey, string kind) => "sgdb:" + gameKey + ":" + kind;

        // ------------------------------------------------------------ custom images

        public OpResult SetCustomImage(Game game, string kind, string sourceFile)
        {
            if (game == null || string.IsNullOrEmpty(game.Id)) return OpResult.Fail("Jogo não encontrado.");
            if (!ArtKind.IsValid(kind)) return OpResult.Fail("Tipo de imagem inválido.");
            if (string.IsNullOrEmpty(sourceFile) || !File.Exists(sourceFile)) return OpResult.Fail("Arquivo de imagem não encontrado.");
            try
            {
                if (new FileInfo(sourceFile).Length > MaxCustomBytes) return OpResult.Fail("A imagem é grande demais (máximo de 50 MB).");
                byte[] data = File.ReadAllBytes(sourceFile);
                ImageFiles.SaveNormalized(data, _cache.CustomBase(game.Id, kind), ArtKind.NeedsAlpha(kind));
            }
            catch (Exception ex) when (ex is ArgumentException || ex is ExternalException || ex is OutOfMemoryException)
            {
                Log.Info("Art: rejected custom " + kind + " for " + game.Id + " (not a supported image): " + sourceFile);
                return OpResult.Fail("O arquivo não é uma imagem válida. Use PNG, JPG, BMP ou GIF.");
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Log.Warn("Art: cannot store custom " + kind + " for " + game.Id, ex);
                return OpResult.Fail("Não foi possível salvar a imagem.");
            }
            Log.Info("Art: custom " + kind + " set for " + game.Id);
            Raise(game.Id);
            return OpResult.Success("Imagem atualizada.", game.Id);
        }

        public OpResult ClearCustomImage(Game game, string kind)
        {
            if (game == null || string.IsNullOrEmpty(game.Id)) return OpResult.Fail("Jogo não encontrado.");
            if (!ArtKind.IsValid(kind)) return OpResult.Fail("Tipo de imagem inválido.");
            if (!ArtCache.DeleteAll(_cache.CustomBase(game.Id, kind)))
                return OpResult.Success("Este jogo não tinha imagem personalizada.", game.Id);
            Raise(game.Id);
            Resolve(game);   // the default art may still need downloading
            return OpResult.Success("Imagem personalizada removida.", game.Id);
        }

        // ------------------------------------------------------------ refresh

        public void Refresh(Game game)
        {
            if (game == null || string.IsNullOrEmpty(game.Id)) return;
            GameRef g = GameRef.From(game);
            string gk = ArtCache.GameKey(g.Id);
            string appId = EffectiveAppId(g);
            string key = MatchKey(g.Name);

            DeleteFile(_cache.IconFile(g.Id));
            foreach (string kind in ArtKind.Remote)
            {
                ArtCache.DeleteAll(_cache.SgdbBase(g.Id, kind));
                if (appId.Length > 0) ArtCache.DeleteAll(_cache.SteamBase(appId, kind));
            }
            _index.ClearNegative(NegIcon(gk));
            _index.ClearNegative("sgdb:" + gk + ":");
            if (appId.Length > 0)
            {
                _index.ClearNegative("steam:" + appId + ":");
                _index.RemoveMatch(SgdbKey(appId, key));
            }
            if (!ArtKind.IsAppId(g.SteamAppId))
            {
                _index.RemoveMatch(key);
                _index.RemoveMatch(SgdbKey("", key));
            }
            Log.Info("Art: refresh requested for " + g.Id);
            Raise(g.Id);
            Schedule(g);
        }

        private static void DeleteFile(string path)
        {
            if (!File.Exists(path)) return;
            try { File.Delete(path); }
            catch (Exception ex) when (ExpectedErrors.IsFileSystem(ex)) { Log.Warn("Art: cannot delete " + path, ex); }
        }

        // ------------------------------------------------------------ manual search

        public async Task<List<SteamSearchResult>> SearchSteamAsync(string query)
        {
            var results = new List<SteamSearchResult>();
            string q = (query ?? "").Trim();
            if (q.Length == 0) return results;
            if (ArtKind.IsAppId(q))
            {
                AppDetails d = await _steam.AppDetailsAsync(q).ConfigureAwait(false);
                if (d.Name.Length > 0)
                    results.Add(new SteamSearchResult { AppId = q, Name = d.Name, IconUrl = SteamEndpoints.SmallCapsule(q) });
                return results;
            }
            SearchOutcome o = await _steam.StoreSearchAsync(q).ConfigureAwait(false);
            if (o.Items.Count == 0 && o.Status != FetchStatus.Offline)
                o = await _steam.CommunitySearchAsync(q).ConfigureAwait(false);
            foreach (MatchCandidate c in o.Items.GroupBy(c => c.AppId).Select(grp => grp.First()).Take(10))
                results.Add(new SteamSearchResult { AppId = c.AppId, Name = c.Name, IconUrl = c.IconUrl ?? "" });
            return results;
        }

        // ------------------------------------------------------------ legacy import

        public void ImportLegacy(string legacyHubDir)
        {
            if (string.IsNullOrEmpty(legacyHubDir) || !Directory.Exists(legacyHubDir)) return;
            int covers = 0, matches = 0;
            string coverDir = Path.Combine(legacyHubDir, "covers");
            if (Directory.Exists(coverDir))
            {
                foreach (string file in Directory.GetFiles(coverDir, "*.jpg"))
                {
                    string appId = Path.GetFileNameWithoutExtension(file);
                    if (!ArtKind.IsAppId(appId) || _cache.FindSteam(appId, ArtKind.Header) != null) continue;
                    try
                    {
                        byte[] data = File.ReadAllBytes(file);
                        if (!ImageFiles.IsValidImage(data, out string ext)) continue;
                        ImageFiles.SaveBytes(_cache.SteamBase(appId, ArtKind.Header), ext, data);
                        covers++;
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        Log.Warn("Art: cannot import legacy cover " + file, ex);
                    }
                }
            }
            string searchFile = Path.Combine(legacyHubDir, "searchcache.json");
            var legacy = Json.Load(searchFile, new Dictionary<string, object>());
            foreach (KeyValuePair<string, object> kv in legacy)
            {
                string appId = Convert.ToString(kv.Value);
                if (ArtKind.IsAppId(appId) && _index.AddMatchIfMissing(MatchKey(kv.Key), appId)) matches++;
            }
            if (covers + matches > 0) Log.Info("Art: imported " + covers + " legacy covers and " + matches + " search entries");
        }

        // ------------------------------------------------------------ plumbing

        private void Raise(string gameId)
        {
            Action<string> h = ArtworkUpdated;
            if (h == null) return;
            try { h(gameId); }
            // Resilience boundary: raises an event to arbitrary subscribers, often from a timer thread.
            catch (Exception ex) { Log.Warn("Art: ArtworkUpdated handler failed for " + gameId, ex); }
        }

        public void Dispose()
        {
            _retryTimer.Dispose();
            lock (_jobsGate) _raiseTimer?.Dispose();
            _index.Dispose();
            _http.Dispose();
            if (_icons.IsValueCreated) _icons.Value.Dispose();
        }

        /// <summary>Immutable snapshot of the fields the background work needs.</summary>
        private sealed class GameRef
        {
            public string Id, Name, Platform, SteamAppId, Exe, FilePath;

            public static GameRef From(Game g) => new GameRef
            {
                Id = g.Id, Name = g.Name ?? "", Platform = g.Platform ?? "", SteamAppId = (g.SteamAppId ?? "").Trim(),
                Exe = g.Exe ?? "", FilePath = g.FilePath ?? "",
            };

            public bool HasIconSource => File.Exists(Exe) || File.Exists(FilePath);
        }
    }
}
