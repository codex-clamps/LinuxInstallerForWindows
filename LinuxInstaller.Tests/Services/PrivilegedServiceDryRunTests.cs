using LinuxInstaller.Services;

namespace LinuxInstaller.Tests.Services;

public class PrivilegedServiceDryRunTests
{
    [Fact]
    public async Task BootManager_DoesNotExposeWritableEspPathInDryRun()
    {
        var service = new BootManagerService();

        var espPath = await service.MountEspAsync();
        await service.CreateBcdEntryAsync("S:", "EFI\\Installer\\shimx64.efi");
        await service.UnmountEspAsync();

        Assert.True(service.IsDryRun);
        Assert.Null(espPath);
    }

    [Fact]
    public async Task Diskpart_DryRunCannotReportShrinkSuccessOrFakeTopology()
    {
        var service = new DiskpartService();

        var shrinkSucceeded = await service.ShrinkPartitionAsync("C:", 1024);
        var disks = await service.ListDisksAsync();
        var volumes = await service.ListVolumesAsync();
        var execution = await service.ExecuteScriptAsync("list disk");

        Assert.True(service.IsDryRun);
        Assert.False(shrinkSucceeded);
        Assert.Empty(disks);
        Assert.Empty(volumes);
        Assert.Equal(50, execution.ExitCode);
        Assert.Empty(execution.StandardOutput);
        Assert.Contains("disabled", execution.StandardError);
    }
}
