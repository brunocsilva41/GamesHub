// OWNER: INSTALL agent. Skeleton by lead — replace entirely.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GamesHub
{
    public sealed class InstallInspector : IInstallInspector
    {
        public InstallHealth CheckHealth(Game game) => new InstallHealth();
        public Task<long> GetSizeBytesAsync(Game game) => Task.FromResult(-1L);
        public long GetCachedSizeBytes(Game game) => -1;
        public List<DriveSpace> GetDrives() => new List<DriveSpace>();
        public UninstallInfo FindUninstaller(Game game) => new UninstallInfo();
        public OpResult RunUninstaller(UninstallInfo info) => OpResult.Fail("Não implementado");
    }
}
