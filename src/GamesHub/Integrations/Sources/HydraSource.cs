// OWNER: SOURCES agent. Skeleton by lead — replace entirely.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GamesHub
{
    public sealed class HydraSource : IExtraSource
    {
        public string Name => "hydra";
        public List<Game> Scan() => new List<Game>();
    }
}
