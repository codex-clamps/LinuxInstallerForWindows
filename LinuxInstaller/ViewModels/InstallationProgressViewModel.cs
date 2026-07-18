using CommunityToolkit.Mvvm.ComponentModel;
using LinuxInstaller.Models;
using LinuxInstaller.Services;
using LinuxInstaller.ViewModels.Interfaces;
using System;
using System.Threading.Tasks;

namespace LinuxInstaller.ViewModels;

public partial class InstallationProgressViewModel : NavigatableViewModelBase
{
    private readonly DistroService _distroService;
    private readonly InstallationConfigService _installationConfigService;
    private bool _isRunning;

    [ObservableProperty]
    private string _statusText = "Starting installation...";

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private bool _isIndeterminate = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoBack))]
    private bool _hasError;

    public InstallationProgressViewModel(
        NavigationService navigationService,
        DistroService distroService,
        InstallationConfigService installationConfigService)
        : base(navigationService)
    {
        _distroService = distroService;
        _installationConfigService = installationConfigService;
    }

    public async Task StartInstallationAsync()
    {
        if (_isRunning)
        {
            return;
        }

        _isRunning = true;
        HasError = false;

        try
        {
            if (_installationConfigService.SelectedInstallWorkflow != InstallWorkflowType.Distro)
            {
                await RunIsoPreparationAsync();
                return;
            }

            var distro = _installationConfigService.SelectedDistro
                ?? throw new InvalidOperationException("No Linux distribution was selected.");

            IsIndeterminate = false;
            ProgressValue = 0;
            StatusText = $"Downloading {distro.DistroName} root filesystem...";

            var progress = new Progress<double>(value =>
            {
                ProgressValue = Math.Clamp(value, 0, 100);
                StatusText = $"Downloading {distro.DistroName} root filesystem... {ProgressValue:0}%";
            });

            _installationConfigService.SelectedRootfsPath = await _distroService.DownloadRootfsAsync(
                distro,
                progress);

            ProgressValue = 100;
            StatusText = $"{distro.DistroName} root filesystem downloaded and verified.";
            await Task.Delay(500);
            Navigation.Next();
        }
        catch (Exception exception)
        {
            HasError = true;
            IsIndeterminate = false;
            StatusText = $"Download failed: {exception.Message}";
        }
        finally
        {
            _isRunning = false;
        }
    }

    private async Task RunIsoPreparationAsync()
    {
        IsIndeterminate = true;
        StatusText = "Preparing the selected ISO...";
        await Task.Delay(1000);
        StatusText = "ISO is ready for installation.";
        Navigation.Next();
    }

    public override bool CanProceed => false;
    public override bool CanGoBack => HasError;
}
