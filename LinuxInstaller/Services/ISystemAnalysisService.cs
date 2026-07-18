using LinuxInstaller.Models;
using System.Threading;
using System.Threading.Tasks;

namespace LinuxInstaller.Services;

public interface ISystemAnalysisService
{
    Task<SystemAnalysisSnapshot> AnalyzeAsync(
        string? driveLetter = null,
        CancellationToken cancellationToken = default);

    Task RelaunchAsAdminAsync(CancellationToken cancellationToken = default);
}
