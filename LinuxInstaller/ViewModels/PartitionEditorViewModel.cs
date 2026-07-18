using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LinuxInstaller.Models;
using LinuxInstaller.Services;
using LinuxInstaller.ViewModels.Interfaces;
using LinuxInstaller.Views;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace LinuxInstaller.ViewModels;

public partial class PartitionEditorViewModel : NavigatableViewModelBase
{
    private readonly PartitionService _partitionService;
    private readonly InstallationConfigService _installationConfigService;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanProceed))]
    private ObservableCollection<Disk> _disks;

    [ObservableProperty]
    private ObservableCollection<ChartSpace> _diskLayoutChart;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAddPartition))]
    [NotifyPropertyChangedFor(nameof(CanEditPartition))]
    [NotifyPropertyChangedFor(nameof(CanDeletePartition))]
    private ChartSpace? _selectedChartSpace;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanProceed))]
    private PartitionPlan _plan = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanProceed))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanProceed))]
    private string? _errorMessage;

    public PartitionEditorViewModel(
        NavigationService navigationService,
        PartitionService partitionService,
        InstallationConfigService installationConfigService)
        : base(navigationService)
    {
        _partitionService = partitionService;
        _installationConfigService = installationConfigService;
        _disks = [];
        _diskLayoutChart = [];
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public Disk? SelectedDisk
    {
        get => Plan.TargetDisk;
        set
        {
            var oldDisk = Plan.TargetDisk;
            if (oldDisk == value)
            {
                return;
            }

            if (oldDisk == null || Plan.PartitionHistory.Count <= 1)
            {
                ApplySelectedDisk(value);
                return;
            }

            var dialog = new ConfirmationDialogView();
            dialog.DataContext = new ConfirmationDialogViewModel(
                "You have unsaved partition-plan changes. Discard them and switch disks?",
                dialog);

            if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
                desktop.MainWindow == null)
            {
                OnPropertyChanged();
                return;
            }

            dialog.ShowDialog<bool>(desktop.MainWindow).ContinueWith(task =>
            {
                if (task.Status == TaskStatus.RanToCompletion && task.Result)
                {
                    ApplySelectedDisk(value);
                }
                else
                {
                    OnPropertyChanged(nameof(SelectedDisk));
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }
    }

    public override bool CanProceed =>
        !IsLoading &&
        !HasError &&
        Plan.IsValid &&
        Plan.TargetDisk != null &&
        Disks.Any(disk => ReferenceEquals(disk, Plan.TargetDisk));
    public override bool CanGoBack => true;

    public bool CanAddPartition => SelectedChartSpace is ChartFreeSpace;
    public bool CanEditPartition =>
        SelectedChartSpace is ChartPartition partition && !partition.Partition.IsProtected;
    public bool CanDeletePartition =>
        SelectedChartSpace is ChartPartition partition && !partition.Partition.IsProtected;

    partial void OnErrorMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(HasError));
    }

    public Task ActivateAsync()
    {
        return LoadDisksAsync();
    }

    [RelayCommand]
    private async Task RefreshDisksAsync()
    {
        await LoadDisksAsync(forceRefresh: true);
    }

    private async Task LoadDisksAsync(bool forceRefresh = false)
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var availableDisks = await _partitionService.GetAvailableDisksAsync(forceRefresh);
            var eligibleDisks = availableDisks
                .Where(disk => disk.IsEligibleForInstallation)
                .ToList();

            Disks = new ObservableCollection<Disk>(eligibleDisks);
            if (Disks.Count == 0)
            {
                SelectedChartSpace = null;
                DiskLayoutChart = [];
                OnPropertyChanged(nameof(SelectedDisk));
                ErrorMessage = "No online, writable internal GPT disk is eligible for installation.";
                return;
            }

            var reusablePlan = GetReusablePlan(eligibleDisks);
            if (reusablePlan != null)
            {
                var matchingDisk = eligibleDisks.First(disk =>
                    string.Equals(
                        disk.Id,
                        reusablePlan.TargetDisk!.Id,
                        StringComparison.OrdinalIgnoreCase));
                Plan = RebasePlan(reusablePlan, matchingDisk);
                SelectedChartSpace = null;
                OnPropertyChanged(nameof(SelectedDisk));
                UpdateChart();
                OnPropertyChanged(nameof(CanProceed));
                return;
            }

            var selectedDisk = SelectedDisk == null
                ? null
                : Disks.FirstOrDefault(disk =>
                    string.Equals(
                        disk.Id,
                        SelectedDisk.Id,
                        StringComparison.OrdinalIgnoreCase));
            ApplySelectedDisk(selectedDisk ?? Disks.First());
        }
        catch (Exception)
        {
            Disks = [];
            SelectedChartSpace = null;
            OnPropertyChanged(nameof(SelectedDisk));
            ErrorMessage = "Windows storage information could not be loaded. Relaunch as administrator and retry.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private PartitionPlan? GetReusablePlan(System.Collections.Generic.IReadOnlyList<Disk> eligibleDisks)
    {
        if (CanReusePlan(Plan, eligibleDisks))
        {
            return Plan;
        }

        var configuredPlan = _installationConfigService.PartitionPlan;
        return CanReusePlan(configuredPlan, eligibleDisks)
            ? configuredPlan
            : null;
    }

    private static bool CanReusePlan(
        PartitionPlan plan,
        System.Collections.Generic.IReadOnlyList<Disk> eligibleDisks)
    {
        return plan.TargetDisk != null &&
            plan.PartitionHistory.Count > 0 &&
            eligibleDisks.Any(disk =>
                string.Equals(
                    disk.Id,
                    plan.TargetDisk.Id,
                    StringComparison.OrdinalIgnoreCase));
    }

    private static PartitionPlan RebasePlan(PartitionPlan source, Disk targetDisk)
    {
        var plannedPartitions = source.PartitionHistory.Last()
            .Where(partition => !partition.IsExisting)
            .Select(partition => partition.Clone())
            .ToList();
        var rebasedPlan = new PartitionPlan
        {
            TargetDisk = targetDisk
        };

        foreach (var partition in plannedPartitions)
        {
            partition.DiskId = targetDisk.Id;
            rebasedPlan.AddPartition(partition);
        }

        return rebasedPlan;
    }

    private void ApplySelectedDisk(Disk? disk)
    {
        Plan.TargetDisk = disk;
        SelectedChartSpace = null;
        OnPropertyChanged(nameof(SelectedDisk));
        UpdateChart();
        OnPropertyChanged(nameof(CanProceed));
    }

    private void UpdateChart()
    {
        if (SelectedDisk == null ||
            Plan.PartitionHistory.Count == 0 ||
            SelectedDisk.Size <= AutomaticPartitionPlanner.PartitionAlignmentBytes * 2)
        {
            DiskLayoutChart = [];
            return;
        }

        var usableDiskStart = AutomaticPartitionPlanner.PartitionAlignmentBytes;
        var usableDiskEnd = AlignDown(
            SelectedDisk.Size - AutomaticPartitionPlanner.PartitionAlignmentBytes,
            AutomaticPartitionPlanner.PartitionAlignmentBytes);
        if (usableDiskEnd <= usableDiskStart)
        {
            DiskLayoutChart = [];
            return;
        }

        var newLayout = new ObservableCollection<ChartSpace>();
        var sortedPartitions = Plan.PartitionHistory.Last()
            .Where(partition =>
                partition.Size > 0 &&
                partition.StartOffset < usableDiskEnd &&
                GetSafeEndOffset(partition) > usableDiskStart)
            .OrderBy(partition => partition.StartOffset)
            .ToList();
        var lastPosition = usableDiskStart;

        foreach (var partition in sortedPartitions)
        {
            var partitionStart = Math.Max(partition.StartOffset, usableDiskStart);
            var partitionEnd = Math.Min(GetSafeEndOffset(partition), usableDiskEnd);
            if (partitionEnd <= partitionStart)
            {
                continue;
            }

            AddAlignedFreeSpace(newLayout, lastPosition, partitionStart);

            newLayout.Add(new ChartPartition
            {
                Start = partitionStart,
                Size = partitionEnd - partitionStart,
                Partition = partition
            });
            lastPosition = Math.Max(lastPosition, partitionEnd);
        }

        AddAlignedFreeSpace(newLayout, lastPosition, usableDiskEnd);

        DiskLayoutChart = newLayout;
    }

    private static void AddAlignedFreeSpace(
        ObservableCollection<ChartSpace> layout,
        ulong start,
        ulong end)
    {
        var alignedStart = AlignUp(start, AutomaticPartitionPlanner.PartitionAlignmentBytes);
        var alignedEnd = AlignDown(end, AutomaticPartitionPlanner.PartitionAlignmentBytes);
        if (alignedEnd <= alignedStart)
        {
            return;
        }

        layout.Add(new ChartFreeSpace
        {
            Start = alignedStart,
            Size = alignedEnd - alignedStart
        });
    }

    private static ulong GetSafeEndOffset(Partition partition)
    {
        return partition.Size > ulong.MaxValue - partition.StartOffset
            ? ulong.MaxValue
            : partition.StartOffset + partition.Size;
    }

    private static ulong AlignUp(ulong value, ulong alignment)
    {
        var remainder = value % alignment;
        if (remainder == 0)
        {
            return value;
        }

        var increment = alignment - remainder;
        return value > ulong.MaxValue - increment
            ? ulong.MaxValue
            : value + increment;
    }

    private static ulong AlignDown(ulong value, ulong alignment)
    {
        return value - value % alignment;
    }

    [RelayCommand]
    public void Back()
    {
        Navigation.Previous();
    }

    [RelayCommand]
    public void Next()
    {
        if (!CanProceed)
        {
            return;
        }

        _installationConfigService.PartitionPlan = Plan;
        Navigation.Next();
    }

    [RelayCommand]
    public async Task AddPartitionAsync()
    {
        if (SelectedChartSpace is not ChartFreeSpace)
        {
            return;
        }

        var dialog = new PartitionDialogView();
        dialog.DataContext = new PartitionDialogViewModel(
            dialog,
            SelectedChartSpace,
            Plan.PartitionHistory.Last().Count,
            Plan.PartitionHistory.Last().Any(partition =>
                partition.MountPoint == "/" && !partition.IsProtected));

        if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
            desktop.MainWindow == null)
        {
            return;
        }

        var newPartition = await dialog.ShowDialog<PlannedPartition>(desktop.MainWindow);
        if (newPartition == null)
        {
            return;
        }

        newPartition.DiskId = SelectedDisk?.Id ?? string.Empty;
        newPartition.IsExisting = false;
        Plan.AddPartition(newPartition);
        UpdateChart();
        OnPropertyChanged(nameof(CanProceed));
    }

    [RelayCommand]
    public async Task EditPartitionAsync()
    {
        if (SelectedChartSpace is not ChartPartition selected || selected.Partition.IsProtected)
        {
            return;
        }

        var partition = selected.Partition;
        var dialog = new PartitionDialogView();
        dialog.DataContext = new PartitionDialogViewModel(dialog, SelectedChartSpace);

        if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
            desktop.MainWindow == null)
        {
            return;
        }

        var updatedPartition = await dialog.ShowDialog<PlannedPartition>(desktop.MainWindow);
        if (updatedPartition == null)
        {
            return;
        }

        updatedPartition.Id = partition.Id;
        updatedPartition.DiskId = partition.DiskId;
        updatedPartition.StartOffset = partition.StartOffset;
        updatedPartition.IsExisting = false;

        Plan.EditPartition(partition, updatedPartition);
        UpdateChart();
        OnPropertyChanged(nameof(CanProceed));
    }

    [RelayCommand]
    public void DeletePartition()
    {
        if (SelectedChartSpace is not ChartPartition selected || selected.Partition.IsProtected)
        {
            return;
        }

        Plan.DeletePartition(selected.Partition);
        SelectedChartSpace = null;
        UpdateChart();
        OnPropertyChanged(nameof(CanProceed));
    }
}
