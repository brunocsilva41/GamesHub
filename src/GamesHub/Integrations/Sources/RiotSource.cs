// OWNER: SOURCES agent. Skeleton by lead — replace entirely.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GamesHub
{
    public sealed class RiotSource : IExtraSource
    {
        public string Name => "riot";
        public List<Game> Scan() => new List<Game>();
    }
}
