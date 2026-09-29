// OWNER: LIB agent. Skeleton created by lead — replace entirely.
using System;
using System.Collections.Generic;

namespace GamesHub
{
    public sealed class LibraryService : ILibraryService
    {
        private readonly AppSettings _settings;
        private readonly IArtworkService _art;
        public LibraryService(AppSettings settings, IArtworkService art) { _settings = settings; _art = art; }
        public event Action Changed;
        public event Action<string, bool> RunningChanged;
        public List<Game> GetGames() => new List<Game>();
        public Game Get(string id) => null;
        public List<string> GetCollections() => new List<string>();
        public void Start() { }
        public void Rescan() { }
        public OpResult Update(string id, GameEdit edit) => OpResult.Fail("Não implementado");
        public OpResult AddFromFile(string path, string name) => OpResult.Fail("Não implementado");
        public OpResult AddSteamApp(string appId, string name) => OpResult.Fail("Não implementado");
        public OpResult Remove(string id) => OpResult.Fail("Não implementado");
        public OpResult Undo(string undoToken) => OpResult.Fail("Não implementado");
        public OpResult Launch(string id) => OpResult.Fail("Não implementado");
        public string RevealPath(string id) => "";
        public void Dispose() { }
        private void Raise() { Changed?.Invoke(); RunningChanged?.Invoke("", false); }
    }
}
