// OWNER: DIST agent. Skeleton created by lead — replace entirely.
using System;
using System.Threading.Tasks;

namespace GamesHub
{
    public sealed class UpdateChecker : IUpdateChecker
    {
        private readonly AppSettings _settings;
        public UpdateChecker(AppSettings settings) { _settings = settings; }
        public Task<UpdateInfo> CheckAsync() => Task.FromResult(new UpdateInfo());
        public Task<bool> DownloadAndInstallAsync(UpdateInfo info, Action<int> progress) => Task.FromResult(false);
    }
}
