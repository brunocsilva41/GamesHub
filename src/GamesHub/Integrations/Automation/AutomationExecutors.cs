// OWNER: AUTO agent. One executor per action type (run, close, wait, and the setting changers).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GamesHub
{
    /// <summary>Executes one kind of action. Returns a short English log line; throws on failure.</summary>
    public interface IActionExecutor
    {
        string Type { get; }
        Task<string> ExecuteAsync(AutomationAction action, CancellationToken ct);
    }

    /// <summary>Reads/writes the system settings that "Before" may change and "After" restores.
    /// Keys are AutomationTypes.PowerPlan / AudioDevice / Resolution; values are the action Target format.</summary>
    public interface IAutomationSettingsAccess
    {
        string Get(string type);
        void Set(string type, string value);
    }

    /// <summary>Processes that must never be closed by an automation.</summary>
    public static class AutomationProcessRules
    {
        private static readonly HashSet<string> Protected = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "system", "idle", "registry", "secure system", "memory compression", "smss", "csrss", "wininit",
            "winlogon", "services", "lsass", "lsaiso", "svchost", "dwm", "explorer", "fontdrvhost", "sihost",
            "taskhostw", "ctfmon", "conhost", "spoolsv", "audiodg", "dllhost", "wmiprvse", "wudfhost",
            "runtimebroker", "startmenuexperiencehost", "shellexperiencehost", "searchhost", "searchui",
            "searchapp", "textinputhost", "lockapp", "logonui", "userinit", "msmpeng", "securityhealthservice",
            "securityhealthsystray", "nissrv", "smartscreen", "taskmgr", "gameshub", "gameslounge",
        };

        /// <summary>"C:\x\Foo.EXE " → "Foo". Path parts are removed.</summary>
        public static string NormalizeName(string name)
        {
            string n = (name ?? "").Trim().Trim('"');
            int slash = n.LastIndexOfAny(new[] { '\\', '/' });
            if (slash >= 0) n = n.Substring(slash + 1);
            if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) n = n.Substring(0, n.Length - 4);
            return n.Trim();
        }

        public static bool IsProtected(string name)
        {
            string n = NormalizeName(name);
            if (n.Length == 0) return true;
            if (Protected.Contains(n)) return true;
            try
            {
                using (Process me = Process.GetCurrentProcess())
                    if (string.Equals(me.ProcessName, n, StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch (Exception ex) { Log.Warn("Automation: could not read own process name", ex); }
            return false;
        }
    }

    public sealed class RunActionExecutor : IActionExecutor
    {
        public string Type => AutomationTypes.Run;

        public Task<string> ExecuteAsync(AutomationAction action, CancellationToken ct)
        {
            string target = Environment.ExpandEnvironmentVariables(action.Target ?? "");
            var psi = new ProcessStartInfo(target, Environment.ExpandEnvironmentVariables(action.Args ?? ""))
            { UseShellExecute = true };
            try
            {
                if (File.Exists(target)) psi.WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(target));
            }
            catch (Exception ex) { Log.Warn("Automation run: bad path " + target, ex); }
            using (Process.Start(psi)) { }
            return Task.FromResult("started " + target);
        }
    }

    /// <summary>Graceful close only (CloseMainWindow), never Kill. Only processes in the user's session.</summary>
    public sealed class CloseActionExecutor : IActionExecutor
    {
        private readonly TimeSpan grace;
        public CloseActionExecutor() : this(TimeSpan.FromSeconds(5)) { }
        public CloseActionExecutor(TimeSpan grace) { this.grace = grace; }

        public string Type => AutomationTypes.Close;

        public Task<string> ExecuteAsync(AutomationAction action, CancellationToken ct) => Task.Run(() =>
        {
            string name = AutomationProcessRules.NormalizeName(action.Target);
            if (AutomationProcessRules.IsProtected(name))
                throw new InvalidOperationException("refused to close protected process '" + name + "'");
            int session;
            using (Process me = Process.GetCurrentProcess()) session = me.SessionId;
            Process[] procs = Process.GetProcessesByName(name).Where(p => SafeSession(p) == session).ToArray();
            try
            {
                if (procs.Length == 0) return "no running process '" + name + "'";
                int noWindow = 0;
                foreach (Process p in procs)
                {
                    try { if (!p.CloseMainWindow()) noWindow++; }
                    catch (Exception ex) { Log.Warn("Automation close: CloseMainWindow failed for " + name, ex); }
                }
                DateTime deadline = DateTime.UtcNow + grace;
                foreach (Process p in procs)
                {
                    int left = (int)Math.Max(0, (deadline - DateTime.UtcNow).TotalMilliseconds);
                    try { p.WaitForExit(left); }
                    catch (Exception ex) { Log.Warn("Automation close: wait failed for " + name, ex); }
                }
                int still = procs.Count(StillRunning);
                if (still > 0)
                    throw new TimeoutException(still + " of " + procs.Length + " '" + name + "' process(es) did not close"
                                               + (noWindow > 0 ? " (" + noWindow + " without a window)" : ""));
                return "closed " + procs.Length + " '" + name + "' process(es)";
            }
            finally { foreach (Process p in procs) p.Dispose(); }
        }, ct);

        private static int SafeSession(Process p)
        {
            try { return p.SessionId; }
            catch (Exception ex) { Log.Warn("Automation close: cannot read session of pid " + p.Id, ex); return -1; }
        }

        private static bool StillRunning(Process p)
        {
            try { return !p.HasExited; }
            catch (Exception ex) { Log.Warn("Automation close: cannot query pid " + p.Id, ex); return false; }
        }
    }

    public sealed class WaitActionExecutor : IActionExecutor
    {
        public string Type => AutomationTypes.Wait;

        public async Task<string> ExecuteAsync(AutomationAction action, CancellationToken ct)
        {
            int s = Math.Max(0, Math.Min(action.Seconds, AutomationValidation.MaxWaitSeconds));
            await Task.Delay(TimeSpan.FromSeconds(s), ct).ConfigureAwait(false);
            return "waited " + s + "s";
        }
    }

    /// <summary>powerPlan / audioDevice / resolution: delegates to the settings access.</summary>
    public sealed class SettingActionExecutor : IActionExecutor
    {
        private readonly IAutomationSettingsAccess access;
        public SettingActionExecutor(string type, IAutomationSettingsAccess access) { Type = type; this.access = access; }

        public string Type { get; }

        public Task<string> ExecuteAsync(AutomationAction action, CancellationToken ct) => Task.Run(() =>
        {
            access.Set(Type, action.Target);
            return "set " + Type + " = " + action.Target;
        }, ct);
    }
}
