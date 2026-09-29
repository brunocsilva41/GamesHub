using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO;
using System.Linq;

namespace GamesHub
{
    public sealed partial class LibraryService
    {
        private sealed class UndoEntry
        {
            public string GameId, Name;
            public string TrashPath, OriginalPath;   // folder games
            public bool Unignore;                    // imported games
        }

        private readonly ConcurrentDictionary<string, UndoEntry> _undo = new ConcurrentDictionary<string, UndoEntry>();

        private static OpResult NotFound() => OpResult.Fail("Jogo não encontrado na biblioteca.");

        // ------------------------------------------------------------------ launch

        public OpResult Launch(string id)
        {
            Game g = Get(id);
            if (g == null) return NotFound();
            if (g.Source == GameRules.SourceFolder && !File.Exists(g.FilePath))
                return OpResult.Fail("O atalho de " + g.Name + " não existe mais.");
            try
            {
                ShortcutInfo lnk = g.Ext == ".lnk" && g.LaunchArgs.Trim().Length > 0 ? _folder.GetShortcut(g.FilePath) : null;
                string sourceArgs;
                lock (_gate) sourceArgs = _sourceById.TryGetValue(g.Id, out Game src) ? src.LaunchArgs ?? "" : "";
                GameLauncher.Start(GameLauncher.BuildStartInfo(g, lnk, g.Source == GameRules.SourceFolder ? "" : sourceArgs));
            }
            catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException || ex is IOException || ex is FileNotFoundException)
            {
                Log.Warn("Launch failed: " + id, ex);
                return OpResult.Fail("Não foi possível iniciar " + g.Name + ": " + ex.Message);
            }
            DateTime now = DateTime.Now;
            _store.Edit(g.Id, m => m.LastPlayed = now, 0);
            Patch(g.Id, x => x.LastPlayed = now);
            Log.Info("Launched " + g.Id);
            return OpResult.Success("Iniciando " + g.Name + "...", g.Id);
        }

        // ------------------------------------------------------------------ edit

        public OpResult Update(string id, GameEdit edit)
        {
            Game g = Get(id);
            if (g == null) return NotFound();
            if (edit == null) return OpResult.Success("Nada para alterar.", g.Id);
            string appId = null;
            if (edit.SteamAppId != null)
            {
                appId = edit.SteamAppId.Trim().Length == 0 ? "" : GameRules.NormalizeAppId(edit.SteamAppId);
                if (appId.Length == 0 && edit.SteamAppId.Trim().Length > 0) return OpResult.Fail("App ID da Steam inválido.");
            }
            string sourceName;
            lock (_gate) sourceName = _sourceById.TryGetValue(g.Id, out Game src) ? src.Name : g.Name;

            _store.Edit(g.Id, m =>
            {
                if (edit.Name != null)
                {
                    string n = edit.Name.Trim();
                    m.NameOverride = n.Length == 0 || n == sourceName ? "" : n;
                }
                if (edit.LaunchArgs != null) m.LaunchArgs = edit.LaunchArgs.Trim();
                if (appId != null) m.SteamAppIdOverride = appId;
                if (edit.Favorite.HasValue) m.Favorite = edit.Favorite.Value;
                if (edit.Hidden.HasValue) m.Hidden = edit.Hidden.Value;
                if (edit.Collections != null) m.Collections = LibraryMerge.NormalizeCollections(edit.Collections);
            });
            lock (_rebuildLock) RebuildLocked(false);
            return OpResult.Success("Alterações salvas.", g.Id);
        }

        // ------------------------------------------------------------------ add

        public OpResult AddFromFile(string path, string name)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return OpResult.Fail("Arquivo não encontrado.");
            string full;
            try { full = Path.GetFullPath(path); }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            { Log.Warn("AddFromFile bad path: " + path, ex); return OpResult.Fail("Caminho de arquivo inválido."); }
            string ext = Path.GetExtension(full).ToLowerInvariant();
            if (!GameRules.IsGameFileExt(ext)) return OpResult.Fail("Formato não suportado. Use arquivos .exe, .lnk ou .url.");
            string gamesDir = _settings.GamesDir ?? "";
            if (gamesDir.Trim().Length == 0) return OpResult.Fail("Escolha a pasta de jogos nas configurações primeiro.");

            string displayName = string.IsNullOrWhiteSpace(name) ? GameRules.CleanName(Path.GetFileName(full)) : name.Trim();
            string gamesFull = GameRules.NormalizeDir(gamesDir);
            if (LibraryMerge.IsUnder(full, gamesFull))
            {
                Game existing = Get(GameRules.FolderId(Path.GetFileName(full)));
                return OpResult.Success((existing?.Name ?? displayName) + " já está na biblioteca.", existing?.Id);
            }
            string target = ext == ".exe" ? full : ext == ".lnk" ? _folder.GetShortcut(full).Target : "";
            Game dup = target.Length > 0 ? GetGames().FirstOrDefault(x => x.Exe.Length > 0 && LibraryMerge.PathEq(x.Exe, target)) : null;
            if (dup != null) return OpResult.Success(dup.Name + " já está na biblioteca.", dup.Id);

            string created;
            try { created = ShortcutFactory.CreateFromFile(gamesDir, full, displayName); }
            catch (Exception ex)
            {
                Log.Warn("AddFromFile failed: " + full, ex);
                return OpResult.Fail("Não foi possível adicionar " + displayName + ": " + ex.Message);
            }
            Log.Info("Added shortcut " + created);
            ScanFolder();
            return OpResult.Success(displayName + " adicionado à biblioteca.", GameRules.FolderId(Path.GetFileName(created)));
        }

        public OpResult AddSteamApp(string appId, string name)
        {
            string id = GameRules.NormalizeAppId(appId);
            if (id.Length == 0) return OpResult.Fail("App ID da Steam inválido.");
            Game dup = GetGames().FirstOrDefault(x => x.SteamAppId == id);
            if (dup != null) return OpResult.Success(dup.Name + " já está na biblioteca.", dup.Id);
            string steamId = GameRules.SteamId(id);
            if (_store.IsIgnored(steamId))
            {
                _store.SetIgnored(steamId, false);
                lock (_rebuildLock) RebuildLocked(false);
                Game back = Get(steamId);
                if (back != null) return OpResult.Success(back.Name + " voltou para a biblioteca.", back.Id);
            }
            string gamesDir = _settings.GamesDir ?? "";
            if (gamesDir.Trim().Length == 0) return OpResult.Fail("Escolha a pasta de jogos nas configurações primeiro.");

            string display = (name ?? "").Trim();
            if (display.Length == 0) display = ShortcutFactory.FetchSteamName(id);
            if (display.Length == 0) display = "Steam " + id;
            string created;
            try { created = ShortcutFactory.CreateSteamUrl(gamesDir, id, display); }
            catch (Exception ex)
            {
                Log.Warn("AddSteamApp failed: " + id, ex);
                return OpResult.Fail("Não foi possível adicionar " + display + ": " + ex.Message);
            }
            ScanFolder();
            return OpResult.Success(display + " adicionado à biblioteca.", GameRules.FolderId(Path.GetFileName(created)));
        }

        // ------------------------------------------------------------------ remove / undo

        public OpResult Remove(string id)
        {
            Game g = Get(id);
            if (g == null) return NotFound();
            var entry = new UndoEntry { GameId = g.Id, Name = g.Name };
            if (g.Source == GameRules.SourceFolder)
            {
                string dir = GameRules.NormalizeDir(Path.GetDirectoryName(g.FilePath));
                if (!File.Exists(g.FilePath) || !string.Equals(dir, GameRules.NormalizeDir(_settings.GamesDir), StringComparison.OrdinalIgnoreCase))
                    return OpResult.Fail("O atalho de " + g.Name + " não está mais na pasta de jogos.");
                try { entry.TrashPath = ShortcutFactory.MoveToTrash(g.FilePath, _trashDir, DateTime.Now); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    Log.Warn("Remove failed: " + g.FilePath, ex);
                    return OpResult.Fail("Não foi possível remover " + g.Name + ": " + ex.Message);
                }
                entry.OriginalPath = g.FilePath;
                Log.Info("Moved to trash: " + g.FilePath + " -> " + entry.TrashPath);
                ScanFolder();
            }
            else
            {
                _store.SetIgnored(g.Id, true);
                entry.Unignore = true;
                lock (_rebuildLock) RebuildLocked(false);
            }
            string token = Guid.NewGuid().ToString("N");
            _undo[token] = entry;
            return OpResult.Success(g.Name + " removido da biblioteca.", g.Id, token);
        }

        public OpResult Undo(string undoToken)
        {
            if (string.IsNullOrEmpty(undoToken) || !_undo.TryRemove(undoToken, out UndoEntry e))
                return OpResult.Fail("Não há nada para desfazer.");
            if (e.Unignore)
            {
                _store.SetIgnored(e.GameId, false);
                lock (_rebuildLock) RebuildLocked(false);
                return OpResult.Success(e.Name + " restaurado.", e.GameId);
            }
            if (!File.Exists(e.TrashPath)) return OpResult.Fail("O arquivo removido não está mais na lixeira do GamesHub.");
            try
            {
                if (!ShortcutFactory.RestoreFromTrash(e.TrashPath, e.OriginalPath))
                {
                    _undo[undoToken] = e; // user may free the name and retry
                    return OpResult.Fail("Não foi possível restaurar: já existe um arquivo chamado " + Path.GetFileName(e.OriginalPath) + " na pasta de jogos.");
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Log.Warn("Undo failed: " + e.TrashPath, ex);
                _undo[undoToken] = e;
                return OpResult.Fail("Não foi possível restaurar " + e.Name + ": " + ex.Message);
            }
            Log.Info("Restored from trash: " + e.OriginalPath);
            ScanFolder();
            return OpResult.Success(e.Name + " restaurado.", e.GameId);
        }

        // ------------------------------------------------------------------ reveal

        public string RevealPath(string id)
        {
            Game g = Get(id);
            if (g == null) return "";
            if (g.Source == GameRules.SourceFolder) return File.Exists(g.FilePath) ? g.FilePath : "";
            return g.InstallDir.Length > 0 && Directory.Exists(g.InstallDir) ? g.InstallDir : "";
        }
    }
}
