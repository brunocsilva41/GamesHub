// OWNER: AUTO agent. Skeleton by lead — replace entirely.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GamesHub
{
    public sealed class AutomationService : IAutomationService
    {
        public AutomationProfile GetProfile(string gameId) => new AutomationProfile();
        public OpResult SaveProfile(string gameId, AutomationProfile profile) => OpResult.Fail("Não implementado");
        public AutomationProfile GetDefaultProfile() => new AutomationProfile();
        public OpResult SaveDefaultProfile(AutomationProfile profile) => OpResult.Fail("Não implementado");
        public Task RunBeforeAsync(Game game) => Task.CompletedTask;
        public Task RunAfterAsync(Game game) => Task.CompletedTask;
        public List<NamedOption> ListPowerPlans() => new List<NamedOption>();
        public List<NamedOption> ListAudioDevices() => new List<NamedOption>();
        public List<NamedOption> ListResolutions() => new List<NamedOption>();
    }
}
