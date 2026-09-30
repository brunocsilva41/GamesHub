using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GamesHub
{
    /// <summary>
    /// Finds games installed outside the supported launchers (read-only): Uninstall registry entries plus a shallow
    /// scan of the usual game folders on every fixed drive (see <see cref="DiscoveryRoots"/>). Each candidate folder
    /// is scored by <see cref="GameEvidence"/> plus priors (game folder +0.2, game publisher +0.25 / games-ish
    /// publisher +0.1, registry size ≥ 2 GB +0.1); hard negatives (utilities, drivers, runtimes, launchers, dev
    /// tools) are excluded by name. Candidates already in the library (same exe, same/inner install folder, or same
    /// normalized name) are dropped. Bounded by <see cref="ScanBudget"/>; never throws.
    /// </summary>
    public sealed class GameDiscovery
    {
        private const double GameFolderPrior = 0.2, PublisherPrior = 0.25, GameishPublisherPrior = 0.1, BigInstallPrior = 0.1;
        private const long BigInstall = 2L * 1024 * 1024 * 1024;

        public Func<List<RegistryApp>> Registry = DiscoveryRegistry.ReadAll;
        public Func<List<ScanRoot>> Roots = DiscoveryRoots.Default;
        public long MaxEntries = 200000;
        public TimeSpan Deadline = TimeSpan.FromSeconds(12);
        /// <summary>Folders examined in parallel (I/O bound).</summary>
        public int Parallelism = 4;

        private sealed class Pending
        {
            public string Dir = "", Key = "";
            public RegistryApp App;
            public ScanRoot Root;
            public bool FromRegistry => App != null;
        }

        public Task<List<DiscoveredGame>> DiscoverAsync(IEnumerable<Game> known, CancellationToken ct, IProgress<string> progress)
            => Task.Run(() => Discover(known, ct, progress));

        public List<DiscoveredGame> Discover(IEnumerable<Game> known, CancellationToken ct = default, IProgress<string> progress = null)
        {
            try
            {
                return Run(new KnownGames(known), new ScanBudget(MaxEntries, Deadline, ct), progress);
            }
            // Resilience boundary: discovery probes arbitrary third-party folders; a bug must not break the Add dialog.
            catch (Exception ex)
            {
                Log.Error("Discovery failed", ex);
                return new List<DiscoveredGame>();
            }
        }

        private List<DiscoveredGame> Run(KnownGames known, ScanBudget budget, IProgress<string> progress)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var pending = new Dictionary<string, Pending>();
            progress?.Report("Lendo os programas instalados…");
            foreach (RegistryApp app in Registry() ?? new List<RegistryApp>())
            {
                if (DiscoveryRules.IsNotAGame(app.Name) || DiscoveryRules.IsUtilityPublisher(app.Publisher)) continue;
                string dir = app.InstallLocation;
                if (DiscoveryRoots.Key(dir).Length == 0 || !DiscoveryRoots.SafeExists(dir))
                {
                    string icon = DiscoveryRegistry.IconExe(app.DisplayIcon);
                    dir = icon.Length > 0 ? SafeDirName(icon) : "";
                }
                AddPending(pending, dir, app, null);
            }

            List<ScanRoot> roots = Roots() ?? new List<ScanRoot>();
            var rootKeys = new HashSet<string>(roots.Select(r => DiscoveryRoots.Key(r.Path)));
            known.AddContainers(rootKeys);
            foreach (ScanRoot root in roots)
            {
                if (budget.Exhausted) break;
                progress?.Report("Procurando em " + root.Path + "…");
                // XboxGames holds only games (their names may start with "Microsoft"), plus the GameSave folder.
                foreach (DirectoryInfo d in DiscoveryRoots.SubDirs(root.Path, budget))
                    if ((root.Kind == "xbox" ? !d.Name.Equals("GameSave", StringComparison.OrdinalIgnoreCase) : !IsSkippedFolder(d.Name))
                        && !rootKeys.Contains(DiscoveryRoots.Key(d.FullName)))
                        AddPending(pending, d.FullName, null, root);
            }

            foreach (string k in rootKeys) pending.Remove(k); // a registry InstallLocation of "D:\Games" is not one game

            var found = new ConcurrentBag<DiscoveredGame>();
            var containers = new ConcurrentBag<Pending>();
            progress?.Report("Analisando " + pending.Count + " pastas…");
            Inspect(pending.Values, budget, found, containers);

            var deeper = new Dictionary<string, Pending>();
            foreach (Pending c in containers)
                foreach (DirectoryInfo d in DiscoveryRoots.SubDirs(c.Dir, budget, 30))
                    if (!IsSkippedFolder(d.Name) && !pending.ContainsKey(DiscoveryRoots.Key(d.FullName)))
                        AddPending(deeper, d.FullName, null, new ScanRoot(c.Root.Path, c.Root.Kind, false));
            if (deeper.Count > 0)
            {
                progress?.Report("Analisando mais " + deeper.Count + " pastas…");
                Inspect(deeper.Values, budget, found, new ConcurrentBag<Pending>());
            }

            List<DiscoveredGame> result = Merge(found).Where(g => !known.Contains(g)).OrderByDescending(g => g.Confidence)
                .ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            Log.Info("Discovery: " + result.Count + " candidate(s) from " + (pending.Count + deeper.Count) + " folder(s), "
                     + budget.Entries + " entries in " + sw.ElapsedMilliseconds + " ms" + (budget.Exhausted ? " (budget exhausted)" : ""));
            progress?.Report("Concluído.");
            return result;
        }

        private static void AddPending(Dictionary<string, Pending> pending, string dir, RegistryApp app, ScanRoot root)
        {
            string key = DiscoveryRoots.Key(dir);
            if (key.Length == 0 || DiscoveryRoots.IsForbidden(key)) return;
            if (pending.TryGetValue(key, out Pending p))
            {
                p.App = p.App ?? app;
                p.Root = p.Root ?? root;
                return;
            }
            pending[key] = new Pending { Dir = dir, Key = key, App = app, Root = root };
        }

        private static bool IsSkippedFolder(string name)
            => DiscoveryRules.IsNotAGame(name) || name.StartsWith("$", StringComparison.Ordinal)
               || name.Equals("SteamLibrary", StringComparison.OrdinalIgnoreCase) || name.Equals("steamapps", StringComparison.OrdinalIgnoreCase)
               || name.Equals("GameSave", StringComparison.OrdinalIgnoreCase);

        private void Inspect(IEnumerable<Pending> items, ScanBudget budget, ConcurrentBag<DiscoveredGame> found, ConcurrentBag<Pending> containers)
        {
            var opts = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Parallelism) };
            Parallel.ForEach(items.ToList(), opts, (p, state) =>
            {
                if (budget.Exhausted) { state.Stop(); return; }
                try
                {
                    DiscoveredGame g = Evaluate(p, budget, out bool container);
                    if (g != null) found.Add(g);
                    else if (container) containers.Add(p);
                }
                // Resilience boundary: one unreadable/odd folder must not abort the whole scan.
                catch (Exception ex)
                {
                    Log.Warn("Discovery: failed to inspect " + p.Dir, ex);
                }
            });
        }

        /// <summary>Scores one folder; null when it is not a game (container = worth looking one level deeper).</summary>
        private static DiscoveredGame Evaluate(Pending p, ScanBudget budget, out bool container)
        {
            container = false;
            FolderSnapshot snap = FolderSnapshot.Take(p.Dir, budget);
            if (!snap.Readable) return null;
            Evidence ev = GameEvidence.Analyze(snap);
            string folderName = Path.GetFileName(p.Dir.TrimEnd('\\'));
            string regName = p.FromRegistry ? DiscoveryRules.CleanDisplayName(p.App.Name) : "";
            string preferred = p.FromRegistry ? DiscoveryRegistry.IconExe(p.App.DisplayIcon) : "";
            FolderSnapshot.Entry exe = MainExePicker.Pick(snap, new[] { regName, ev.XboxName, folderName }, preferred);
            bool rootExe = snap.Exes.Any(e => e.Depth == 0 && !DiscoveryRules.IsHelperExe(e.Name));
            // Several sub-folders with their own executables and none at the top: a folder of games, not a game.
            int exeFolders = snap.Exes.Where(e => e.Depth > 0 && !DiscoveryRules.IsHelperExe(e.Name) && !e.Rel.StartsWith("engine\\", StringComparison.Ordinal))
                .Select(e => e.Rel.Split('\\')[0]).Where(d => !FolderSnapshot.IsLayoutDir(d)).Distinct().Count();
            if (exe == null || (!rootExe && (exeFolders > 1 || ev.Score < GameEvidence.Threshold)))
            {
                container = p.Root != null && p.Root.Containers && !rootExe;
                if (exe == null || exeFolders > 1) return null;
            }
            if (DiscoveryRules.IsNotAGame(Path.GetFileNameWithoutExtension(exe.Name))) return null;

            double score = ev.Score;
            var reasons = new List<string>(ev.Reasons);
            if (p.Root != null && (p.Root.Kind == "games" || p.Root.Kind == "xbox")) { score += GameFolderPrior; reasons.Add("Pasta de jogos"); }
            string publisher = p.App?.Publisher ?? "";
            if (DiscoveryRules.IsGamePublisher(publisher)) { score += PublisherPrior; reasons.Add("Editora de jogos"); }
            else if (DiscoveryRules.IsGameishPublisher(publisher)) { score += GameishPublisherPrior; reasons.Add("Editora de jogos"); }
            long size = p.App?.SizeBytes ?? -1;
            if (size >= BigInstall) { score += BigInstallPrior; reasons.Add("Instalação grande"); }
            if (p.FromRegistry) reasons.Add("Registro do Windows");

            double confidence = Math.Round(Math.Max(0, Math.Min(1, score)), 2);
            if (confidence < GameEvidence.Threshold) return null;
            string name = ev.XboxName.Length > 0 ? ev.XboxName : regName.Length > 0 ? regName : FolderGameName(p.Dir, folderName, exe);
            return new DiscoveredGame
            {
                Name = name, Exe = exe.FullPath, InstallDir = p.Dir.TrimEnd('\\'), Confidence = confidence, Reasons = reasons,
                Source = ev.Xbox ? "xbox" : p.FromRegistry ? "registry" : "folder", SizeBytes = size, Publisher = publisher,
            };
        }

        /// <summary>One candidate per executable: best confidence wins, registry name/size/publisher fill the gaps.</summary>
        internal static List<DiscoveredGame> Merge(IEnumerable<DiscoveredGame> all)
        {
            var result = new List<DiscoveredGame>();
            foreach (IGrouping<string, DiscoveredGame> grp in all.GroupBy(g => DiscoveryRoots.Key(g.Exe)))
            {
                List<DiscoveredGame> list = grp.OrderByDescending(g => g.Confidence).ThenBy(g => g.InstallDir.Length).ToList();
                DiscoveredGame best = list[0];
                DiscoveredGame reg = list.FirstOrDefault(g => g.Source == "registry");
                if (reg != null && best.Source != "xbox")
                {
                    best.Name = reg.Name;
                    best.Source = "registry";
                }
                if (best.SizeBytes < 0) best.SizeBytes = list.Max(g => g.SizeBytes);
                if (best.Publisher.Length == 0) best.Publisher = list.Select(g => g.Publisher).FirstOrDefault(s => s.Length > 0) ?? "";
                best.Reasons = list.SelectMany(g => g.Reasons).Distinct().ToList();
                result.Add(best);
            }
            return result;
        }

        /// <summary>Name from the folder: release tags stripped; when the folder name was a download/repack name
        /// ("Game-SteamRIP.com\Game\game.exe") the sub-folder holding the exe is used instead.</summary>
        internal static string FolderGameName(string dir, string folderName, FolderSnapshot.Entry exe)
        {
            string name = DiscoveryRules.CleanFolderName(folderName, out bool noisy);
            string root = dir.TrimEnd('\\') + "\\";
            if ((noisy || name.Length == 0) && exe != null && exe.Depth > 0 && exe.FullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                string first = exe.FullPath.Substring(root.Length).Split('\\')[0];
                if (!FolderSnapshot.IsLayoutDir(first)) return DiscoveryRules.CleanFolderName(first, out _);
            }
            return name.Length > 0 ? name : DiscoveryRules.CleanDisplayName(folderName);
        }

        private static string SafeDirName(string file)
        {
            try { return Path.GetDirectoryName(file) ?? ""; }
            catch (Exception ex) when (DiscoveryErrors.IsIo(ex)) { return ""; }
        }
    }
}
