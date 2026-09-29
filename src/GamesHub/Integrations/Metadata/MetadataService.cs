// OWNER: META agent. Skeleton by lead — replace entirely.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GamesHub
{
    public sealed class MetadataService : IMetadataService
    {
        private readonly AppSettings _settings;
        public MetadataService(AppSettings settings) { _settings = settings; }
        public event Action<string> MetadataUpdated;
        public GameInfo GetCached(string appId) => null;
        public Task<GameInfo> FetchAsync(string appId) => Task.FromResult<GameInfo>(null);
        public void Prefetch(IEnumerable<string> appIds) { }
        private void Raise(string id) => MetadataUpdated?.Invoke(id);
    }
}
