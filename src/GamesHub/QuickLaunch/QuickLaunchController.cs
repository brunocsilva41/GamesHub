// OWNER: QUICK agent. Skeleton by lead — replace entirely.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GamesHub
{
    public sealed class QuickLaunchController : IQuickLaunch
    {
        private readonly ILibraryService _library;
        private readonly AppSettings _settings;
        public QuickLaunchController(ILibraryService library, AppSettings settings) { _library = library; _settings = settings; }
        public OpResult Start() => OpResult.Fail("Não implementado");
        public OpResult ApplyHotkey(string hotkey) => OpResult.Fail("Não implementado");
        public void Show() { }
        public void Dispose() { }
    }
}
