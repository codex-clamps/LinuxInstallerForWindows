using LinuxInstaller.Models;
using LinuxInstaller.Services;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LinuxInstaller.Tests.Services;

public class PrivilegedServiceConfigurationTests
{
    [Fact]
    public void BootManager_IsConfiguredForRealExecution()
    {
        var service = new BootManagerService(new ProcessRunnerService());

        Assert.False(service.IsDryRun);
    }

    [Fact]
    public void Diskpart_IsConfiguredForRealExecution()
    {
        var service = new DiskpartService(
            new ProcessRunnerService(),
            new EmptyStorageManager());

        Assert.False(service.IsDryRun);
    }

    private sealed class EmptyStorageManager : IStorageManager
    {
        public Task<IReadOnlyList<Disk>> GetDisksAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<Disk>>([]);
        }
    }
}
