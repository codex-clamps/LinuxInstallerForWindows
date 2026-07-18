using LinuxInstaller.Models;

namespace LinuxInstaller.Tests.Models;

public class PartitionPlanTests
{
    private const ulong MiB = 1024UL * 1024UL;
    private const ulong GiB = 1024UL * MiB;

    [Fact]
    public void IsValid_WithOneAlignedNonOverlappingRootAndProtectedExistingPartition_ReturnsTrue()
    {
        var (plan, _, root) = CreateValidPlan();
        root.DiskId = "DISK";

        Assert.True(plan.IsValid);
    }

    [Fact]
    public void IsValid_RequiresExactlyOneEditableRoot()
    {
        var (plan, _, root) = CreateValidPlan();
        root.MountPoint = string.Empty;

        Assert.False(plan.IsValid);

        root.MountPoint = "/";
        plan.AddPartition(CreatePlannedPartition("second-root", 40 * GiB, 8 * GiB, "/"));

        Assert.False(plan.IsValid);
    }

    [Fact]
    public void IsValid_RequiresLinuxFileSystemForRoot()
    {
        var (plan, _, root) = CreateValidPlan();
        root.FileSystem = FileSystem.NTFS;

        Assert.False(plan.IsValid);
    }

    [Fact]
    public void IsValid_AllowsProtectedExistingPartitionsOutsidePlanningMargins()
    {
        var diskSize = (64 * GiB) + (2 * MiB);
        var disk = new Disk
        {
            Id = "disk",
            Name = "Disk",
            Size = diskSize,
            PartitionStyle = "GPT",
            IsEligibleForInstallation = true,
            Partitions =
            [
                new Partition
                {
                    Id = "leading",
                    DiskId = "disk",
                    Name = "Leading existing partition",
                    StartOffset = MiB / 2,
                    Size = MiB / 4,
                    FileSystem = FileSystem.NTFS,
                    IsSystem = false
                },
                new Partition
                {
                    Id = "trailing",
                    DiskId = "disk",
                    Name = "Trailing existing partition",
                    StartOffset = diskSize - (MiB / 2),
                    Size = MiB / 4,
                    FileSystem = FileSystem.NTFS,
                    IsSystem = false
                }
            ]
        };
        var plan = new PartitionPlan { TargetDisk = disk };
        plan.AddPartition(CreatePlannedPartition("root", 16 * GiB, 16 * GiB, "/"));

        Assert.True(plan.IsValid);
    }

    [Theory]
    [InlineData("zero-size")]
    [InlineData("overflow")]
    [InlineData("past-disk-end")]
    [InlineData("overlap")]
    public void IsValid_RejectsUnsafeProtectedExistingGeometry(string scenario)
    {
        var disk = CreateDisk();
        var existing = disk.Partitions.Single();

        switch (scenario)
        {
            case "zero-size":
                existing.Size = 0;
                break;
            case "overflow":
                existing.StartOffset = ulong.MaxValue - 10;
                existing.Size = 20;
                break;
            case "past-disk-end":
                existing.StartOffset = disk.Size - (MiB / 2);
                existing.Size = MiB;
                break;
            case "overlap":
                existing.StartOffset = 16 * GiB;
                existing.Size = GiB;
                break;
        }

        var plan = new PartitionPlan { TargetDisk = disk };
        plan.AddPartition(CreatePlannedPartition("root", 16 * GiB, 16 * GiB, "/"));

        Assert.False(plan.IsValid);
    }

    [Theory]
    [InlineData("zero-size")]
    [InlineData("misaligned-start")]
    [InlineData("misaligned-size")]
    [InlineData("leading-reservation")]
    [InlineData("trailing-reservation")]
    [InlineData("overlap")]
    public void IsValid_RejectsUnsafeGeometry(string scenario)
    {
        var (plan, disk, root) = CreateValidPlan();

        switch (scenario)
        {
            case "zero-size":
                root.Size = 0;
                break;
            case "misaligned-start":
                root.StartOffset++;
                break;
            case "misaligned-size":
                root.Size--;
                break;
            case "leading-reservation":
                root.StartOffset = 0;
                break;
            case "trailing-reservation":
                root.StartOffset = disk.Size - MiB;
                root.Size = MiB;
                break;
            case "overlap":
                root.StartOffset = 8 * GiB;
                break;
        }

        Assert.False(plan.IsValid);
    }

    [Theory]
    [InlineData("remove")]
    [InlineData("resize")]
    [InlineData("mount")]
    public void IsValid_RejectsChangedProtectedExistingPartitions(string scenario)
    {
        var (plan, _, _) = CreateValidPlan();
        var existing = plan.PartitionHistory.Last().Single(partition => partition.IsExisting);

        switch (scenario)
        {
            case "remove":
                plan.PartitionHistory.Last().Remove(existing);
                break;
            case "resize":
                existing.Size += MiB;
                break;
            case "mount":
                existing.MountPoint = "/windows";
                break;
        }

        Assert.False(plan.IsValid);
    }

    [Fact]
    public void EditAndDelete_IgnoreProtectedExistingPartitions()
    {
        var (plan, _, _) = CreateValidPlan();
        var existing = plan.PartitionHistory.Last().Single(partition => partition.IsExisting);
        var updated = existing.Clone();
        updated.Size += MiB;
        var historyCount = plan.PartitionHistory.Count;

        plan.EditPartition(existing, updated);
        plan.DeletePartition(existing);

        Assert.Equal(historyCount, plan.PartitionHistory.Count);
        Assert.True(plan.IsValid);
    }

    [Fact]
    public void AddPartition_ReplacesCaseInsensitiveIdentifierCollision()
    {
        var disk = CreateDisk(existingId: "PLANNED-COLLISION");
        var plan = new PartitionPlan { TargetDisk = disk };
        var root = CreatePlannedPartition(
            "planned-collision",
            16 * GiB,
            16 * GiB,
            "/");

        plan.AddPartition(root);

        Assert.False(string.Equals(
            "planned-collision",
            root.Id,
            StringComparison.OrdinalIgnoreCase));
        Assert.StartsWith("planned-", root.Id, StringComparison.Ordinal);
        Assert.True(plan.IsValid);
    }

    [Fact]
    public void IsValid_RejectsCaseInsensitiveDuplicateIdentifiers()
    {
        var (plan, _, root) = CreateValidPlan();
        var duplicate = CreatePlannedPartition(
            root.Id.ToUpperInvariant(),
            40 * GiB,
            8 * GiB,
            "/home");
        plan.PartitionHistory.Last().Add(duplicate);

        Assert.False(plan.IsValid);
    }

    private static (PartitionPlan Plan, Disk Disk, PlannedPartition Root) CreateValidPlan()
    {
        var disk = CreateDisk();
        var plan = new PartitionPlan { TargetDisk = disk };
        var root = CreatePlannedPartition("root", 16 * GiB, 16 * GiB, "/");
        plan.AddPartition(root);
        return (plan, disk, root);
    }

    private static Disk CreateDisk(string existingId = "windows")
    {
        return new Disk
        {
            Id = "disk",
            Name = "Disk",
            Size = (64 * GiB) + (2 * MiB),
            PartitionStyle = "GPT",
            IsEligibleForInstallation = true,
            Partitions =
            [
                new Partition
                {
                    Id = existingId,
                    DiskId = "disk",
                    Name = "Windows",
                    StartOffset = MiB,
                    Size = 8 * GiB,
                    FileSystem = FileSystem.NTFS,
                    IsSystem = false
                }
            ]
        };
    }

    private static PlannedPartition CreatePlannedPartition(
        string id,
        ulong startOffset,
        ulong size,
        string mountPoint)
    {
        return new PlannedPartition
        {
            Id = id,
            DiskId = "disk",
            Name = id,
            StartOffset = startOffset,
            Size = size,
            FileSystem = FileSystem.LINUX,
            IsSystem = false,
            MountPoint = mountPoint
        };
    }
}
