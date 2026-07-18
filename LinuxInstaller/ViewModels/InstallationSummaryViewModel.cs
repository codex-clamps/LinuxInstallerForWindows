using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LinuxInstaller.ViewModels.Interfaces;
using LinuxInstaller.Services; // Add this using directive
using LinuxInstaller.Models; // Add this using directive
using System.Collections.Generic;
using LinuxInstaller.Views;
using Avalonia.Controls.ApplicationLifetimes;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using System.Linq;
using LinuxInstaller.Converters;

namespace LinuxInstaller.ViewModels;

public partial class InstallationSummaryViewModel : NavigatableViewModelBase
{
    private readonly InstallationConfigService _installationConfigService;

    public InstallationSummaryViewModel(NavigationService navigationService, InstallationConfigService installationConfigService) : base(navigationService)
    {
        _installationConfigService = installationConfigService;
    }

    public string Title => "Installation Summary";
    public string Subtitle => "Review your selections before proceeding.";

    public Distro? SelectedDistro => _installationConfigService.SelectedDistro;
    public PartitionWorkflowType SelectedWorkflow => _installationConfigService.SelectedPartitionWorkflow;
    public UserInfo UserInfo => _installationConfigService.UserInfo;
    public PartitionPlan PartitionPlan => _installationConfigService.PartitionPlan;

    public bool IsDistroSelected => SelectedDistro != null;
    public bool IsWorkflowSelected => SelectedWorkflow != default;
    public bool IsUserInfoAvailable => UserInfo != null;
    public bool IsPartitionPlanAvailable => PartitionPlan.IsValid;

    public List<KeyValuePair<string, string>> PartitionSummaryContent
    {
        get
        {
            var content = new List<KeyValuePair<string, string>>();
            if (PartitionPlan.TargetDisk is not { } targetDisk ||
                PartitionPlan.PartitionHistory.Count == 0)
            {
                return content;
            }

            content.Add(new("Target Disk", targetDisk.Name));
            foreach (var part in PartitionPlan.PartitionHistory.Last().Where(p => !string.IsNullOrWhiteSpace(p.MountPoint)))
            {
                content.Add(new($"Mountpoint {part.MountPoint}", $"{part.Name} - {FS.ToString(part.FileSystem)} - {FileSizeConverter.ToUnit(part.Size)}"));
            }

            return content;
        }
    }

    public ObservableCollection<SummaryItem> SummaryItems
    {
        get
        {
            if (_installationConfigService.SelectedInstallWorkflow == InstallWorkflowType.Iso)
            {
                return [
                    new SummaryItem
                    {
                        Title = "Selected ISO Image",
                        Icon = "\uE019",
                        Content = [
                            new KeyValuePair<string, string>(
                                "Path",
                                _installationConfigService.SelectedIsoPath ?? "No ISO image selected")
                        ],
                        Action = new() {
                            Label = "Edit",
                            Icon = "\uE3C9",
                            Callback = BackToStartCommand,
                        }
                    }
                ];
            }

            var distroContent = SelectedDistro is { } distro
                ? new List<KeyValuePair<string, string>>
                {
                    new("Name", distro.DistroName),
                    new("Description", distro.Description),
                    new("Size", FileSizeConverter.ToUnit(distro.Size))
                }
                :
                [
                    new("Status", "No distribution selected")
                ];

            return [
                new SummaryItem
                {
                    Title = "Distro",
                    Icon = "\uE019",
                    Content = distroContent,
                    Action = new() {
                        Label = "Edit",
                        Icon = "\uE3C9",
                        Callback = GoToDistroPickerCommand,
                    }
                },
                new SummaryItem
                {
                    Title = "User Account",
                    Icon = "\uE31E",
                    Content = [
                        new KeyValuePair<string, string>("Full Name", UserInfo.FullName ?? UserInfo.Username),
                        new KeyValuePair<string, string>("Username", UserInfo.Username),
                        new KeyValuePair<string, string>("Auto Login", UserInfo.AutoLogin.ToString()),
                    ],
                    Action = new() {
                        Label = "Edit",
                        Icon = "\uE3C9",
                        Callback = GoToUserEditCommand,
                    }
                },
                new SummaryItem
                {
                    Title = "Partitions",
                    Icon = "\uE161",
                    Content = PartitionSummaryContent,
                    Action = new() {
                        Label = "Edit",
                        Icon = "\uE3C9",
                        Callback = GoToPartitionEditorCommand,
                    }
                }
            ];
        }
        set { }
    }

    [RelayCommand]
    private void BackToStart()
    {
        Navigation.Reset();
    }

    [RelayCommand]
    private void GoToDistroPicker()
    {
        Navigation.Goto("distroPicker");
    }

    [RelayCommand]
    private void GoToUserEdit()
    {
        Navigation.Goto("userCreation");
    }

    [RelayCommand]
    private void GoToPartitionEditor()
    {
        Navigation.Goto("partitionEditor");
    }

    [RelayCommand]
    private async Task Install()
    {
        if (!CanProceed)
        {
            return;
        }

        var dialog = new ConfirmationDialogView();
        dialog.DataContext = new ConfirmationDialogViewModel("This will start the installation process.\nAre you sure you want to continue?", dialog);

        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow is { } mainWindow)
        {
            bool result = await dialog.ShowDialog<bool>(owner: mainWindow);
            if (result)
            {
                Navigation.Next();
            }
        }
    }

    [RelayCommand]
    private void Back()
    {
        Navigation.Previous();
    }

    // INavigatableViewModel Implementation
    public override bool CanProceed => _installationConfigService.SelectedInstallWorkflow switch
    {
        InstallWorkflowType.Iso =>
            !string.IsNullOrWhiteSpace(_installationConfigService.SelectedIsoPath),
        InstallWorkflowType.Distro =>
            SelectedDistro != null &&
            PartitionPlan.IsValid &&
            !string.IsNullOrWhiteSpace(UserInfo.Username) &&
            !string.IsNullOrWhiteSpace(UserInfo.Password) &&
            UserInfo.Password == UserInfo.ConfirmPassword,
        _ => false
    };
    public override bool CanGoBack => true; // Assume always can go back to review/edit
}
