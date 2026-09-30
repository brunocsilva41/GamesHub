// "run" actions coming from the page need a native confirmation (the prompt is a fake here).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GamesHub.Tests
{
    public static class AutomationRunApprovalTests
    {
        private static AutomationAction Run(string target, string args = "", bool enabled = true)
            => new AutomationAction { Type = "run", Target = target, Args = args, Enabled = enabled };

        private static AutomationProfile Profile(params AutomationAction[] before)
            => new AutomationProfile { Before = before.ToList(), After = new List<AutomationAction>() };

        private static string TempFile() => Path.Combine(Path.GetTempPath(), "gameshub-approvals-" + Guid.NewGuid().ToString("N") + ".json");

        private static void Cleanup(string file)
        {
            foreach (string f in new[] { file, file + ".bak" }) if (File.Exists(f)) File.Delete(f);
        }

        public static void TestNewRunAsksAndApprovalIsRemembered()
        {
            string file = TempFile();
            try
            {
                var approvals = new AutomationRunApprovals(file);
                var asked = new List<IList<AutomationAction>>();
                AutomationProfile incoming = Profile(Run(@"C:\Tools\obs.exe", "--minimize"), new AutomationAction { Type = "wait", Seconds = 2, Enabled = true });
                AutomationProfile saved = AutomationRunGate.Decide(incoming, new AutomationProfile(), approvals, r => { asked.Add(r); return true; }, out int refused);
                Assert.Equal(1, asked.Count, "asked once");
                Assert.Equal(1, asked[0].Count);
                Assert.Equal(0, refused);
                Assert.Equal(2, saved.Before.Count, "everything kept");

                // Same program + arguments again (other profile, after a restart): no new prompt.
                var reloaded = new AutomationRunApprovals(file);
                Assert.True(reloaded.IsApproved(Run(@"C:\Tools\obs.exe", "--minimize")), "persisted");
                AutomationRunGate.Decide(Profile(Run(@"C:\Tools\obs.exe", "--minimize", enabled: false)), new AutomationProfile(), reloaded,
                    r => { asked.Add(r); return false; }, out refused);
                Assert.Equal(1, asked.Count, "approved action (even toggled off) is not asked again");
                Assert.Equal(0, refused);
            }
            finally { Cleanup(file); }
        }

        public static void TestChangedArgumentsAskAgain()
        {
            string file = TempFile();
            try
            {
                var approvals = new AutomationRunApprovals(file);
                approvals.Approve(new[] { Run(@"C:\Tools\obs.exe", "--minimize") });
                int prompts = 0;
                AutomationProfile saved = AutomationRunGate.Decide(Profile(Run(@"C:\Tools\obs.exe", "--minimize & calc")), null, approvals,
                    r => { prompts++; return false; }, out int refused);
                Assert.Equal(1, prompts);
                Assert.Equal(1, refused);
                Assert.Equal(0, saved.Before.Count, "declined run is not saved");
                Assert.True(AutomationRunApprovals.Hash(Run("a", "b")) != AutomationRunApprovals.Hash(Run("a b", "")), "target/args boundary is part of the hash");
            }
            finally { Cleanup(file); }
        }

        public static void TestDeclineKeepsEverythingElse()
        {
            string file = TempFile();
            try
            {
                var approvals = new AutomationRunApprovals(file);
                AutomationProfile current = Profile(Run(@"C:\Old\tool.exe"));   // already stored → trusted
                var incoming = new AutomationProfile
                {
                    Enabled = true, UseDefault = false,
                    Before = new List<AutomationAction> { Run(@"C:\Old\tool.exe"), Run(@"C:\Evil\payload.exe", "-x"), new AutomationAction { Type = "close", Target = "discord.exe", Enabled = true } },
                    After = new List<AutomationAction> { Run(@"C:\Evil\payload.exe", "-x") },
                };
                IList<AutomationAction> shown = null;
                AutomationProfile saved = AutomationRunGate.Decide(incoming, current, approvals, r => { shown = r; return false; }, out int refused);
                Assert.Equal(1, shown.Count, "only the new program, once");
                Assert.Equal(@"C:\Evil\payload.exe", shown[0].Target);
                Assert.Equal(2, refused, "removed from before and after");
                Assert.Equal("run|close", string.Join("|", saved.Before.Select(a => a.Type)));
                Assert.Equal(@"C:\Old\tool.exe", saved.Before[0].Target);
                Assert.Equal(0, saved.After.Count);
                Assert.False(saved.UseDefault, "other settings still saved");
                Assert.False(approvals.IsApproved(Run(@"C:\Evil\payload.exe", "-x")), "a refusal is not remembered as approval");
            }
            finally { Cleanup(file); }
        }

        public static void TestNoPromptAvailableMeansDeclined()
        {
            AutomationProfile saved = AutomationRunGate.Decide(Profile(Run("calc.exe")), null, null, null, out int refused);
            Assert.Equal(1, refused);
            Assert.Equal(0, saved.Before.Count);
            // Profiles without run actions never prompt.
            AutomationRunGate.Decide(Profile(new AutomationAction { Type = "wait", Seconds = 3, Enabled = true }), null, null,
                r => throw new Exception("must not prompt"), out refused);
            Assert.Equal(0, refused);
        }

        public static void TestDescribeShowsTargetAndArgumentsVisibly()
        {
            string text = AutomationRunGate.Describe(new List<AutomationAction> { Run(@"C:\x\a.exe", "--one\r\n--hidden        tail") });
            Assert.True(text.Contains(@"C:\x\a.exe"), text);
            Assert.True(text.Contains(@"--one\r\n--hidden"), "line breaks shown escaped: " + text);
            Assert.True(text.Contains("[8 espaços]"), "long blank runs shortened: " + text);
            Assert.True(AutomationRunGate.Describe(new List<AutomationAction> { Run("a.exe") }).Contains("(nenhum)"));
        }
    }
}
