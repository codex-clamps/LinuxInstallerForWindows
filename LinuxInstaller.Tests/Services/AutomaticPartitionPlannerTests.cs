using LinuxInstaller.Models;
using LinuxInstaller.Services;

namespace LinuxInstaller.Tests.Services;

public class AutomaticPartitionPlannerTests
{
    private const ulong MiB = 1024UL * 1024UL;
    private const ulong GiB = 1024UL * MiB;

    [Fact]
    public void CreatePlan_WithExactlySixteenGiBBetweenGptReservations_Succeeds()
    {
        var disk = CreateDisk(
            "disk-exact",
            AutomaticPartitionPlanner.MinimumPartitionSizeBytes +
            (2 * AutomaticPartitionPlanner.PartitionAlignmentBytes));

        var result = AutomaticPartitionPlanner.CreatePlan([disk]);

        Assert.True(result.IsSuccess);
        Assert.Same(disk, result.TargetDisk);
        Assert.NotNull(result.RootPartition);
        Assert.Equal(AutomaticPartitionPlanner.PartitionAlignmentBytes, result.RootPartition.StartOffset);
        Assert.Equal(AutomaticPartitionPlanner.MinimumPartitionSizeBytes, result.RootPartition.Size);
        Assert.Equal(
            disk.Size - AutomaticPartitionPlanner.PartitionAlignmentBytes,
            result.RootPartition.EndOffset);
        Assert.False(result.RootPartition.IsExisting);
        Assert.False(result.RootPartition.IsProtected);
    }

    [Fact]
    public void CreatePlan_WithLessThanSixteenAlignedGiB_Fails()
    {
        var disk = CreateDisk(
            "disk-too-small",
            AutomaticPartitionPlanner.MinimumPartitionSizeBytes +
            (2 * AutomaticPartitionPlanner.PartitionAlignmentBytes) - 1);

        var result = AutomaticPartitionPlanner.CreatePlan([disk]);

        Assert.False(result.IsSuccess);
        Assert.Equal(AutomaticPartitionPlanFailure.NoSuitableFreeSpace, result.Failure);
    }

    [Fact]
    public void CreatePlan_AlignsGapInwardToOneMiBBoundaries()
    {
        var unalignedGapStart = (3 * MiB) + 123;
        var alignedGapStart = 4 * MiB;
        var alignedGapEnd = alignedGapStart + AutomaticPartitionPlanner.MinimumPartitionSizeBytes;
        var unalignedGapEnd = alignedGapEnd + (MiB / 2);
        var disk = CreateDisk(
            "disk-unaligned",
            32 * GiB,
            CreatePartition("after-gap", unalignedGapEnd, 32 * GiB),
            CreatePartition("before-gap", 0, unalignedGapStart));

        var result = AutomaticPartitionPlanner.CreatePlan([disk]);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.RootPartition);
        Assert.Equal(alignedGapStart, result.RootPartition.StartOffset);
        Assert.Equal(alignedGapEnd, result.RootPartition.EndOffset);
        Assert.Equal(0UL, result.RootPartition.StartOffset % MiB);
        Assert.Equal(0UL, result.RootPartition.EndOffset % MiB);
    }

    [Fact]
    public void CreatePlan_WithUnsortedOverlappingAndOutOfRangePartitions_FindsSafeGap()
    {
        var diskSize = (64 * GiB) + (2 * MiB);
        var disk = CreateDisk(
            "disk-messy",
            diskSize,
            CreatePartition("after-gap", 40 * GiB, 30 * GiB),
            CreatePartition("overlap", 10 * GiB, 10 * GiB),
            CreatePartition("out-of-range", ulong.MaxValue - 10, ulong.MaxValue),
            CreatePartition("leading", 0, 12 * GiB),
            CreatePartition("contained", 5 * GiB, 2 * GiB));

        var result = AutomaticPartitionPlanner.CreatePlan([disk]);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.RootPartition);
        Assert.Equal(20 * GiB, result.RootPartition.StartOffset);
        Assert.Equal(20 * GiB, result.RootPartition.Size);
    }

    [Fact]
    public void CreatePlan_SelectsFirstEligibleDiskWithASuitableGap()
    {
        var tooSmall = CreateDisk("disk-small", (8 * GiB) + (2 * MiB));
        var selected = CreateDisk("disk-selected", (24 * GiB) + (2 * MiB));
        var later = CreateDisk("disk-later", (32 * GiB) + (2 * MiB));

        var result = AutomaticPartitionPlanner.CreatePlan([tooSmall, selected, later]);

        Assert.True(result.IsSuccess);
        Assert.Same(selected, result.TargetDisk);
        Assert.NotNull(result.RootPartition);
        Assert.StartsWith("planned-", result.RootPartition.Id, StringComparison.Ordinal);
        Assert.DoesNotContain(
            selected.Partitions,
            partition => string.Equals(
                partition.Id,
                result.RootPartition.Id,
                StringComparison.OrdinalIgnoreCase));
        Assert.Equal(FileSystem.EXT4, result.RootPartition.FileSystem);
        Assert.Equal("/", result.RootPartition.MountPoint);
    }

    [Fact]
    public void CreatePlan_IgnoresIneligibleDiskEvenWhenItHasMoreSpace()
    {
        var removable = CreateDisk("usb", (128 * GiB) + (2 * MiB), eligible: false);
        var internalDisk = CreateDisk("internal", (24 * GiB) + (2 * MiB));

        var result = AutomaticPartitionPlanner.CreatePlan([removable, internalDisk]);

        Assert.True(result.IsSuccess);
        Assert.Same(internalDisk, result.TargetDisk);
    }

    [Fact]
    public void CreatePlan_WhenAllDisksAreIneligible_ReturnsExplicitFailure()
    {
        var result = AutomaticPartitionPlanner.CreatePlan(
        [
            CreateDisk("usb", 64 * GiB, eligible: false),
            CreateDisk("mbr", 64 * GiB, eligible: false)
        ]);

        Assert.False(result.IsSuccess);
        Assert.Equal(AutomaticPartitionPlanFailure.NoEligibleDisk, result.Failure);
        Assert.Null(result.TargetDisk);
        Assert.Null(result.RootPartition);
    }

    [Fact]
    public void CreatePlan_WhenNoEligibleDiskHasSuitableSpace_ReturnsExplicitFailure()
    {
        var result = AutomaticPartitionPlanner.CreatePlan(
        [
            CreateDisk("empty-small", 4 * GiB),
            CreateDisk("fully-occupied", 64 * GiB, CreatePartition("all", 0, 64 * GiB))
        ]);

        Assert.False(result.IsSuccess);
        Assert.Equal(AutomaticPartitionPlanFailure.NoSuitableFreeSpace, result.Failure);
        Assert.Null(result.TargetDisk);
        Assert.Null(result.RootPartition);
    }

    private static Disk CreateDisk(
        string id,
        ulong size,
        params Partition[] partitions)
    {
        return CreateDisk(id, size, eligible: true, partitions);
    }

    private static Disk CreateDisk(
        string id,
        ulong size,
        bool eligible,
        params Partition[] partitions)
    {
        return new Disk
        {
            Id = id,
            Name = id,
            Size = size,
            PartitionStyle = eligible ? "GPT" : "MBR",
            IsEligibleForInstallation = eligible,
            Partitions = [.. partitions]
        };
    }

    private static Partition CreatePartition(string id, ulong startOffset, ulong size)
    {
        return new Partition
        {
            Id = id,
            Name = id,
            StartOffset = startOffset,
            Size = size,
            FileSystem = FileSystem.NTFS,
            IsSystem = false
        };
    }
}
