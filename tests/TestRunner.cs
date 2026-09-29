// Convention: any public static class whose name ends with "Tests"; every public static void method
// whose name starts with "Test" is a test. Use Assert.* below. Exit code = number of failures.
// Options: --junit <file>   write a JUnit XML report (CI test summary)
//          --filter <text>  run only tests whose "Class.Method" contains <text>
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml.Linq;

namespace GamesHub.Tests
{
    public static class Assert
    {
        public static void True(bool cond, string msg = "expected true") { if (!cond) throw new Exception(msg); }
        public static void False(bool cond, string msg = "expected false") { if (cond) throw new Exception(msg); }
        public static void Equal<T>(T expected, T actual, string msg = "")
        {
            if (!Equals(expected, actual))
                throw new Exception($"{msg} expected <{expected}> but got <{actual}>".Trim());
        }
        public static void NotNull(object o, string msg = "expected non-null") { if (o == null) throw new Exception(msg); }
    }

    public static class TestRunner
    {
        private sealed class Result
        {
            public string Suite, Name, Error, Stack;
            public double Seconds;
            public bool Passed => Error == null;
        }

        public static int Main(string[] args)
        {
            // Keep logs/caches written by code under test away from the user's real %LOCALAPPDATA%\GamesHub.
            Environment.SetEnvironmentVariable("GAMESHUB_DATA_DIR", Path.Combine(Path.GetTempPath(), "gameshub-tests-data"));
            string junit = Option(args, "--junit");
            string filter = Option(args, "--filter");

            var results = new List<Result>();
            var total = Stopwatch.StartNew();
            var classes = Assembly.GetExecutingAssembly().GetTypes()
                .Where(t => t.IsClass && t.IsAbstract && t.IsSealed && t.Name.EndsWith("Tests")).OrderBy(t => t.Name);
            foreach (Type t in classes)
            foreach (MethodInfo m in t.GetMethods(BindingFlags.Public | BindingFlags.Static)
                         .Where(m => m.Name.StartsWith("Test") && m.GetParameters().Length == 0).OrderBy(m => m.Name))
            {
                string full = t.Name + "." + m.Name;
                if (filter != null && full.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                var r = new Result { Suite = t.Name, Name = m.Name };
                var sw = Stopwatch.StartNew();
                try { m.Invoke(null, null); }
                catch (TargetInvocationException ex)
                {
                    Exception inner = ex.InnerException ?? ex;
                    r.Error = inner.GetType().Name + ": " + inner.Message;
                    r.Stack = inner.StackTrace;
                }
                r.Seconds = sw.Elapsed.TotalSeconds;
                results.Add(r);
                Console.WriteLine((r.Passed ? "  ok   " : "  FAIL ") + full + (r.Passed ? "" : ": " + r.Error));
            }

            int fail = results.Count(r => !r.Passed);
            Console.WriteLine($"{results.Count - fail} passed, {fail} failed ({total.Elapsed.TotalSeconds:0.0}s)");
            if (junit != null) WriteJUnit(junit, results);
            if (results.Count == 0)
            {
                Console.WriteLine("No tests were run — failing (an empty test run is never a success).");
                return 1;
            }
            return fail;
        }

        private static string Option(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        private static void WriteJUnit(string file, List<Result> results)
        {
            string F(double s) => s.ToString("0.000", CultureInfo.InvariantCulture);
            var suites = new XElement("testsuites",
                new XAttribute("name", "GamesHub"),
                new XAttribute("tests", results.Count),
                new XAttribute("failures", results.Count(r => !r.Passed)),
                new XAttribute("time", F(results.Sum(r => r.Seconds))),
                results.GroupBy(r => r.Suite).Select(g => new XElement("testsuite",
                    new XAttribute("name", g.Key),
                    new XAttribute("tests", g.Count()),
                    new XAttribute("failures", g.Count(r => !r.Passed)),
                    new XAttribute("time", F(g.Sum(r => r.Seconds))),
                    g.Select(r => new XElement("testcase",
                        new XAttribute("classname", "GamesHub.Tests." + r.Suite),
                        new XAttribute("name", r.Name),
                        new XAttribute("time", F(r.Seconds)),
                        r.Passed ? null : new XElement("failure", new XAttribute("message", r.Error), r.Stack ?? ""))))));
            string dir = Path.GetDirectoryName(Path.GetFullPath(file));
            Directory.CreateDirectory(dir);
            File.WriteAllText(file, new XDocument(suites).ToString(), new UTF8Encoding(false));
        }
    }
}
