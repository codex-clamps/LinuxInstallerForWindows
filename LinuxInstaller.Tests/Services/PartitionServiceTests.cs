using LinuxInstaller.Models;
using LinuxInstaller.Services;

namespace LinuxInstaller.Tests.Services;

public class PartitionServiceTests
{
    [Fact]
    public async Task GetAvailableDisksAsync_CachesDiscoveryUntilForcedRefresh()
    {
        var storageManager = new FakeStorageManager();
        var service = new PartitionService(storageManager);

        var first = await service.GetAvailableDisksAsync();
        var second = await service.GetAvailableDisksAsync();
        var refreshed = await service.GetAvailableDisksAsync(forceRefresh: true);

        Assert.NotSame(first, second);
        Assert.Equal(first.Select(disk => disk.Id), second.Select(disk => disk.Id));
        Assert.NotSame(first, refreshed);
        Assert.Equal(2, storageManager.CallCount);
    }

    [Fact]
    public async Task GetPartitionsAsync_UsesStableDiskId()
    {
        var storageManager = new FakeStorageManager();
        var service = new PartitionService(storageManager);

        var partitions = await service.GetPartitionsAsync("disk-1");

        var partition = Assert.Single(partitions);
        Assert.Equal("partition-1", partition.Id);
    }

    [Fact]
    public async Task GetAvailableDisksAsync_ExcludesDuplicateDiskIdentities()
    {
        var storageManager = new StaticStorageManager(
        [
            CreateDisk("duplicate"),
            CreateDisk("DUPLICATE"),
            CreateDisk("safe")
        ]);
        var service = new PartitionService(storageManager);

        var disks = await service.GetAvailableDisksAsync();

        var disk = Assert.Single(disks);
        Assert.Equal("safe", disk.Id);
    }

    [Fact]
    public async Task InvalidateCache_DuringRefresh_DoesNotRestoreInvalidatedSnapshot()
    {
        var storageManager = new BlockingStorageManager();
        var service = new PartitionService(storageManager);
        var staleSnapshot = new List<Disk> { CreateDisk("stale") };

        var firstLoad = service.GetAvailableDisksAsync();
        await storageManager.FirstCallStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        service.InvalidateCache();
        storageManager.CompleteFirstCall(staleSnapshot);

        var first = await firstLoad;
        var second = await service.GetAvailableDisksAsync();

        Assert.NotSame(staleSnapshot, first);
        Assert.Equal("stale", Assert.Single(first).Id);
        Assert.NotSame(first, second);
        Assert.Equal("fresh", Assert.Single(second).Id);
        Assert.Equal(2, storageManager.CallCount);
    }

    [Fact]
    public async Task GetAvailableDisksAsync_ReturnsDeepCopiesOfCachedTopology()
    {
        var storageManager = new FakeStorageManager();
        var service = new PartitionService(storageManager);

        var first = await service.GetAvailableDisksAsync();
        var firstDisk = Assert.Single(first);
        var firstPartition = Assert.Single(firstDisk.Partitions);
        firstDisk.Name = "Changed by caller";
        firstPartition.Name = "Changed by caller";
        firstDisk.Partitions.Clear();

        var second = await service.GetAvailableDisksAsync();
        var secondDisk = Assert.Single(second);
        var secondPartition = Assert.Single(secondDisk.Partitions);

        Assert.Equal("Disk 1", secondDisk.Name);
        Assert.Equal("Windows", secondPartition.Name);
        Assert.Equal(1, storageManager.CallCount);
    }

    [Fact]
    public async Task GetPartitionsAsync_ReturnsCopiesInsteadOfCachedPartitions()
    {
        var storageManager = new FakeStorageManager();
        var service = new PartitionService(storageManager);

        var first = await service.GetPartitionsAsync("disk-1");
        Assert.Single(first).Name = "Changed by caller";

        var second = await service.GetPartitionsAsync("disk-1");

        Assert.Equal("Windows", Assert.Single(second).Name);
        Assert.Equal(1, storageManager.CallCount);
    }

    private sealed class FakeStorageManager : IStorageManager
    {
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<Disk>> GetDisksAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;

            IReadOnlyList<Disk> disks =
            [
                new Disk
                {
                    Id = "disk-1",
                    Name = "Disk 1",
                    Size = 1000,
                    PartitionStyle = "GPT",
                    IsEligibleForInstallation = true,
                    Partitions =
                    [
                        new Partition
                        {
                            Id = "partition-1",
                            DiskId = "disk-1",
                            Name = "Windows",
                            Size = 900,
                            StartOffset = 1,
                            FileSystem = FileSystem.NTFS,
                            IsSystem = false
                        }
                    ]
                }
            ];

            return Task.FromResult(disks);
        }
    }

    private sealed class StaticStorageManager(IReadOnlyList<Disk> disks) : IStorageManager
    {
        public Task<IReadOnlyList<Disk>> GetDisksAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(disks);
        }
    }

    private sealed class BlockingStorageManager : IStorageManager
    {
        private readonly TaskCompletionSource<IReadOnlyList<Disk>> _firstCall =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _callCount;

        public TaskCompletionSource FirstCallStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int CallCount => _callCount;

        public Task<IReadOnlyList<Disk>> GetDisksAsync(
            CancellationToken cancellationToken = default)
        {
            var callCount = Interlocked.Increment(ref _callCount);
            if (callCount == 1)
            {
                FirstCallStarted.TrySetResult();
                return _firstCall.Task.WaitAsync(cancellationToken);
            }

            IReadOnlyList<Disk> freshSnapshot = [CreateDisk("fresh")];
            return Task.FromResult(freshSnapshot);
        }

        public void CompleteFirstCall(IReadOnlyList<Disk> disks) =>
            _firstCall.TrySetResult(disks);
    }

    private static Disk CreateDisk(string id) => new()
    {
        Id = id,
        Name = id,
        Size = 1000,
        PartitionStyle = "GPT",
        IsEligibleForInstallation = true,
        Partitions = []
    };
}
