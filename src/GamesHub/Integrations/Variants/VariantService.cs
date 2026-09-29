// OWNER: VARIANTS agent. Skeleton by lead — replace entirely.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GamesHub
{
    public sealed class VariantService : IVariantService
    {
        public List<VariantGroup> Suggest(List<Game> games) => new List<VariantGroup>();
        public List<VariantGroup> GetGroups() => new List<VariantGroup>();
        public OpResult Group(List<string> memberIds, string primaryId) => OpResult.Fail("Não implementado");
        public OpResult Ungroup(string groupId) => OpResult.Fail("Não implementado");
        public OpResult DismissSuggestion(List<string> memberIds) => OpResult.Fail("Não implementado");
        public OpResult SetLabel(string memberId, string label) => OpResult.Fail("Não implementado");
        public OpResult SetPrimary(string groupId, string primaryId) => OpResult.Fail("Não implementado");
        public List<Game> Apply(List<Game> games) => games;
    }
}
