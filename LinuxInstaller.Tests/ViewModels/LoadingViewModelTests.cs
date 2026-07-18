using LinuxInstaller.Models;
using LinuxInstaller.Services;
using LinuxInstaller.ViewModels;

namespace LinuxInstaller.Tests.ViewModels;

public class LoadingViewModelTests
{
    [Fact]
    public async Task NonAdministrator_IsOfferedElevationWithoutStorageFailure()
    {
        var storage = new FakeStorageManager([CreateDisk(eligible: true)]);
        var viewModel = CreateViewModel(
            new SystemAnalysisSnapshot
            {
                IsAdministrator = false,
                BootMode = FirmwareBootMode.Uefi,
                SecureBoot = SecureBootState.Disabled,
                SystemDrive = "C:",
                SystemDriveBitLocker = BitLockerVolumeState.Unknown,
                CollectedAtUtc = DateTimeOffset.UtcNow
            },
            storage);

        await WaitForAnalysisAsync(viewModel);

        Assert.True(viewModel.NeedsElevation);
        Assert.False(viewModel.CanProceed);
        Assert.False(viewModel.HasError);
        Assert.Equal(0, storage.CallCount);
    }

    [Fact]
    public async Task ElevatedUefiSystemWithEligibleDisk_CanProceed()
    {
        var storage = new FakeStorageManager([CreateDisk(eligible: true)]);
        var viewModel = CreateViewModel(CreateElevatedSnapshot(), storage);

        await WaitForAnalysisAsync(viewModel);

        Assert.True(viewModel.CanProceed);
        Assert.Equal(1, viewModel.DiskCount);
        Assert.Equal(1, viewModel.PartitionCount);
        Assert.Equal(1, viewModel.EligibleDiskCount);
        Assert.Equal(1, storage.CallCount);
    }

    [Fact]
    public async Task ElevatedUefiSystemWithoutEligibleDisk_IsBlocked()
    {
        var storage = new FakeStorageManager([CreateDisk(eligible: false)]);
        var viewModel = CreateViewModel(CreateElevatedSnapshot(), storage);

        await WaitForAnalysisAsync(viewModel);

        Assert.False(viewModel.CanProceed);
        Assert.Equal(0, viewModel.EligibleDiskCount);
        Assert.Contains("no supported internal GPT disk", viewModel.CompatibilityStatus);
    }

    [Fact]
    public async Task StorageCheckInProgress_DoesNotExposeInterimResults()
    {
        var storageResult = new TaskCompletionSource<IReadOnlyList<Disk>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var storage = new FakeStorageManager(
            cancellationToken => storageResult.Task.WaitAsync(cancellationToken));
        var viewModel = CreateViewModel(CreateElevatedSnapshot(), storage);

        await WaitUntilAsync(() => storage.CallCount == 1);

        Assert.True(viewModel.IsChecking);
        Assert.NotNull(viewModel.Analysis);
        Assert.False(viewModel.HasAnalysis);
        Assert.False(viewModel.HasError);

        storageResult.SetResult([CreateDisk(eligible: true)]);
        await WaitForAnalysisAsync(viewModel);

        Assert.True(viewModel.HasAnalysis);
        Assert.False(viewModel.HasError);
    }

    [Fact]
    public async Task StorageFailure_ShowsErrorInsteadOfPartialAnalysis()
    {
        var storage = new FakeStorageManager(
            _ => Task.FromException<IReadOnlyList<Disk>>(
                new InvalidOperationException("storage unavailable")));
        var viewModel = CreateViewModel(CreateElevatedSnapshot(), storage);

        await WaitForAnalysisAsync(viewModel);

        Assert.False(viewModel.HasAnalysis);
        Assert.True(viewModel.HasError);
        Assert.Contains("storage unavailable", viewModel.ErrorMessage);
    }

    private static LoadingViewModel CreateViewModel(
        SystemAnalysisSnapshot snapshot,
        FakeStorageManager storageManager)
    {
        return new LoadingViewModel(
            new NavigationService(),
            new FakeSystemAnalysisService(snapshot),
            new PartitionService(storageManager));
    }

    private static async Task WaitForAnalysisAsync(LoadingViewModel viewModel)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (viewModel.IsChecking)
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private static SystemAnalysisSnapshot CreateElevatedSnapshot()
    {
        return new SystemAnalysisSnapshot
        {
            IsAdministrator = true,
            BootMode = FirmwareBootMode.Uefi,
            SecureBoot = SecureBootState.Disabled,
            SystemDrive = "C:",
            SystemDriveBitLocker = BitLockerVolumeState.FullyDecrypted,
            CollectedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static Disk CreateDisk(bool eligible)
    {
        return new Disk
        {
            Id = "disk",
            Name = "Disk",
            Size = 1000,
            PartitionStyle = eligible ? "GPT" : "MBR",
            IsEligibleForInstallation = eligible,
            Partitions =
            [
                new Partition
                {
                    Id = "partition",
                    DiskId = "disk",
                    Name = "Windows",
                    Size = 900,
                    StartOffset = 1,
                    FileSystem = FileSystem.NTFS,
                    IsSystem = false
                }
            ]
        };
    }

    private sealed class FakeSystemAnalysisService(SystemAnalysisSnapshot snapshot)
        : ISystemAnalysisService
    {
        public Task<SystemAnalysisSnapshot> AnalyzeAsync(
            string? driveLetter = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(snapshot);
        }

        public Task RelaunchAsAdminAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class FakeStorageManager : IStorageManager
    {
        private readonly Func<CancellationToken, Task<IReadOnlyList<Disk>>> _getDisksAsync;

        public FakeStorageManager(IReadOnlyList<Disk> disks)
            : this(cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(disks);
            })
        {
        }

        public FakeStorageManager(
            Func<CancellationToken, Task<IReadOnlyList<Disk>>> getDisksAsync)
        {
            _getDisksAsync = getDisksAsync;
        }

        public int CallCount { get; private set; }

        public Task<IReadOnlyList<Disk>> GetDisksAsync(
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return _getDisksAsync(cancellationToken);
        }
    }
}
