using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GamesHub
{
    public sealed class AutomationService : IAutomationService
    {
        private readonly AutomationProfileStore profiles;
        private readonly AutomationStateStore state;
        private readonly IAutomationSettingsAccess settings;
        private readonly Dictionary<string, IActionExecutor> executors;
        private readonly TimeSpan baseBudget;
        private readonly SemaphoreSlim runGate = new SemaphoreSlim(1, 1);
        private readonly object stateGate = new object();

        public AutomationService() : this(AppPaths.DataDir, new AutomationWindowsSettings(), null, TimeSpan.FromSeconds(10)) { }

        /// <summary>Test/composition ctor. <paramref name="executors"/> null = the real ones.
        /// <paramref name="baseBudget"/> is the time allowed for a list on top of the user's waits.</summary>
        public AutomationService(string dataDir, IAutomationSettingsAccess settings, IEnumerable<IActionExecutor> executors, TimeSpan baseBudget)
        {
            this.settings = settings;
            this.baseBudget = baseBudget;
            profiles = new AutomationProfileStore(Path.Combine(dataDir, "automation.json"));
            state = new AutomationStateStore(Path.Combine(dataDir, "automation-state.json"));
            this.executors = (executors ?? DefaultExecutors(settings)).ToDictionary(e => e.Type, StringComparer.Ordinal);
        }

        public static List<IActionExecutor> DefaultExecutors(IAutomationSettingsAccess settings) => new List<IActionExecutor>
        {
            new RunActionExecutor(), new CloseActionExecutor(), new WaitActionExecutor(),
            new SettingActionExecutor(AutomationTypes.PowerPlan, settings),
            new SettingActionExecutor(AutomationTypes.AudioDevice, settings),
            new SettingActionExecutor(AutomationTypes.Resolution, settings),
        };

        // ------------------------------------------------------------------ profiles

        public AutomationProfile GetProfile(string gameId) => profiles.Get(gameId);
        public AutomationProfile GetDefaultProfile() => profiles.GetDefault();

        public OpResult SaveProfile(string gameId, AutomationProfile profile)
        {
            if (string.IsNullOrWhiteSpace(gameId)) return OpResult.Fail("Jogo inválido.");
            try { return Saved(profiles.Set(gameId, profile), gameId); }
            catch (Exception ex)
            {
                Log.Error("Automation: save profile failed for " + gameId, ex);
                return OpResult.Fail("Não foi possível salvar a automação.");
            }
        }

        public OpResult SaveDefaultProfile(AutomationProfile profile)
        {
            try { return Saved(profiles.SetDefault(profile), null); }
            catch (Exception ex)
            {
                Log.Error("Automation: save default profile failed", ex);
                return OpResult.Fail("Não foi possível salvar a automação padrão.");
            }
        }

        private static OpResult Saved(int dropped, string gameId) => OpResult.Success(dropped == 0
            ? "Automação salva."
            : "Automação salva. " + dropped + (dropped == 1 ? " ação inválida foi ignorada." : " ações inválidas foram ignoradas."), gameId);

        // ------------------------------------------------------------------ run

        public async Task RunBeforeAsync(Game game)
        {
            if (game == null || string.IsNullOrEmpty(game.Id)) return;
            await runGate.WaitAsync().ConfigureAwait(false);
            try
            {
                List<AutomationAction> plan = AutomationValidation.Compose(profiles.GetDefault(), profiles.Get(game.Id), true);
                if (plan.Count == 0) return;
                TakeSnapshot(game.Id, plan);
                await RunListAsync(game, "before", plan).ConfigureAwait(false);
            }
            catch (Exception ex) { Log.Error("Automation: before failed for " + game.Id, ex); }
            finally { runGate.Release(); }
        }

        public async Task RunAfterAsync(Game game)
        {
            if (game == null || string.IsNullOrEmpty(game.Id)) return;
            await runGate.WaitAsync().ConfigureAwait(false);
            try
            {
                await Task.Run(() => RestoreSnapshot(game.Id)).ConfigureAwait(false);
                List<AutomationAction> plan = AutomationValidation.Compose(profiles.GetDefault(), profiles.Get(game.Id), false);
                if (plan.Count > 0) await RunListAsync(game, "after", plan).ConfigureAwait(false);
            }
            catch (Exception ex) { Log.Error("Automation: after failed for " + game.Id, ex); }
            finally { runGate.Release(); }
        }

        /// <summary>Runs sequentially; a failing/slow action never aborts the rest. The whole list is bounded
        /// by baseBudget + the user's waits; actions past the deadline are skipped.</summary>
        private async Task RunListAsync(Game game, string phase, List<AutomationAction> plan)
        {
            TimeSpan budget = baseBudget + TimeSpan.FromSeconds(plan.Where(a => a.Type == AutomationTypes.Wait).Sum(a => a.Seconds));
            DateTime deadline = DateTime.UtcNow + budget;
            using (var cts = new CancellationTokenSource(budget))
            {
                foreach (AutomationAction a in plan)
                {
                    string what = "Automation[" + game.Id + "] " + phase + " " + a.Type + " '" + (a.Type == AutomationTypes.Wait ? a.Seconds + "s" : a.Target) + "'";
                    TimeSpan left = deadline - DateTime.UtcNow;
                    if (left <= TimeSpan.Zero) { Log.Warn(what + ": skipped (time budget exhausted)"); continue; }
                    if (!executors.TryGetValue(a.Type, out IActionExecutor exec)) { Log.Warn(what + ": no executor"); continue; }
                    try
                    {
                        Task<string> t = exec.ExecuteAsync(a, cts.Token);
                        Task done = await Task.WhenAny(t, Task.Delay(left)).ConfigureAwait(false);
                        if (done != t)
                        {
                            Log.Warn(what + ": timed out");
                            ObserveLater(t, what);
                            continue;
                        }
                        Log.Info(what + ": " + await t.ConfigureAwait(false));
                    }
                    catch (Exception ex) { Log.Warn(what + ": failed", ex); }
                }
            }
        }

        private static void ObserveLater(Task t, string what)
            => t.ContinueWith(x => Log.Warn(what + ": finished late with error", x.Exception?.GetBaseException()),
                              TaskContinuationOptions.OnlyOnFaulted);

        // ------------------------------------------------------------------ snapshot / restore

        private void TakeSnapshot(string gameId, List<AutomationAction> plan)
        {
            var types = plan.Select(a => a.Type).Where(AutomationTypes.IsSetting).Distinct().ToList();
            if (types.Count == 0) return;
            lock (stateGate)
            {
                AutomationSnapshot snap = state.Find(gameId);
                bool isNew = snap == null;
                if (isNew) snap = new AutomationSnapshot { GameId = gameId, Order = DateTime.UtcNow.Ticks };
                bool changed = false;
                foreach (string t in types)
                {
                    if (snap.Values.ContainsKey(t)) continue;   // keep the value from the first (still pending) launch
                    try
                    {
                        string v = settings.Get(t);
                        if (!string.IsNullOrEmpty(v)) { snap.Values[t] = v; changed = true; }
                    }
                    catch (Exception ex) { Log.Warn("Automation[" + gameId + "]: could not snapshot " + t, ex); }
                }
                if (!changed) return;
                if (isNew) state.All.Add(snap);
                state.Save();
                Log.Info("Automation[" + gameId + "]: snapshot " + string.Join(", ", snap.Values.Select(kv => kv.Key + "=" + kv.Value)));
            }
        }

        /// <summary>Restores what this game's "Before" changed. If another game that is still running also
        /// changed the same setting, the restore is deferred to it (and the oldest original value is kept).</summary>
        private void RestoreSnapshot(string gameId)
        {
            lock (stateGate)
            {
                AutomationSnapshot snap = state.Find(gameId);
                if (snap == null) return;
                state.All.Remove(snap);
                foreach (string type in AutomationTypes.Settings)
                {
                    if (!snap.Values.TryGetValue(type, out string value)) continue;
                    AutomationSnapshot other = state.All.Where(s => s.Values.ContainsKey(type)).OrderBy(s => s.Order).FirstOrDefault();
                    if (other != null)
                    {
                        if (snap.Order < other.Order) other.Values[type] = value;
                        Log.Info("Automation[" + gameId + "]: " + type + " restore deferred to " + other.GameId);
                        continue;
                    }
                    Apply(gameId, type, value);
                }
                state.Save();
            }
        }

        /// <summary>Call once at startup (before any game is launched): restores settings left changed by a
        /// previous session that crashed or was closed while a game with automations was running.
        /// Returns the number of settings restored.</summary>
        public int RestorePendingOnStartup()
        {
            lock (stateGate)
            {
                if (state.All.Count == 0) return 0;
                int n = 0;
                List<AutomationSnapshot> ordered = state.All.OrderBy(s => s.Order).ToList();
                foreach (string type in AutomationTypes.Settings)
                {
                    AutomationSnapshot oldest = ordered.FirstOrDefault(s => s.Values.ContainsKey(type));
                    if (oldest != null && Apply("startup", type, oldest.Values[type])) n++;
                }
                state.All.Clear();
                state.Save();
                Log.Info("Automation: startup restore applied " + n + " setting(s)");
                return n;
            }
        }

        private bool Apply(string who, string type, string value)
        {
            try
            {
                string cur = null;
                try { cur = settings.Get(type); }
                catch (Exception ex) { Log.Warn("Automation[" + who + "]: could not read " + type + " before restore", ex); }
                if (string.Equals(cur, value, StringComparison.OrdinalIgnoreCase))
                {
                    Log.Info("Automation[" + who + "]: " + type + " already " + value);
                    return true;
                }
                settings.Set(type, value);
                Log.Info("Automation[" + who + "]: restored " + type + " = " + value);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("Automation[" + who + "]: restore " + type + " = " + value + " failed", ex);
                return false;
            }
        }

        // ------------------------------------------------------------------ option lists

        public List<NamedOption> ListPowerPlans() => SafeList("power plans", AutomationPower.List);
        public List<NamedOption> ListAudioDevices() => SafeList("audio devices", AutomationAudio.List);
        public List<NamedOption> ListResolutions() => SafeList("resolutions", AutomationDisplay.List);

        private static List<NamedOption> SafeList(string what, Func<List<NamedOption>> f)
        {
            try { return f(); }
            catch (Exception ex)
            {
                Log.Warn("Automation: listing " + what + " failed", ex);
                return new List<NamedOption>();
            }
        }
    }
}
