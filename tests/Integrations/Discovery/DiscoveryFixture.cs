using System;
using System.Collections.Generic;
using System.IO;

namespace GamesHub.Tests
{
    /// <summary>Temp folder trees for the discovery tests. Dispose deletes the tree.</summary>
    internal sealed class DiscoveryFixture : IDisposable
    {
        public readonly string Root = Path.Combine(Path.GetTempPath(), "gh-discovery-" + Guid.NewGuid().ToString("N").Substring(0, 8));

        public DiscoveryFixture() { Directory.CreateDirectory(Root); }

        /// <summary>Creates files (and their folders) under <paramref name="dir"/>. "name:12345" makes a file of 12345 bytes;
        /// a trailing "\" makes a folder.</summary>
        public string Tree(string dir, params string[] entries)
        {
            string baseDir = Path.Combine(Root, dir);
            Directory.CreateDirectory(baseDir);
            foreach (string e in entries)
            {
                if (e.EndsWith("\\")) { Directory.CreateDirectory(Path.Combine(baseDir, e)); continue; }
                string[] parts = e.Split(':');
                string file = Path.Combine(baseDir, parts[0]);
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                int size = parts.Length > 1 ? int.Parse(parts[1]) : 16;
                File.WriteAllBytes(file, new byte[size]);
            }
            return baseDir;
        }

        public string Path_(string rel) => Path.Combine(Root, rel);

        /// <summary>Discovery over this fixture only: the given roots, the given registry entries, no real machine state.</summary>
        public GameDiscovery Discovery(List<RegistryApp> registry = null, params ScanRoot[] roots)
            => new GameDiscovery
            {
                Registry = () => registry ?? new List<RegistryApp>(),
                Roots = () => new List<ScanRoot>(roots),
                Deadline = TimeSpan.FromSeconds(20),
            };

        public ScanRoot GamesRoot(string rel = "Games") => new ScanRoot(Path.Combine(Root, rel), "games");

        public void Dispose()
        {
            try { Directory.Delete(Root, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        /// <summary>A Unity game folder: exe + UnityPlayer.dll + &lt;exe&gt;_Data + crash handler (bigger than the game exe).</summary>
        public string UnityGame(string rel, string exeName)
            => Tree(rel, exeName + ".exe:600000", "UnityPlayer.dll", exeName + "_Data\\globalgamemanagers", "UnityCrashHandler64.exe:1500000",
                    "unins000.exe:2000000");

        /// <summary>An Unreal game folder: small root stub, big -Shipping exe, .pak and the engine crash reporter.</summary>
        public string UnrealGame(string rel, string stubName, string project)
            => Tree(rel, stubName + ".exe:300000",
                    project + "\\Binaries\\Win64\\" + project + "-Win64-Shipping.exe:2000000",
                    project + "\\Content\\Paks\\" + project + "-WindowsNoEditor.pak",
                    "Engine\\Binaries\\Win64\\CrashReportClient.exe:900000",
                    "Engine\\Binaries\\ThirdParty\\PhysX3\\Win64\\PhysX3_x64.dll");
    }
}
