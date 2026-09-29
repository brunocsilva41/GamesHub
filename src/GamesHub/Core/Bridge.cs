using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace GamesHub
{
    /// <summary>Outcome of one bridge command.</summary>
    internal sealed class BridgeResult
    {
        public bool Ok;
        public object Data;
        public string Error;

        public static BridgeResult Success(object data = null) => new BridgeResult { Ok = true, Data = data };
        public static BridgeResult Fail(string error) => new BridgeResult { Ok = false, Error = error };
        public static BridgeResult From(OpResult r)
        {
            if (r == null) return Fail("Algo deu errado.");
            return new BridgeResult { Ok = r.Ok, Data = BridgeDto.OpResultData(r), Error = r.Ok ? null : r.Message };
        }
    }

    /// <summary>
    /// C# side of the JS bridge (docs/ARCHITECTURE.md): parses requests, dispatches commands, replies
    /// exactly once per request and pushes events. Transport calls happen on the UI thread.
    /// </summary>
    internal sealed partial class Bridge : IDisposable
    {
        private const int GamesThrottleMs = 250; // ≤ 4 "games" events per second

        private readonly AppController _app;
        private readonly EventCoalescer _gamesPush;
        private volatile CoreWebView2 _core;
        private bool _disposed;

        public Bridge(AppController app)
        {
            _app = app;
            _gamesPush = new EventCoalescer(PushGames, GamesThrottleMs);
            _app.Catalog.Changed += OnLibraryChanged;
            _app.Library.RunningChanged += OnRunningChanged;
        }

        public void Attach(CoreWebView2 core)
        {
            _core = core;
            core.WebMessageReceived += OnWebMessage;
        }

        public void SignalGames() => _gamesPush.Signal();

        private void OnLibraryChanged() => _gamesPush.Signal();

        private void OnRunningChanged(string id, bool running)
            => Emit("running", new Dictionary<string, object> { ["id"] = id, ["running"] = running });

        // ------------------------------------------------------------------ outgoing

        /// <summary>Sends an event to the page. Safe from any thread; dropped if the page isn't ready.</summary>
        public void Emit(string name, object data) => Post(BridgeDto.Event(name, data));

        public void Toast(string text, string kind = "info", string undoToken = null)
        {
            var data = new Dictionary<string, object> { ["text"] = text, ["kind"] = kind };
            if (undoToken != null) data["undoToken"] = undoToken;
            Emit("toast", data);
        }

        private void Post(object message)
        {
            if (_core == null || _disposed) return;
            string json;
            try
            {
                json = Json.Serialize(message);
            }
            catch (Exception ex)
            {
                Log.Error("Bridge: could not serialize message", ex);
                return;
            }
            _app.RunOnUi(() => PostJson(json));
        }

        private void PostJson(string json)
        {
            CoreWebView2 core = _core;
            if (core == null || _disposed) return;
            try
            {
                core.PostWebMessageAsJson(json);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is System.Runtime.InteropServices.COMException)
            {
                Log.Warn("Bridge: PostWebMessageAsJson failed", ex);
            }
        }

        private void PushGames()
        {
            if (_core == null || _disposed) return;
            Emit("games", GamesPayload());
        }

        /// <summary>Builds { games, collections }. Touches the disk (art stamps): call off the UI thread.</summary>
        internal Dictionary<string, object> GamesPayload()
        {
            List<Game> games = _app.Catalog.GetGames() ?? new List<Game>();
            return new Dictionary<string, object>
            {
                ["games"] = games.Select(g => BridgeDto.Game(g, BridgeDto.FileStamp)).ToList(),
                ["collections"] = _app.Library.GetCollections() ?? new List<string>(),
            };
        }

        // ------------------------------------------------------------------ incoming

        private void OnWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            if (!BridgeDto.IsAppUri(e.Source))
            {
                Log.Warn("Bridge: ignored message from " + ShellActions.Truncate(e.Source));
                return;
            }

            IDictionary<string, object> msg;
            List<string> files;
            try
            {
                msg = Json.DeserializeObject(e.WebMessageAsJson) as IDictionary<string, object>;
                files = DroppedFiles(e); // only valid during this event
            }
            catch (Exception ex)
            {
                Log.Warn("Bridge: malformed message", ex);
                return;
            }
            if (msg == null || Json.Str(msg, "type") != "cmd") return;
            if (!msg.TryGetValue("id", out object id) || id == null)
            {
                Log.Warn("Bridge: command without id: " + Json.Str(msg, "name"));
                return;
            }
            string name = Json.Str(msg, "name");
            IDictionary<string, object> args = Json.Obj(msg, "args") ?? new Dictionary<string, object>();
            Dispatch(id, name, args, files);
        }

        private static List<string> DroppedFiles(CoreWebView2WebMessageReceivedEventArgs e)
        {
            var paths = new List<string>();
            IReadOnlyList<object> objects = e.AdditionalObjects;
            if (objects == null) return paths;
            foreach (object o in objects)
                if (o is CoreWebView2File f && !string.IsNullOrEmpty(f.Path)) paths.Add(f.Path);
            return paths;
        }

        /// <summary>Runs a command and replies exactly once. Continuations resume on the UI thread.</summary>
        private async void Dispatch(object id, string name, IDictionary<string, object> args, List<string> files)
        {
            BridgeResult result;
            try
            {
                result = await Execute(name, args, files) ?? BridgeResult.Success();
            }
            catch (BridgeException ex)
            {
                result = BridgeResult.Fail(ex.Message);
            }
            catch (Exception ex)
            {
                Log.Error("Bridge: command '" + name + "' failed", ex);
                result = BridgeResult.Fail("Ocorreu um erro inesperado. Tente novamente.");
            }
            Post(BridgeDto.Reply(id, result.Ok, result.Data, result.Error));
        }

        // ------------------------------------------------------------------ arg helpers

        private static string RequireString(IDictionary<string, object> args, string key)
        {
            string v = Json.Str(args, key).Trim();
            if (v.Length == 0) throw new BridgeException("Parâmetro obrigatório ausente: " + key + ".");
            return v;
        }

        private Game RequireGame(IDictionary<string, object> args)
        {
            Game g = _app.Library.Get(RequireString(args, "id"));
            if (g == null) throw new BridgeException("Jogo não encontrado.");
            return g;
        }

        private static string RequireArtKind(IDictionary<string, object> args)
        {
            string kind = RequireString(args, "kind").ToLowerInvariant();
            if (Array.IndexOf(BridgeDto.ArtKinds, kind) < 0) throw new BridgeException("Tipo de imagem inválido.");
            return kind;
        }

        private static Dictionary<string, object> Empty() => new Dictionary<string, object>();

        private static Dictionary<string, object> Cancelled() => new Dictionary<string, object> { ["cancelled"] = true };

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _app.Catalog.Changed -= OnLibraryChanged;
            _app.Library.RunningChanged -= OnRunningChanged;
            _gamesPush.Dispose();
            CoreWebView2 core = _core;
            _core = null;
            if (core == null) return;
            try
            {
                core.WebMessageReceived -= OnWebMessage;
            }
            catch (InvalidOperationException ex)
            {
                Log.Warn("Bridge: WebView already gone on dispose", ex);
            }
        }

        private static Task<BridgeResult> Done(BridgeResult r) => Task.FromResult(r);
    }
}
