// "run" actions start arbitrary programs, and profiles arrive from the web page (saveAutomation). The page is
// not trusted to decide that on its own: every new or changed "run" action needs a native confirmation first.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace GamesHub
{
    public sealed class AutomationApprovalData
    {
        public int Version = 1;
        /// <summary>Hashes (AutomationRunApprovals.Hash) of "run" actions the user confirmed.</summary>
        public List<string> Runs = new List<string>();
    }

    /// <summary>Persistent set of "run" actions (program + arguments) the user already confirmed, so an
    /// unchanged action is never asked about twice. Thread-safe.</summary>
    public sealed class AutomationRunApprovals
    {
        private readonly object gate = new object();
        private readonly string file;
        private readonly HashSet<string> approved;

        public AutomationRunApprovals(string file)
        {
            this.file = file;
            AutomationApprovalData d = Json.Load(file, new AutomationApprovalData());
            approved = new HashSet<string>((d.Runs ?? new List<string>()).Where(h => !string.IsNullOrEmpty(h)), StringComparer.Ordinal);
        }

        /// <summary>SHA-256 (hex) of the canonical "run" action: target + arguments, exactly as they will run.</summary>
        public static string Hash(AutomationAction a)
        {
            string canonical = AutomationTypes.Run + "\n" + (a?.Target ?? "").Trim() + "\n" + (a?.Args ?? "").Trim();
            using (SHA256 sha = SHA256.Create())
            {
                byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(canonical));
                var sb = new StringBuilder(h.Length * 2);
                foreach (byte b in h) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        public bool IsApproved(AutomationAction a)
        {
            string h = Hash(a);
            lock (gate) return approved.Contains(h);
        }

        public void Approve(IEnumerable<AutomationAction> runs)
        {
            lock (gate)
            {
                bool changed = false;
                foreach (AutomationAction a in runs ?? Enumerable.Empty<AutomationAction>()) changed |= approved.Add(Hash(a));
                if (!changed) return;
                try { Json.Save(file, new AutomationApprovalData { Runs = approved.OrderBy(h => h, StringComparer.Ordinal).ToList() }); }
                catch (Exception ex) when (ExpectedErrors.IsFileOrJson(ex))
                {
                    // Still approved for this session; the user is simply asked again after a restart.
                    Log.Warn("Automation: could not save approvals", ex);
                }
            }
        }
    }

    /// <summary>Decision logic around saving a profile (the prompt itself is injected, so this is testable).</summary>
    public static class AutomationRunGate
    {
        /// <summary>Enabled or disabled "run" actions of <paramref name="incoming"/> that are neither already in
        /// the stored profile (<paramref name="current"/>) nor approved before. Distinct, in profile order.</summary>
        public static List<AutomationAction> Unapproved(AutomationProfile incoming, AutomationProfile current, Func<AutomationAction, bool> isApproved)
        {
            var known = new HashSet<string>(Runs(current).Select(AutomationRunApprovals.Hash), StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var r = new List<AutomationAction>();
            foreach (AutomationAction a in Runs(incoming))
            {
                string h = AutomationRunApprovals.Hash(a);
                if (known.Contains(h) || !seen.Add(h) || (isApproved != null && isApproved(a))) continue;
                r.Add(a);
            }
            return r;
        }

        /// <summary>Sanitizes <paramref name="incoming"/> and asks <paramref name="confirm"/> about its new "run"
        /// actions (one prompt for all of them). Confirmed → they are remembered and kept. Refused (or no prompt
        /// available) → they are removed from the returned profile; everything else is still saved.</summary>
        public static AutomationProfile Decide(AutomationProfile incoming, AutomationProfile current, AutomationRunApprovals approvals,
                                               Func<IList<AutomationAction>, bool> confirm, out int refused)
        {
            refused = 0;
            AutomationProfile clean = AutomationValidation.Sanitize(incoming);
            List<AutomationAction> pending = Unapproved(clean, current, approvals == null ? (Func<AutomationAction, bool>)null : approvals.IsApproved);
            if (pending.Count == 0) return clean;
            if (confirm != null && confirm(pending))
            {
                approvals?.Approve(pending);
                return clean;
            }
            var drop = new HashSet<string>(pending.Select(AutomationRunApprovals.Hash), StringComparer.Ordinal);
            int before = clean.Before.Count + clean.After.Count;
            clean.Before = clean.Before.Where(a => !IsDropped(a, drop)).ToList();
            clean.After = clean.After.Where(a => !IsDropped(a, drop)).ToList();
            refused = before - clean.Before.Count - clean.After.Count;
            return clean;
        }

        /// <summary>pt-BR text for the native prompt: every program with its arguments, control characters
        /// made visible so nothing can hide behind line breaks.</summary>
        public static string Describe(IList<AutomationAction> runs)
        {
            var sb = new StringBuilder();
            sb.Append(runs.Count == 1
                ? "Uma automação quer executar este programa sempre que o jogo abrir ou fechar:"
                : "Uma automação quer executar estes " + runs.Count + " programas sempre que o jogo abrir ou fechar:");
            sb.Append("\n");
            foreach (AutomationAction a in runs)
            {
                sb.Append("\n• Programa: ").Append(Visible(a.Target));
                sb.Append("\n   Argumentos: ").Append(string.IsNullOrWhiteSpace(a.Args) ? "(nenhum)" : Visible(a.Args));
            }
            sb.Append("\n\nSó autorize se foi você quem adicionou e se confia nesses programas.");
            return sb.ToString();
        }

        /// <summary>Escapes control characters and shortens long runs of blanks (which could push text out of view).</summary>
        public static string Visible(string s)
        {
            string v = Log.EscapeControl(s ?? "");
            return System.Text.RegularExpressions.Regex.Replace(v, @" {4,}", m => " [" + m.Length + " espaços] ");
        }

        private static bool IsDropped(AutomationAction a, HashSet<string> drop)
            => a.Type == AutomationTypes.Run && drop.Contains(AutomationRunApprovals.Hash(a));

        private static IEnumerable<AutomationAction> Runs(AutomationProfile p)
            => p == null ? Enumerable.Empty<AutomationAction>()
               : (p.Before ?? new List<AutomationAction>()).Concat(p.After ?? new List<AutomationAction>())
                 .Where(a => a != null && a.Type == AutomationTypes.Run);
    }
}
