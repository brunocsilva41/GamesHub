// Convention: any public static class whose name ends with "Tests"; every public static void method
// whose name starts with "Test" is a test. Use Assert.* below. Exit code = number of failures.
using System;
using System.Linq;
using System.Reflection;

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
        public static int Main()
        {
            // Keep logs/caches written by code under test away from the user's real %LOCALAPPDATA%GamesHub.
            Environment.SetEnvironmentVariable("GAMESHUB_DATA_DIR", System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gameshub-tests-data"));
            int pass = 0, fail = 0;
            var classes = Assembly.GetExecutingAssembly().GetTypes()
                .Where(t => t.IsClass && t.IsAbstract && t.IsSealed && t.Name.EndsWith("Tests")).OrderBy(t => t.Name);
            foreach (Type t in classes)
            foreach (MethodInfo m in t.GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.Name.StartsWith("Test") && m.GetParameters().Length == 0))
            {
                try { m.Invoke(null, null); pass++; Console.WriteLine("  ok   " + t.Name + "." + m.Name); }
                catch (TargetInvocationException ex) { fail++; Console.WriteLine("  FAIL " + t.Name + "." + m.Name + ": " + ex.InnerException?.Message); }
            }
            Console.WriteLine($"{pass} passed, {fail} failed");
            return fail;
        }
    }
}
