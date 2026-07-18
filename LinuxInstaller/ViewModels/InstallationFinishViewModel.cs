using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LinuxInstaller.Services;
using LinuxInstaller.ViewModels.Interfaces;
using System;
using System.Threading.Tasks;

namespace LinuxInstaller.ViewModels;

public partial class InstallationFinishViewModel : NavigatableViewModelBase
{
    private readonly ProcessRunnerService _processRunner;

    [ObservableProperty]
    private bool _restartNow = true;

    public InstallationFinishViewModel(
        NavigationService navigationService,
        ProcessRunnerService processRunner)
        : base(navigationService)
    {
        _processRunner = processRunner;
    }

    [RelayCommand]
    private async Task Finish()
    {
        if (!RestartNow)
        {
            Environment.Exit(0);
            return;
        }

        var result = await _processRunner.RunAsync(
            "shutdown.exe",
            [
                "/r",
                "/t",
                "0",
                "/d",
                "p:4:1",
                "/c",
                "Starting LinuxInstallerForWindows boot installer"
            ]);
        result.EnsureSuccess("Restarting Windows");
    }

    public override bool CanProceed => true;
    public override bool CanGoBack => false;
}
