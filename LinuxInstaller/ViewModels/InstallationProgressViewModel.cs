using CommunityToolkit.Mvvm.ComponentModel;
using LinuxInstaller.Services;
using LinuxInstaller.ViewModels.Interfaces;
using System;
using System.Threading.Tasks;

namespace LinuxInstaller.ViewModels;

public partial class InstallationProgressViewModel : NavigatableViewModelBase
{
    private readonly InstallerOrchestratorService _installerOrchestrator;
    private bool _isRunning;

    [ObservableProperty]
    private string _statusText = "Starting installation preparation...";

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private bool _isIndeterminate = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoBack))]
    private bool _hasError;

    public InstallationProgressViewModel(
        NavigationService navigationService,
        InstallerOrchestratorService installerOrchestrator)
        : base(navigationService)
    {
        _installerOrchestrator = installerOrchestrator;
    }

    public async Task StartInstallationAsync()
    {
        if (_isRunning)
        {
            return;
        }

        _isRunning = true;
        HasError = false;
        IsIndeterminate = false;
        ProgressValue = 0;

        try
        {
            var progress = new Progress<InstallationPreparationProgress>(value =>
            {
                ProgressValue = Math.Clamp(value.Percentage, 0, 100);
                StatusText = value.Status;
            });
            await _installerOrchestrator.PrepareInstallationAsync(progress);
            ProgressValue = 100;
            await Task.Delay(400);
            Navigation.Next();
        }
        catch (Exception exception)
        {
            HasError = true;
            IsIndeterminate = false;
            StatusText = $"Installation preparation failed: {exception.Message}";
        }
        finally
        {
            _isRunning = false;
        }
    }

    public override bool CanProceed => false;
    public override bool CanGoBack => HasError;
}
