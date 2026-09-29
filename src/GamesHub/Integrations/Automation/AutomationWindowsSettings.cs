// OWNER: AUTO agent. Real IAutomationSettingsAccess backed by the Windows APIs.
using System;

namespace GamesHub
{
    public sealed class AutomationWindowsSettings : IAutomationSettingsAccess
    {
        public string Get(string type)
        {
            switch (type)
            {
                case AutomationTypes.PowerPlan: return AutomationPower.GetActive().ToString("D");
                case AutomationTypes.AudioDevice: return AutomationAudio.GetDefaultId();
                case AutomationTypes.Resolution: return AutomationDisplay.Current().ToString();
                default: throw new ArgumentException("unknown setting " + type);
            }
        }

        public void Set(string type, string value)
        {
            switch (type)
            {
                case AutomationTypes.PowerPlan:
                    if (!Guid.TryParse(value, out Guid g)) throw new ArgumentException("invalid power plan id " + value);
                    AutomationPower.SetActive(g);
                    break;
                case AutomationTypes.AudioDevice:
                    if (string.IsNullOrEmpty(value)) throw new ArgumentException("empty audio device id");
                    if (AutomationAudio.GetDefaultId() != value) AutomationAudio.SetDefault(value);
                    break;
                case AutomationTypes.Resolution:
                    if (!ResolutionSpec.TryParse(value, out ResolutionSpec spec)) throw new ArgumentException("invalid resolution " + value);
                    AutomationDisplay.Apply(spec);
                    break;
                default: throw new ArgumentException("unknown setting " + type);
            }
        }
    }
}
