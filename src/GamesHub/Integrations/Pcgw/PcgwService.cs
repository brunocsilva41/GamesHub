// OWNER: PCGW agent. Skeleton by lead — replace entirely.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GamesHub
{
    public sealed class PcgwService : IPcgwService
    {
        private readonly AppSettings _settings;
        public PcgwService(AppSettings settings) { _settings = settings; }
        public string PageUrl(Game game, string steamAppId) => "";
        public Task<PcgwInfo> GetInfoAsync(Game game, string steamAppId) => Task.FromResult(new PcgwInfo());
    }
}
