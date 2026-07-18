using LinuxInstaller.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace LinuxInstaller.Services;

public sealed record InstallationPreparationProgress(string Status, double Percentage);

public sealed class InstallerOrchestratorService
{
    private readonly AssetManagerService _assetManager;
    private readonly BootManagerService _bootManager;
    private readonly ConfigGeneratorService _configGenerator;
    private readonly DistroService _distroService;
    private readonly InstallationConfigService _installationConfig;
    private readonly ToolchainService _toolchainService;

    public InstallerOrchestratorService(
        AssetManagerService assetManager,
        BootManagerService bootManager,
        ConfigGeneratorService configGenerator,
        DistroService distroService,
        InstallationConfigService installationConfig,
        ToolchainService toolchainService)
    {
        _assetManager = assetManager;
        _bootManager = bootManager;
        _configGenerator = configGenerator;
        _distroService = distroService;
        _installationConfig = installationConfig;
        _toolchainService = toolchainService;
    }

    public async Task PrepareInstallationAsync(
        IProgress<InstallationPreparationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (_installationConfig.SelectedInstallWorkflow != InstallWorkflowType.Distro)
        {
            throw new NotSupportedException(
                "Generic ISO boot is not supported because distributions require different kernel and initrd paths.");
        }

        var distro = _installationConfig.SelectedDistro
            ?? throw new InvalidOperationException("No Linux distribution was selected.");
        if (!_installationConfig.PartitionPlan.IsValid)
        {
            throw new InvalidOperationException("The partition plan is not valid.");
        }

        progress?.Report(new InstallationPreparationProgress(
            "Downloading verified rootfs and installer toolchains...",
            0));

        var rootfsProgress = new Progress<double>(value =>
            progress?.Report(new InstallationPreparationProgress(
                $"Downloading {distro.DistroName} root filesystem... {value:0}%",
                value * 0.55)));
        var toolchainProgress = new Progress<ToolchainProgress>(value =>
            progress?.Report(new InstallationPreparationProgress(
                value.Status,
                55 + value.Percentage * 0.25)));

        var rootfsTask = _distroService.DownloadRootfsAsync(
            distro,
            rootfsProgress,
            cancellationToken);
        var toolchainTask = _toolchainService.PrepareSessionAsync(
            toolchainProgress,
            cancellationToken);
        await Task.WhenAll(rootfsTask, toolchainTask);

        var rootfsPath = await rootfsTask;
        var toolchainSession = await toolchainTask;
        var installationId = System.Guid.NewGuid();
        var stage2Config = _configGenerator.GenerateGrubStage2Config(
            distro,
            installationId);
        var installConfiguration = _configGenerator.GenerateInstallConfiguration(
            installationId,
            distro,
            _installationConfig.PartitionPlan,
            _installationConfig.UserInfo);

        progress?.Report(new InstallationPreparationProgress(
            "Staging the boot runtime and installation payload...",
            82));
        var stagingDirectory = await _assetManager.StageInstallationAsync(
            toolchainSession,
            rootfsPath,
            stage2Config,
            installConfiguration,
            cancellationToken);

        progress?.Report(new InstallationPreparationProgress(
            "Installing GRUB on the EFI System Partition...",
            92));
        var espPath = await _bootManager.MountEspAsync(cancellationToken);
        string bootEntryId;
        try
        {
            await _assetManager.StageBootloaderAsync(
                toolchainSession,
                espPath,
                cancellationToken);
            bootEntryId = await _bootManager.CreateBcdEntryAsync(
                espPath,
                @"EFI\LinuxInstaller\grubx64.efi",
                cancellationToken);
        }
        finally
        {
            await _bootManager.UnmountEspAsync(cancellationToken);
        }

        _installationConfig.SelectedRootfsPath = rootfsPath;
        _installationConfig.ToolchainSessionDirectory = toolchainSession.DirectoryPath;
        _installationConfig.StagingDirectory = stagingDirectory;
        _installationConfig.BootEntryId = bootEntryId;

        progress?.Report(new InstallationPreparationProgress(
            "Installation payload is ready. Restart to run the Linux installer.",
            100));
    }
}
