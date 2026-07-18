using LinuxInstaller.Models;
using System;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LinuxInstaller.Services;

public sealed class AssetManagerService
{
    private readonly ProcessRunnerService _processRunner;

    public AssetManagerService(ProcessRunnerService processRunner)
    {
        _processRunner = processRunner;
    }

    public string StagingDirectory
    {
        get
        {
            var systemDrive = Environment.GetEnvironmentVariable("SystemDrive");
            if (string.IsNullOrWhiteSpace(systemDrive))
            {
                throw new InvalidOperationException("The Windows system drive could not be determined.");
            }

            return systemDrive.TrimEnd('\\') + "\\.myinstaller";
        }
    }

    public async Task<string> StageInstallationAsync(
        ToolchainSession session,
        string rootfsPath,
        string stage2Config,
        string installConfiguration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootfsPath);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Installation staging is only supported on Windows.");
        }

        Directory.CreateDirectory(StagingDirectory);
        await ProtectStagingDirectoryAsync(cancellationToken);

        await CopyFileAsync(
            rootfsPath,
            Path.Combine(StagingDirectory, "rootfs.tar.zst"),
            cancellationToken);
        await CopyFileAsync(
            session.GetArtifactPath(ToolchainService.FilesystemToolsArtifactId),
            Path.Combine(StagingDirectory, "filesystem-tools.tar.zst"),
            cancellationToken);
        await CopyFileAsync(
            session.GetArtifactPath(ToolchainService.InstallerKernelArtifactId),
            Path.Combine(StagingDirectory, "installer.vmlinuz"),
            cancellationToken);
        await CopyFileAsync(
            session.GetArtifactPath(ToolchainService.InstallerInitramfsArtifactId),
            Path.Combine(StagingDirectory, "installer.initrd"),
            cancellationToken);

        await File.WriteAllTextAsync(
            Path.Combine(StagingDirectory, "stage2.cfg"),
            stage2Config,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(StagingDirectory, "install.json"),
            installConfiguration,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken);

        return StagingDirectory;
    }

    public async Task<string> StageBootloaderAsync(
        ToolchainSession session,
        string espPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(espPath);

        var targetDirectory = Path.Combine(espPath, "EFI", "LinuxInstaller");
        Directory.CreateDirectory(targetDirectory);
        var targetPath = Path.Combine(targetDirectory, "grubx64.efi");
        await CopyFileAsync(
            session.GetArtifactPath(ToolchainService.GrubArtifactId),
            targetPath,
            cancellationToken);
        return targetPath;
    }

    private async Task ProtectStagingDirectoryAsync(CancellationToken cancellationToken)
    {
        var currentSid = WindowsIdentity.GetCurrent().User?.Value
            ?? throw new InvalidOperationException("The current Windows user SID is unavailable.");
        var result = await _processRunner.RunAsync(
            "icacls.exe",
            [
                StagingDirectory,
                "/inheritance:r",
                "/grant:r",
                $"*{currentSid}:(OI)(CI)F",
                "*S-1-5-18:(OI)(CI)F",
                "*S-1-5-32-544:(OI)(CI)F"
            ],
            cancellationToken);
        result.EnsureSuccess("Protecting the installation staging directory");
    }

    private static async Task CopyFileAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("A required installation asset is missing.", sourcePath);
        }

        await using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var destination = new FileStream(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await source.CopyToAsync(destination, cancellationToken);
        await destination.FlushAsync(cancellationToken);
    }
}
