// OWNER: ART agent. Skeleton created by lead — replace entirely.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GamesHub
{
    public sealed class ArtworkService : IArtworkService
    {
        private readonly AppSettings _settings;
        public ArtworkService(AppSettings settings) { _settings = settings; }
        public event Action<string> ArtworkUpdated;
        public Artwork Resolve(Game game) => new Artwork();
        public OpResult SetCustomImage(Game game, string kind, string sourceFile) => OpResult.Fail("Não implementado");
        public OpResult ClearCustomImage(Game game, string kind) => OpResult.Fail("Não implementado");
        public void Refresh(Game game) { }
        public Task<List<SteamSearchResult>> SearchSteamAsync(string query) => Task.FromResult(new List<SteamSearchResult>());
        public void ImportLegacy(string legacyHubDir) { }
        private void Raise(string id) => ArtworkUpdated?.Invoke(id);
    }
}
