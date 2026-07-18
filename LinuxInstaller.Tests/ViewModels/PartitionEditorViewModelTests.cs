using LinuxInstaller.Models;
using LinuxInstaller.Services;
using LinuxInstaller.ViewModels;

namespace LinuxInstaller.Tests.ViewModels;

public class PartitionEditorViewModelTests
{
    [Fact]
    public async Task DiskChart_ReservesBothGptMetadataEdges()
    {
        var disk = CreateDisk();
        var viewModel = CreateViewModel(disk);

        await viewModel.ActivateAsync();

        var freeSpace = Assert.IsType<ChartFreeSpace>(Assert.Single(viewModel.DiskLayoutChart));
        Assert.Equal(AutomaticPartitionPlanner.PartitionAlignmentBytes, freeSpace.Start);
        Assert.Equal(
            disk.Size - (2 * AutomaticPartitionPlanner.PartitionAlignmentBytes),
            freeSpace.Size);
    }

    [Fact]
    public async Task ExistingPartitionsCannotBeEditedOrDeleted()
    {
        var disk = CreateDisk(
            new Partition
            {
                Id = "windows",
                DiskId = "disk",
                Name = "Windows",
                Size = 8UL * 1024 * 1024 * 1024,
                StartOffset = AutomaticPartitionPlanner.PartitionAlignmentBytes,
                FileSystem = FileSystem.NTFS,
                IsSystem = false
            });
        var viewModel = CreateViewModel(disk);

        await viewModel.ActivateAsync();
        viewModel.SelectedChartSpace = viewModel.DiskLayoutChart.OfType<ChartPartition>().Single();

        Assert.False(viewModel.CanEditPartition);
        Assert.False(viewModel.CanDeletePartition);
    }

    [Fact]
    public async Task NewPlannedPartitionsRemainEditable()
    {
        var viewModel = CreateViewModel(CreateDisk());
        await viewModel.ActivateAsync();
        var partition = new PlannedPartition
        {
            Id = "new-root",
            DiskId = "disk",
            Name = "Linux root",
            Size = 16UL * 1024 * 1024 * 1024,
            StartOffset = AutomaticPartitionPlanner.PartitionAlignmentBytes,
            FileSystem = FileSystem.EXT4,
            IsSystem = false,
            MountPoint = "/"
        };

        viewModel.SelectedChartSpace = ChartPartition.FromPartition(partition);

        Assert.True(viewModel.CanEditPartition);
        Assert.True(viewModel.CanDeletePartition);
    }

    [Fact]
    public async Task Constructor_DoesNotStartStorageDiscoveryUntilActivated()
    {
        var storageManager = new FakeStorageManager([CreateDisk()]);
        var viewModel = CreateViewModel(storageManager);

        Assert.Equal(0, storageManager.CallCount);

        await viewModel.ActivateAsync();

        Assert.Equal(1, storageManager.CallCount);
    }

    [Fact]
    public async Task ActivateAsync_RebasesConfiguredPlannedPartitionsOntoFreshTopology()
    {
        var staleDisk = CreateDisk();
        var freshExisting = new Partition
        {
            Id = "fresh-windows",
            DiskId = "disk",
            Name = "Fresh Windows topology",
            Size = 2UL * 1024 * 1024 * 1024,
            StartOffset = 17UL * 1024 * 1024 * 1024,
            FileSystem = FileSystem.NTFS,
            IsSystem = false
        };
        var freshDisk = CreateDisk(freshExisting);
        var configuredPlan = new PartitionPlan { TargetDisk = staleDisk };
        var root = CreateRootPartition();
        configuredPlan.AddPartition(root);
        var configuration = new InstallationConfigService
        {
            PartitionPlan = configuredPlan
        };
        var viewModel = CreateViewModel(
            new FakeStorageManager([freshDisk]),
            configuration);

        await viewModel.ActivateAsync();

        Assert.Same(viewModel.Disks.Single(), viewModel.Plan.TargetDisk);
        Assert.NotSame(staleDisk, viewModel.Plan.TargetDisk);
        Assert.Contains(
            viewModel.Plan.PartitionHistory.Last(),
            partition => partition.Id == freshExisting.Id && partition.IsExisting);
        Assert.Contains(
            viewModel.Plan.PartitionHistory.Last(),
            partition => partition.Id == root.Id && !partition.IsExisting);
        Assert.True(viewModel.CanProceed);
    }

    [Fact]
    public async Task RefreshFailure_RetainsPlannedPartitionsButBlocksProceeding()
    {
        var storageManager = new FakeStorageManager([CreateDisk()]);
        var configuration = new InstallationConfigService();
        var configuredPlan = new PartitionPlan { TargetDisk = storageManager.Disks.Single() };
        var root = CreateRootPartition();
        configuredPlan.AddPartition(root);
        configuration.PartitionPlan = configuredPlan;
        var viewModel = CreateViewModel(storageManager, configuration);
        await viewModel.ActivateAsync();
        storageManager.ThrowOnRead = true;

        await viewModel.RefreshDisksCommand.ExecuteAsync(null);

        Assert.Contains(
            viewModel.Plan.PartitionHistory.Last(),
            partition => partition.Id == root.Id && !partition.IsExisting);
        Assert.True(viewModel.HasError);
        Assert.False(viewModel.CanProceed);
    }

    [Fact]
    public async Task DiskChart_AlignsFreeSpaceAroundUnalignedExistingPartitions()
    {
        const ulong mib = 1024UL * 1024UL;
        var existing = new Partition
        {
            Id = "unaligned",
            DiskId = "disk",
            Name = "Unaligned existing partition",
            StartOffset = (5 * mib) + 123,
            Size = (2 * mib) + 333,
            FileSystem = FileSystem.NTFS,
            IsSystem = false
        };
        var viewModel = CreateViewModel(CreateDisk(existing));

        await viewModel.ActivateAsync();

        var freeSpaces = viewModel.DiskLayoutChart.OfType<ChartFreeSpace>().ToList();
        Assert.Equal(2, freeSpaces.Count);
        Assert.Equal(mib, freeSpaces[0].Start);
        Assert.Equal(4 * mib, freeSpaces[0].Size);
        Assert.Equal(8 * mib, freeSpaces[1].Start);
        Assert.All(freeSpaces, freeSpace =>
        {
            Assert.Equal(0UL, freeSpace.Start % mib);
            Assert.Equal(0UL, freeSpace.Size % mib);
        });
    }

    private static PartitionEditorViewModel CreateViewModel(Disk disk)
    {
        return CreateViewModel(new FakeStorageManager([disk]));
    }

    private static PartitionEditorViewModel CreateViewModel(
        FakeStorageManager storageManager,
        InstallationConfigService? configuration = null)
    {
        return new PartitionEditorViewModel(
            new NavigationService(),
            new PartitionService(storageManager),
            configuration ?? new InstallationConfigService());
    }

    private static Disk CreateDisk(params Partition[] partitions)
    {
        return new Disk
        {
            Id = "disk",
            Name = "Disk",
            Size = (20UL * 1024 * 1024 * 1024) +
                (2 * AutomaticPartitionPlanner.PartitionAlignmentBytes),
            PartitionStyle = "GPT",
            IsEligibleForInstallation = true,
            Partitions = [.. partitions]
        };
    }

    private static PlannedPartition CreateRootPartition()
    {
        return new PlannedPartition
        {
            Id = PlannedPartition.CreateId(),
            DiskId = "disk",
            Name = "Linux root",
            Size = 16UL * 1024 * 1024 * 1024,
            StartOffset = AutomaticPartitionPlanner.PartitionAlignmentBytes,
            FileSystem = FileSystem.EXT4,
            IsSystem = false,
            MountPoint = "/"
        };
    }

    private sealed class FakeStorageManager(IReadOnlyList<Disk> disks) : IStorageManager
    {
        public IReadOnlyList<Disk> Disks { get; } = disks;
        public int CallCount { get; private set; }
        public bool ThrowOnRead { get; set; }

        public Task<IReadOnlyList<Disk>> GetDisksAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            if (ThrowOnRead)
            {
                throw new InvalidOperationException("Storage discovery failed.");
            }

            return Task.FromResult(Disks);
        }
    }
}
