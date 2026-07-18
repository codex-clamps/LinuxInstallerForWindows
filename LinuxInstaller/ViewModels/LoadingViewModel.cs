using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LinuxInstaller.Models;
using LinuxInstaller.Services;
using LinuxInstaller.ViewModels.Interfaces;
using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace LinuxInstaller.ViewModels;

public partial class LoadingViewModel : NavigatableViewModelBase
{
    private readonly ISystemAnalysisService _systemAnalysisService;
    private readonly PartitionService _partitionService;

    [ObservableProperty]
    private string _statusText = "Loading system information...";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanProceed))]
    private bool _isChecking = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanProceed))]
    private SystemAnalysisSnapshot? _analysis;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanProceed))]
    private int _diskCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanProceed))]
    private int _partitionCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanProceed))]
    private int _eligibleDiskCount;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _elevationMessage;

    public LoadingViewModel(
        NavigationService navigationService,
        ISystemAnalysisService systemAnalysisService,
        PartitionService partitionService)
        : base(navigationService)
    {
        _systemAnalysisService = systemAnalysisService;
        _partitionService = partitionService;
        _ = PerformSystemChecksAsync();
    }

    public bool HasAnalysis => Analysis != null && !IsChecking && !HasError;
    public bool HasError => !IsChecking && !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool HasElevationMessage => !string.IsNullOrWhiteSpace(ElevationMessage);
    public bool NeedsElevation => Analysis is { IsAdministrator: false };

    public string AdministratorStatus => Analysis?.IsAdministrator switch
    {
        true => "Running as administrator",
        false => "Standard user",
        _ => "Not checked"
    };

    public string BootModeStatus => Analysis?.BootMode switch
    {
        FirmwareBootMode.Uefi => "UEFI",
        FirmwareBootMode.LegacyBios => "Legacy BIOS (unsupported)",
        _ => "Unknown"
    };

    public string SecureBootStatus => Analysis?.SecureBoot switch
    {
        SecureBootState.Enabled => "Enabled",
        SecureBootState.Disabled => "Disabled",
        _ => "Unknown"
    };

    public string BitLockerStatus => Analysis?.SystemDriveBitLocker switch
    {
        BitLockerVolumeState.FullyDecrypted => $"{Analysis.SystemDrive} is fully decrypted",
        BitLockerVolumeState.EncryptedProtectionOn => $"{Analysis.SystemDrive} is encrypted and protected",
        BitLockerVolumeState.EncryptedProtectionOff => $"{Analysis.SystemDrive} is encrypted with protection suspended",
        _ => "Unavailable"
    };

    public string StorageStatus => Analysis is { IsAdministrator: false }
        ? "Administrator access is required to inspect storage topology."
        : DiskCount == 0
            ? "No physical disks discovered"
            : $"{DiskCount} disk(s), {PartitionCount} partition(s), {EligibleDiskCount} eligible installation target(s)";

    public string CompatibilityStatus => Analysis switch
    {
        { BootMode: FirmwareBootMode.Uefi, IsAdministrator: true }
            when EligibleDiskCount > 0 =>
            "This system meets the initial UEFI, administrator, and storage requirements.",
        { BootMode: FirmwareBootMode.Uefi, IsAdministrator: true } =>
            "UEFI and administrator access are available, but no supported internal GPT disk was found.",
        { BootMode: FirmwareBootMode.Uefi, IsAdministrator: false } =>
            "UEFI was detected. Relaunch as administrator to continue safely.",
        { BootMode: FirmwareBootMode.LegacyBios } =>
            "Legacy BIOS systems are outside the initial supported scope.",
        _ =>
            "The firmware mode could not be verified, so installation cannot continue safely."
    };

    public override bool CanProceed =>
        !IsChecking &&
        EligibleDiskCount > 0 &&
        Analysis is
        {
            IsAdministrator: true,
            BootMode: FirmwareBootMode.Uefi
        };

    public override bool CanGoBack => false;

    partial void OnAnalysisChanged(SystemAnalysisSnapshot? value)
    {
        OnPropertyChanged(nameof(HasAnalysis));
        OnPropertyChanged(nameof(NeedsElevation));
        OnPropertyChanged(nameof(AdministratorStatus));
        OnPropertyChanged(nameof(BootModeStatus));
        OnPropertyChanged(nameof(SecureBootStatus));
        OnPropertyChanged(nameof(BitLockerStatus));
        OnPropertyChanged(nameof(StorageStatus));
        OnPropertyChanged(nameof(CompatibilityStatus));
    }

    partial void OnIsCheckingChanged(bool value)
    {
        OnPropertyChanged(nameof(HasAnalysis));
        OnPropertyChanged(nameof(HasError));
    }

    partial void OnDiskCountChanged(int value)
    {
        OnPropertyChanged(nameof(StorageStatus));
    }

    partial void OnPartitionCountChanged(int value)
    {
        OnPropertyChanged(nameof(StorageStatus));
    }

    partial void OnEligibleDiskCountChanged(int value)
    {
        OnPropertyChanged(nameof(StorageStatus));
        OnPropertyChanged(nameof(CompatibilityStatus));
    }

    partial void OnErrorMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(HasAnalysis));
    }

    partial void OnElevationMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(HasElevationMessage));
    }

    [RelayCommand]
    private void Continue()
    {
        if (CanProceed)
        {
            Navigation.Next();
        }
    }

    [RelayCommand]
    private async Task RetryAsync()
    {
        if (!IsChecking)
        {
            await PerformSystemChecksAsync(forceStorageRefresh: true);
        }
    }

    [RelayCommand]
    private async Task RelaunchAsAdminAsync()
    {
        if (!NeedsElevation)
        {
            return;
        }

        ElevationMessage = null;

        try
        {
            await _systemAnalysisService.RelaunchAsAdminAsync();
            ElevationMessage = "Elevated instance started. Continue in the new window; this window remains blocked.";
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            ElevationMessage = "Administrator relaunch was cancelled.";
        }
        catch (Exception exception)
        {
            ElevationMessage = $"Administrator relaunch failed: {exception.Message}";
        }
    }

    private async Task PerformSystemChecksAsync(bool forceStorageRefresh = false)
    {
        IsChecking = true;
        Analysis = null;
        DiskCount = 0;
        PartitionCount = 0;
        EligibleDiskCount = 0;
        ErrorMessage = null;
        ElevationMessage = null;
        StatusText = "Checking Windows system compatibility and storage topology...";

        try
        {
            Analysis = await _systemAnalysisService.AnalyzeAsync();
            if (!Analysis.IsAdministrator)
            {
                StatusText = "System analysis complete. Administrator access is required to continue.";
                return;
            }

            var disks = await _partitionService.GetAvailableDisksAsync(forceStorageRefresh);
            DiskCount = disks.Count;
            PartitionCount = disks.Sum(disk => disk.Partitions.Count);
            EligibleDiskCount = disks.Count(disk => disk.IsEligibleForInstallation);
            StatusText = "System analysis complete.";
        }
        catch (Exception exception)
        {
            ErrorMessage = $"System analysis failed: {exception.Message}";
            StatusText = "System analysis could not be completed.";
        }
        finally
        {
            IsChecking = false;
        }
    }
}
