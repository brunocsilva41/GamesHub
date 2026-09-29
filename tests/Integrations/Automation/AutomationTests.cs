// OWNER: AUTO agent. Automation: validation, persistence, ordering, snapshot/restore, crash restore.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GamesHub.Tests
{
    internal sealed class AutoFakeSettings : IAutomationSettingsAccess
    {
        public readonly Dictionary<string, string> Values = new Dictionary<string, string>
        {
            { AutomationTypes.PowerPlan, "11111111-1111-1111-1111-111111111111" },
            { AutomationTypes.AudioDevice, "{speakers}" },
            { AutomationTypes.Resolution, "2560x1440@144" },
        };
        public readonly List<string> Log;
        public AutoFakeSettings(List<string> log) { Log = log; }
        public string Get(string type) => Values[type];
        public void Set(string type, string value) { Values[type] = value; Log.Add("set:" + type + "=" + value); }
    }

    internal sealed class AutoFakeExecutor : IActionExecutor
    {
        private readonly List<string> log;
        private readonly Func<AutomationAction, CancellationToken, Task> body;
        public AutoFakeExecutor(string type, List<string> log, Func<AutomationAction, CancellationToken, Task> body = null)
        { Type = type; this.log = log; this.body = body; }
        public string Type { get; }
        public async Task<string> ExecuteAsync(AutomationAction a, CancellationToken ct)
        {
            lock (log) log.Add(Type + ":" + (Type == AutomationTypes.Wait ? a.Seconds.ToString() : a.Target));
            if (body != null) await body(a, ct);
            return "ok";
        }
    }

    public static class AutomationTests
    {
        private static string TempDir()
        {
            string d = Path.Combine(Path.GetTempPath(), "gameshub-auto-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(d);
            return d;
        }

        private static AutomationAction A(string type, string target = "", int seconds = 0, bool enabled = true)
            => new AutomationAction { Type = type, Target = target, Seconds = seconds, Enabled = enabled };

        private static Game G(string id) => new Game { Id = id, Name = id };

        /// <summary>Service whose setting actions go through AutoFakeSettings (like the real wiring) and whose
        /// run/close/wait are recorded fakes.</summary>
        private static AutomationService Svc(string dir, List<string> log, out AutoFakeSettings fs, TimeSpan? budget = null,
                                             IActionExecutor runOverride = null)
        {
            fs = new AutoFakeSettings(log);
            var ex = new List<IActionExecutor>
            {
                runOverride ?? new AutoFakeExecutor(AutomationTypes.Run, log), new AutoFakeExecutor(AutomationTypes.Close, log),
                new AutoFakeExecutor(AutomationTypes.Wait, log),
                new SettingActionExecutor(AutomationTypes.PowerPlan, fs), new SettingActionExecutor(AutomationTypes.AudioDevice, fs),
                new SettingActionExecutor(AutomationTypes.Resolution, fs),
            };
            return new AutomationService(dir, fs, ex, budget ?? TimeSpan.FromSeconds(5));
        }

        // ---------------------------------------------------------------- validation

        public static void TestSanitizeDropsUnknownAndInvalid()
        {
            var p = new AutomationProfile
            {
                Before = new List<AutomationAction>
                {
                    A("RUN", "notepad.exe"), A("format-disk", "C:"), A("close", "explorer.exe"), A("close", "C:\\x\\Discord.exe"),
                    A("powerPlan", "not-a-guid"), A("powerplan", "{381B4222-F694-41F0-9685-FF5BB260DF2E}"),
                    A("resolution", "1920 X 1080 @ 60Hz"), A("resolution", "big"), A("wait", "", 999), A("wait", "", 0),
                    A("run", "   "), null,
                },
            };
            AutomationProfile s = AutomationValidation.Sanitize(p, out int dropped);
            Assert.Equal(7, dropped);
            Assert.Equal("run|close|powerPlan|resolution|wait", string.Join("|", s.Before.Select(a => a.Type)));
            Assert.Equal("Discord", s.Before[1].Target);
            Assert.Equal("381b4222-f694-41f0-9685-ff5bb260df2e", s.Before[2].Target);
            Assert.Equal("1920x1080@60", s.Before[3].Target);
            Assert.Equal(AutomationValidation.MaxWaitSeconds, s.Before[4].Seconds);
        }

        public static void TestSanitizeCapsListLength()
        {
            var p = new AutomationProfile();
            for (int i = 0; i < 30; i++) p.After.Add(A("run", "app" + i + ".exe"));
            AutomationProfile s = AutomationValidation.Sanitize(p, out int dropped);
            Assert.Equal(AutomationValidation.MaxActions, s.After.Count);
            Assert.Equal(10, dropped);
            Assert.Equal("app0.exe", s.After[0].Target);
        }

        public static void TestProtectedProcesses()
        {
            foreach (string n in new[] { "explorer", "EXPLORER.EXE", "csrss", "winlogon", "svchost", "dwm", "lsass", "services", "GamesHub", "C:\\Windows\\explorer.exe", "" })
                Assert.True(AutomationProcessRules.IsProtected(n), "should be protected: " + n);
            foreach (string n in new[] { "Discord", "chrome.exe", "Steam" })
                Assert.False(AutomationProcessRules.IsProtected(n), "should be allowed: " + n);
            Assert.Equal("Discord", AutomationProcessRules.NormalizeName(" \"C:\\Apps\\Discord.EXE\" "));
        }

        public static void TestCloseExecutorRefusesProtected()
        {
            bool threw = false;
            try { new CloseActionExecutor().ExecuteAsync(A("close", "explorer"), CancellationToken.None).GetAwaiter().GetResult(); }
            catch (InvalidOperationException) { threw = true; }
            Assert.True(threw, "closing explorer must be refused");
        }

        // ---------------------------------------------------------------- resolution strings

        public static void TestResolutionParseFormat()
        {
            Assert.True(ResolutionSpec.TryParse("1920x1080@60", out ResolutionSpec r));
            Assert.Equal(1920, r.Width); Assert.Equal(1080, r.Height); Assert.Equal(60, r.Hz);
            Assert.Equal("1920x1080@60", r.ToString());
            Assert.Equal("1920 × 1080 (60 Hz)", r.DisplayName);
            Assert.True(ResolutionSpec.TryParse(" 1280 × 720 ", out r));
            Assert.Equal("1280x720", r.ToString());
            Assert.True(r.Matches(new ResolutionSpec(1280, 720, 75)), "Hz 0 matches any rate");
            Assert.True(ResolutionSpec.TryParse("2560X1440 @ 144 hz", out r));
            Assert.Equal("2560x1440@144", r.ToString());
            foreach (string bad in new[] { "", "1920", "1920x", "abc", "10x10@60", "1920x1080@", "1920x1080@60@60", "99999x1080" })
                Assert.False(ResolutionSpec.TryParse(bad, out _), "should reject '" + bad + "'");
        }

        public static void TestResolutionSortDistinct()
        {
            var sorted = ResolutionSpec.SortDistinct(new[]
            {
                new ResolutionSpec(1280, 720, 60), new ResolutionSpec(1920, 1080, 60), new ResolutionSpec(1920, 1080, 144),
                new ResolutionSpec(1920, 1080, 60), new ResolutionSpec(1920, 1200, 60),
            });
            Assert.Equal("1920x1200@60,1920x1080@144,1920x1080@60,1280x720@60", string.Join(",", sorted));
        }

        // ---------------------------------------------------------------- persistence

        public static void TestProfilesRoundTripThroughDisk()
        {
            string dir = TempDir();
            var log = new List<string>();
            AutomationService s1 = Svc(dir, log, out _);
            OpResult r = s1.SaveProfile("steam:730", new AutomationProfile
            {
                UseDefault = false,
                Before = { A("close", "Discord.exe"), A("wait", "", 2), A("bogus", "x") },
                After = { new AutomationAction { Type = "run", Target = "C:\\Apps\\Discord.exe", Args = "--start-minimized" } },
            });
            Assert.True(r.Ok);
            Assert.True(r.Message.Contains("1 ação inválida"), r.Message);
            Assert.True(s1.SaveDefaultProfile(new AutomationProfile { Before = { A("powerPlan", "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c") } }).Ok);
            Assert.True(File.Exists(Path.Combine(dir, "automation.json")));

            AutomationService s2 = Svc(dir, log, out _);
            AutomationProfile p = s2.GetProfile("steam:730");
            Assert.False(p.UseDefault);
            Assert.Equal(2, p.Before.Count);
            Assert.Equal("Discord", p.Before[0].Target);
            Assert.Equal("--start-minimized", p.After[0].Args);
            Assert.Equal(1, s2.GetDefaultProfile().Before.Count);
            Assert.Equal(0, s2.GetProfile("unknown").Before.Count);
            Assert.True(s2.GetProfile("unknown").Enabled);

            // returned profiles are copies
            p.Before.Clear();
            Assert.Equal(2, s2.GetProfile("steam:730").Before.Count);

            // saving a trivial profile removes the entry
            s2.SaveProfile("steam:730", new AutomationProfile());
            Assert.False(File.ReadAllText(Path.Combine(dir, "automation.json")).Contains("steam:730"));
        }

        public static void TestCorruptFileFallsBackToEmpty()
        {
            string dir = TempDir();
            File.WriteAllText(Path.Combine(dir, "automation.json"), "{ not json");
            AutomationService s = Svc(dir, new List<string>(), out _);
            Assert.Equal(0, s.GetDefaultProfile().Before.Count);
        }

        // ---------------------------------------------------------------- ordering

        public static void TestComposeOrderAndSwitches()
        {
            var def = new AutomationProfile { Before = { A("run", "d1"), A("run", "d2", enabled: false) }, After = { A("run", "da") } };
            var game = new AutomationProfile { Before = { A("run", "g1") }, After = { A("run", "ga") } };
            Assert.Equal("d1,g1", string.Join(",", AutomationValidation.Compose(def, game, true).Select(a => a.Target)));
            Assert.Equal("da,ga", string.Join(",", AutomationValidation.Compose(def, game, false).Select(a => a.Target)));
            game.UseDefault = false;
            Assert.Equal("g1", string.Join(",", AutomationValidation.Compose(def, game, true).Select(a => a.Target)));
            game.UseDefault = true; def.Enabled = false;
            Assert.Equal("g1", string.Join(",", AutomationValidation.Compose(def, game, true).Select(a => a.Target)));
            def.Enabled = true; game.Enabled = false;
            Assert.Equal(0, AutomationValidation.Compose(def, game, true).Count);
        }

        public static void TestBeforeThenAfterRestoresFirst()
        {
            string dir = TempDir();
            var log = new List<string>();
            AutomationService s = Svc(dir, log, out AutoFakeSettings fs);
            s.SaveDefaultProfile(new AutomationProfile
            {
                Before = { A("powerPlan", "22222222-2222-2222-2222-222222222222") },
                After = { A("run", "default-after") },
            });
            s.SaveProfile("g", new AutomationProfile
            {
                Before = { A("close", "Discord"), A("audioDevice", "{headset}"), A("resolution", "1920x1080@60"), A("wait", "", 1) },
                After = { A("run", "Discord.exe") },
            });

            s.RunBeforeAsync(G("g")).GetAwaiter().GetResult();
            Assert.Equal("set:powerPlan=22222222-2222-2222-2222-222222222222|close:Discord|set:audioDevice={headset}|set:resolution=1920x1080@60|wait:1",
                         string.Join("|", log));
            Assert.True(File.Exists(Path.Combine(dir, "automation-state.json")), "snapshot persisted");

            log.Clear();
            s.RunAfterAsync(G("g")).GetAwaiter().GetResult();
            Assert.Equal("set:resolution=2560x1440@144|set:audioDevice={speakers}|set:powerPlan=11111111-1111-1111-1111-111111111111|run:default-after|run:Discord.exe",
                         string.Join("|", log));
            Assert.False(File.Exists(Path.Combine(dir, "automation-state.json")), "state cleared");

            // second After without snapshot: only actions
            log.Clear();
            s.RunAfterAsync(G("g")).GetAwaiter().GetResult();
            Assert.Equal("run:default-after|run:Discord.exe", string.Join("|", log));
        }

        public static void TestErrorsDoNotAbortAndBudgetIsBounded()
        {
            string dir = TempDir();
            var log = new List<string>();
            var failing = new AutoFakeExecutor(AutomationTypes.Run, log, async (a, ct) =>
            {
                if (a.Target == "boom") throw new InvalidOperationException("boom");
                if (a.Target == "hang") await Task.Delay(TimeSpan.FromSeconds(30));
            });
            AutomationService s = Svc(dir, log, out _, TimeSpan.FromMilliseconds(300), failing);
            s.SaveProfile("g", new AutomationProfile { UseDefault = false, Before = { A("run", "boom"), A("run", "ok1"), A("run", "hang"), A("run", "late") } });
            var sw = System.Diagnostics.Stopwatch.StartNew();
            s.RunBeforeAsync(G("g")).GetAwaiter().GetResult();
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), "bounded: " + sw.Elapsed);
            Assert.Equal("run:boom|run:ok1|run:hang", string.Join("|", log));
        }

        public static void TestDisabledProfileSkipsActionsButStillRestores()
        {
            string dir = TempDir();
            var log = new List<string>();
            AutomationService s = Svc(dir, log, out _);
            s.SaveProfile("g", new AutomationProfile { Before = { A("resolution", "1280x720@60") }, After = { A("run", "x") } });
            s.RunBeforeAsync(G("g")).GetAwaiter().GetResult();
            s.SaveProfile("g", new AutomationProfile { Enabled = false, After = { A("run", "x") } });
            log.Clear();
            s.RunAfterAsync(G("g")).GetAwaiter().GetResult();
            Assert.Equal("set:resolution=2560x1440@144", string.Join("|", log));
        }

        // ---------------------------------------------------------------- overlap & crash

        public static void TestOverlappingGamesRestoreOriginalOnce()
        {
            string dir = TempDir();
            var log = new List<string>();
            AutomationService s = Svc(dir, log, out AutoFakeSettings fs);
            s.SaveProfile("a", new AutomationProfile { Before = { A("resolution", "1920x1080@60") } });
            s.SaveProfile("b", new AutomationProfile { Before = { A("resolution", "1280x720@60") } });
            s.RunBeforeAsync(G("a")).GetAwaiter().GetResult();
            s.RunBeforeAsync(G("b")).GetAwaiter().GetResult();
            log.Clear();
            s.RunAfterAsync(G("a")).GetAwaiter().GetResult();   // b still running → deferred
            Assert.Equal(0, log.Count);
            Assert.Equal("1280x720@60", fs.Values[AutomationTypes.Resolution]);
            s.RunAfterAsync(G("b")).GetAwaiter().GetResult();   // restores the real original
            Assert.Equal("2560x1440@144", fs.Values[AutomationTypes.Resolution]);
        }

        public static void TestRelaunchKeepsFirstSnapshot()
        {
            string dir = TempDir();
            var log = new List<string>();
            AutomationService s = Svc(dir, log, out AutoFakeSettings fs);
            s.SaveProfile("g", new AutomationProfile { Before = { A("audioDevice", "{headset}") } });
            s.RunBeforeAsync(G("g")).GetAwaiter().GetResult();
            s.RunBeforeAsync(G("g")).GetAwaiter().GetResult();
            s.RunAfterAsync(G("g")).GetAwaiter().GetResult();
            Assert.Equal("{speakers}", fs.Values[AutomationTypes.AudioDevice]);
        }

        public static void TestCrashRestoreOnStartup()
        {
            string dir = TempDir();
            var log = new List<string>();
            AutomationService s1 = Svc(dir, log, out AutoFakeSettings fs1);
            s1.SaveProfile("g", new AutomationProfile { Before = { A("powerPlan", "22222222-2222-2222-2222-222222222222"), A("resolution", "1280x720") } });
            s1.RunBeforeAsync(G("g")).GetAwaiter().GetResult();
            // "crash": new process, system still has the changed values
            var log2 = new List<string>();
            AutomationService s2 = Svc(dir, log2, out AutoFakeSettings fs2);
            fs2.Values[AutomationTypes.PowerPlan] = "22222222-2222-2222-2222-222222222222";
            fs2.Values[AutomationTypes.Resolution] = "1280x720@60";
            int n = s2.RestorePendingOnStartup();
            Assert.Equal(2, n);
            Assert.Equal("set:resolution=2560x1440@144|set:powerPlan=11111111-1111-1111-1111-111111111111", string.Join("|", log2));
            Assert.False(File.Exists(Path.Combine(dir, "automation-state.json")));
            Assert.Equal(0, s2.RestorePendingOnStartup());
        }

        public static void TestRestoreSkipsUnchangedSetting()
        {
            string dir = TempDir();
            var log = new List<string>();
            AutomationService s = Svc(dir, log, out AutoFakeSettings fs);
            s.SaveProfile("g", new AutomationProfile { Before = { A("audioDevice", "{speakers}") } });
            s.RunBeforeAsync(G("g")).GetAwaiter().GetResult();
            log.Clear();
            s.RunAfterAsync(G("g")).GetAwaiter().GetResult();
            Assert.Equal(0, log.Count);
        }
    }
}
