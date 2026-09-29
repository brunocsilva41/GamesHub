// OWNER: STEAMDATA agent. Skeleton by lead — replace entirely.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GamesHub
{
    public sealed class SteamLocalData : ISteamLocalData
    {
        public Dictionary<string, SteamLocalStats> Load() => new Dictionary<string, SteamLocalStats>();
        public string ValidateUri(string appId) => "steam://validate/" + appId;
        public string UninstallUri(string appId) => "steam://uninstall/" + appId;
    }
}
