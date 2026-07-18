using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LinuxInstaller.Models;
using LinuxInstaller.Services;
using LinuxInstaller.ViewModels.Interfaces;
using LinuxInstaller.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace LinuxInstaller.ViewModels;

public partial class DistroPickerViewModel : NavigatableViewModelBase
{
    private readonly DistroService _distroService;
    private readonly InstallationConfigService _installationConfigService;
    private readonly PartitionService _partitionService;

    public string Title => "Select a Distribution";
    public string Subtitle => "Choose a Linux distribution to install on your machine.";

    [ObservableProperty]
    private ObservableCollection<Distro> _distros;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanProceed))]
    private Distro? _selectedDistro;

    [ObservableProperty]
    private string _searchText;

    private List<Distro> _allDistros;
    private Distro? _previouslySelectedDistro;

    public DistroPickerViewModel(
        NavigationService navigationService,
        DistroService distroService,
        InstallationConfigService installationConfigService,
        PartitionService partitionService)
        : base(navigationService)
    {
        _distroService = distroService;
        _installationConfigService = installationConfigService;
        _partitionService = partitionService;
        _distros = [];
        _searchText = string.Empty;
        _allDistros = [];
        _ = LoadDistrosAsync();
    }

    private async Task LoadDistrosAsync()
    {
        _allDistros = (await _distroService.GetDistros()).ToList();
        Distros = new ObservableCollection<Distro>(_allDistros);
    }

    partial void OnSearchTextChanged(string value)
    {
        IEnumerable<Distro> result = _allDistros;
        if (!string.IsNullOrWhiteSpace(value))
        {
            result = _allDistros.Where(distro =>
                distro.DistroName.Contains(value, StringComparison.OrdinalIgnoreCase));
        }

        Distros = new ObservableCollection<Distro>(result);
    }

    [RelayCommand]
    private async Task SelectDistroAsync(Distro distro)
    {
        SelectedDistro = distro;
        _installationConfigService.SelectedDistro = distro;

        var options = new List<DialogOption<PartitionWorkflowType>>
        {
            new()
            {
                Label = "Manual Partitioning",
                Value = PartitionWorkflowType.Manual,
                ButtonStyles = new() { Variant = ButtonVariant.Tonal, Size = ButtonSize.Large }
            },
            new()
            {
                Label = "Automatic Partitioning",
                Value = PartitionWorkflowType.Automatic,
                ButtonStyles = new() { Variant = ButtonVariant.Filled, Size = ButtonSize.Large }
            }
        };

        var dialog = new MultiOptionDialogView();
        dialog.DataContext = new MultiOptionDialogViewModel<PartitionWorkflowType>(
            "Partitioning Options",
            "How would you like to manage your disk partitions?",
            options,
            dialog);

        if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
            desktop.MainWindow == null)
        {
            return;
        }

        var workflow = await dialog.ShowDialog<PartitionWorkflowType>(desktop.MainWindow);

        if (workflow == PartitionWorkflowType.Automatic)
        {
            await SelectAutomaticPartitioningAsync(desktop);
        }
        else if (workflow == PartitionWorkflowType.Manual)
        {
            _installationConfigService.SelectedPartitionWorkflow = PartitionWorkflowType.Manual;
            Navigation.Next();
        }
    }

    private async Task SelectAutomaticPartitioningAsync(
        IClassicDesktopStyleApplicationLifetime desktop)
    {
        AutomaticPartitionPlanResult result;
        try
        {
            var disks = await _partitionService.GetAvailableDisksAsync(forceRefresh: true);
            result = AutomaticPartitionPlanner.CreatePlan(disks);
        }
        catch (Exception)
        {
            await ShowMessageAsync(
                desktop,
                "Storage Discovery Failed",
                "Windows storage information could not be read. Relaunch as administrator and try again.");
            return;
        }

        if (!result.IsSuccess || result.TargetDisk == null || result.RootPartition == null)
        {
            var message = result.Failure == AutomaticPartitionPlanFailure.NoEligibleDisk
                ? "No online, writable internal GPT disk is eligible for installation."
                : "No eligible disk has at least 16 GiB of contiguous unallocated space. Prepare free space in Windows Disk Management and try again.";
            await ShowMessageAsync(
                desktop,
                "Automatic Partitioning Unavailable",
                message);
            return;
        }

        TryApplyAutomaticPartitionPlan(result);
        Navigation.Next();
    }

    internal bool TryApplyAutomaticPartitionPlan(AutomaticPartitionPlanResult result)
    {
        if (!result.IsSuccess ||
            result.TargetDisk is not { } targetDisk ||
            result.RootPartition is not { } rootPartition)
        {
            return false;
        }

        var plan = new PartitionPlan { TargetDisk = targetDisk };
        plan.AddPartition(rootPartition);
        _installationConfigService.PartitionPlan = plan;
        _installationConfigService.SelectedPartitionWorkflow = PartitionWorkflowType.Automatic;
        return true;
    }

    private static async Task ShowMessageAsync(
        IClassicDesktopStyleApplicationLifetime desktop,
        string title,
        string message)
    {
        if (desktop.MainWindow == null)
        {
            return;
        }

        var dialog = new MultiOptionDialogView();
        var options = new List<DialogOption<bool>>
        {
            new()
            {
                Label = "OK",
                Value = true,
                ButtonStyles = new() { Variant = ButtonVariant.Filled, Size = ButtonSize.Large }
            }
        };
        dialog.DataContext = new MultiOptionDialogViewModel<bool>(
            title,
            message,
            options,
            dialog);

        await dialog.ShowDialog<bool>(desktop.MainWindow);
    }

    partial void OnSelectedDistroChanged(Distro? value)
    {
        if (_previouslySelectedDistro != null)
        {
            _previouslySelectedDistro.IsSelected = false;
        }

        if (value != null)
        {
            value.IsSelected = true;
        }
        else
        {
            _installationConfigService.SelectedDistro = null;
        }

        _previouslySelectedDistro = value;
    }

    public override bool CanProceed => SelectedDistro != null;
    public override bool CanGoBack => true;

    [RelayCommand]
    private void Next()
    {
        Navigation.Next();
    }

    [RelayCommand]
    private void Back()
    {
        Navigation.Previous();
    }
}
