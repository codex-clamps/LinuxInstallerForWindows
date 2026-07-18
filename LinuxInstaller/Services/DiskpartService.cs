using LinuxInstaller.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LinuxInstaller.Services;

public sealed class DiskpartService
{
    private const int ErrorNotSupported = 50;

    public bool IsDryRun => true;

    public Task<bool> ShrinkPartitionAsync(
        string driveLetter,
        int sizeInMb,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(false);
    }

    public Task<IReadOnlyList<Disk>> ListDisksAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<Disk>>([]);
    }

    public Task<IReadOnlyList<string>> ListVolumesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<string>>([]);
    }

    public Task<(int ExitCode, string StandardOutput, string StandardError)> ExecuteScriptAsync(
        string scriptContent,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult((
            ErrorNotSupported,
            string.Empty,
            "DiskPart execution is disabled; dry-run only."));
    }
}
