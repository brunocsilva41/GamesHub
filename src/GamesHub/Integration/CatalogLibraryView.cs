// OWNER: integration (lead).
using System;
using System.Collections.Generic;

namespace GamesHub
{
    /// <summary>
    /// ILibraryService facade over the catalog, for consumers written against the library contract (quick launch):
    /// they get the enriched, variant-collapsed list, and launches go through automation + variant resolution.
    /// Dispose is a no-op — the real library is owned by the app.
    /// </summary>
    public sealed class CatalogLibraryView : ILibraryService
    {
        private readonly GameCatalog _catalog;
        public CatalogLibraryView(GameCatalog catalog) { _catalog = catalog; }

        public event Action Changed
        {
            add => _catalog.Changed += value;
            remove => _catalog.Changed -= value;
        }

        public event Action<string, bool> RunningChanged
        {
            add => _catalog.Library.RunningChanged += value;
            remove => _catalog.Library.RunningChanged -= value;
        }

        public List<Game> GetGames() => _catalog.GetGames();
        public Game Get(string id) => _catalog.Get(id);
        public List<string> GetCollections() => _catalog.Library.GetCollections();
        public void Start() { }
        public void Rescan() => _catalog.Library.Rescan();
        public OpResult Update(string id, GameEdit edit) => _catalog.Library.Update(id, edit);
        public OpResult AddFromFile(string path, string name) => _catalog.Library.AddFromFile(path, name);
        public OpResult AddSteamApp(string appId, string name) => _catalog.Library.AddSteamApp(appId, name);
        public OpResult Remove(string id) => _catalog.Library.Remove(id);
        public OpResult Undo(string undoToken) => _catalog.Library.Undo(undoToken);
        /// <summary>Blocks until the "before" automation ran; callers already invoke this off the UI thread.</summary>
        public OpResult Launch(string id) => _catalog.LaunchAsync(id, null).GetAwaiter().GetResult();
        public string RevealPath(string id) => _catalog.Library.RevealPath(id);
        public void Dispose() { }
    }
}
