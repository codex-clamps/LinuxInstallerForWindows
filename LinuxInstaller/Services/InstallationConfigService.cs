using CommunityToolkit.Mvvm.ComponentModel;
using LinuxInstaller.Models;

namespace LinuxInstaller.Services;

public partial class InstallationConfigService : ObservableObject
{
    private InstallWorkflowType _selectedInstallWorkflow = InstallWorkflowType.None;
    public InstallWorkflowType SelectedInstallWorkflow
    {
        get => _selectedInstallWorkflow;
        set => SetProperty(ref _selectedInstallWorkflow, value);
    }

    private string? _selectedIsoPath;
    public string? SelectedIsoPath
    {
        get => _selectedIsoPath;
        set => SetProperty(ref _selectedIsoPath, value);
    }

    private Distro? _selectedDistro;
    public Distro? SelectedDistro
    {
        get => _selectedDistro;
        set => SetProperty(ref _selectedDistro, value);
    }

    private string? _selectedRootfsPath;
    public string? SelectedRootfsPath
    {
        get => _selectedRootfsPath;
        set => SetProperty(ref _selectedRootfsPath, value);
    }

    private string? _toolchainSessionDirectory;
    public string? ToolchainSessionDirectory
    {
        get => _toolchainSessionDirectory;
        set => SetProperty(ref _toolchainSessionDirectory, value);
    }

    private string? _stagingDirectory;
    public string? StagingDirectory
    {
        get => _stagingDirectory;
        set => SetProperty(ref _stagingDirectory, value);
    }

    private string? _bootEntryId;
    public string? BootEntryId
    {
        get => _bootEntryId;
        set => SetProperty(ref _bootEntryId, value);
    }

    private PartitionWorkflowType _selectedPartitionWorkflow = PartitionWorkflowType.None;
    public PartitionWorkflowType SelectedPartitionWorkflow
    {
        get => _selectedPartitionWorkflow;
        set => SetProperty(ref _selectedPartitionWorkflow, value);
    }

    private UserInfo _userInfo = new();
    public UserInfo UserInfo
    {
        get => _userInfo;
        set => SetProperty(ref _userInfo, value);
    }

    private PartitionPlan _partitionPlan = new();
    public PartitionPlan PartitionPlan
    {
        get => _partitionPlan;
        set => SetProperty(ref _partitionPlan, value);
    }
}
