using LinuxInstaller.Services;
using LinuxInstaller.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace LinuxInstaller;

public static class SplatRegistrations
{
    public static void Register(IServiceCollection services)
    {
        services.AddSingleton<ProcessRunnerService>();
        services.AddSingleton<AssetManagerService>();
        services.AddSingleton<BootManagerService>();
        services.AddSingleton<ConfigGeneratorService>();
        services.AddSingleton<DiskpartService>();
        services.AddSingleton<DistroService>();
        services.AddSingleton<IStorageManager, WindowsStorageManager>();
        services.AddSingleton<PartitionService>();
        services.AddSingleton<ISystemAnalysisService, SystemAnalysisService>();
        services.AddSingleton<InstallationConfigService>();
        services.AddSingleton<ToolchainService>();
        services.AddSingleton<InstallerOrchestratorService>();
        services.AddSingleton<NavigationService>();

        services.AddSingleton<MainViewModel>();
        services.AddTransient<DistroPickerViewModel>();
        services.AddTransient<InstallationProgressViewModel>();
        services.AddTransient<InstallationSummaryViewModel>();
        services.AddTransient<LoadingViewModel>();
        services.AddTransient<PartitionEditorViewModel>();
        services.AddTransient<UserCreationViewModel>();
        services.AddTransient<WorkflowSelectionViewModel>();
        services.AddTransient<InstallationFinishViewModel>();
    }
}
