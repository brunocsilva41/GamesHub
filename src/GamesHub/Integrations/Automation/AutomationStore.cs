// OWNER: AUTO agent. Persistence: automation.json (profiles) and automation-state.json (pending restores).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GamesHub
{
    public sealed class AutomationData
    {
        public int Version = 1;
        public AutomationProfile Default = new AutomationProfile();
        public Dictionary<string, AutomationProfile> Games = new Dictionary<string, AutomationProfile>();
    }

    /// <summary>Settings captured right before a game's "Before" actions changed them.</summary>
    public sealed class AutomationSnapshot
    {
        public string GameId = "";
        /// <summary>Creation order (UTC ticks); the oldest snapshot holds the user's real original value.</summary>
        public long Order;
        /// <summary>type (powerPlan/audioDevice/resolution) → value to restore.</summary>
        public Dictionary<string, string> Values = new Dictionary<string, string>();
    }

    public sealed class AutomationStateData
    {
        public List<AutomationSnapshot> Snapshots = new List<AutomationSnapshot>();
    }

    /// <summary>Thread-safe profile store. Everything handed out is a sanitized copy.</summary>
    public sealed class AutomationProfileStore
    {
        private readonly object gate = new object();
        private readonly string file;
        private AutomationData data;

        public AutomationProfileStore(string file)
        {
            this.file = file;
            data = Normalize(Json.Load(file, new AutomationData()));
        }

        public static AutomationData Normalize(AutomationData d)
        {
            var r = new AutomationData { Default = AutomationValidation.Sanitize(d?.Default) };
            if (d?.Games != null)
                foreach (var kv in d.Games)
                {
                    if (string.IsNullOrWhiteSpace(kv.Key)) continue;
                    AutomationProfile p = AutomationValidation.Sanitize(kv.Value);
                    if (!AutomationValidation.IsTrivial(p)) r.Games[kv.Key] = p;
                }
            return r;
        }

        public AutomationProfile GetDefault()
        {
            lock (gate) return AutomationValidation.Clone(data.Default);
        }

        public AutomationProfile Get(string gameId)
        {
            lock (gate)
                return gameId != null && data.Games.TryGetValue(gameId, out AutomationProfile p)
                    ? AutomationValidation.Clone(p) : new AutomationProfile();
        }

        /// <summary>Returns the number of dropped (invalid) actions.</summary>
        public int SetDefault(AutomationProfile profile)
        {
            AutomationProfile p = AutomationValidation.Sanitize(profile, out int dropped);
            lock (gate)
            {
                data.Default = p;
                Persist();
            }
            return dropped;
        }

        public int Set(string gameId, AutomationProfile profile)
        {
            if (string.IsNullOrWhiteSpace(gameId)) throw new ArgumentException("gameId");
            AutomationProfile p = AutomationValidation.Sanitize(profile, out int dropped);
            lock (gate)
            {
                if (AutomationValidation.IsTrivial(p)) data.Games.Remove(gameId);
                else data.Games[gameId] = p;
                Persist();
            }
            return dropped;
        }

        private void Persist() => Json.Save(file, data);
    }

    /// <summary>Pending restores, persisted on every change so a crash can be recovered at next start.</summary>
    public sealed class AutomationStateStore
    {
        private readonly string file;
        private AutomationStateData data;

        public AutomationStateStore(string file)
        {
            this.file = file;
            data = Json.Load(file, new AutomationStateData());
            if (data.Snapshots == null) data.Snapshots = new List<AutomationSnapshot>();
            data.Snapshots = data.Snapshots
                .Where(s => s != null && !string.IsNullOrEmpty(s.GameId) && s.Values != null && s.Values.Count > 0).ToList();
        }

        public List<AutomationSnapshot> All => data.Snapshots;

        public AutomationSnapshot Find(string gameId) => data.Snapshots.FirstOrDefault(s => s.GameId == gameId);

        public void Save()
        {
            try
            {
                if (data.Snapshots.Count == 0) { if (File.Exists(file)) File.Delete(file); }
                else Json.Save(file, data);
            }
            catch (Exception ex) { Log.Error("Automation: could not save " + file, ex); }
        }
    }
}
