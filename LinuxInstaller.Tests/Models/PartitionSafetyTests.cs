using LinuxInstaller.Models;

namespace LinuxInstaller.Tests.Models;

public class PartitionSafetyTests
{
    [Fact]
    public void DiscoveredPartition_IsProtectedByDefault()
    {
        var partition = CreateExistingPartition();

        Assert.True(partition.IsExisting);
        Assert.True(partition.IsProtected);
    }

    [Fact]
    public void NewPlannedPartition_IsEditableByDefault()
    {
        var partition = new PlannedPartition
        {
            Id = "new-root",
            Name = "Linux root",
            Size = 32UL * 1024 * 1024 * 1024,
            StartOffset = 1024 * 1024,
            FileSystem = FileSystem.EXT4,
            IsSystem = false,
            MountPoint = "/"
        };

        Assert.False(partition.IsExisting);
        Assert.False(partition.IsProtected);
    }

    [Fact]
    public void FromPartition_PreservesExistingProtectionAndResizeMetadata()
    {
        var source = CreateExistingPartition();
        source.SupportedSizeMinimum = 40;
        source.SupportedSizeMaximum = 100;
        source.VolumeSizeRemaining = 25;

        var planned = PlannedPartition.FromPartition(source);
        var clone = planned.Clone();

        Assert.True(planned.IsExisting);
        Assert.True(planned.IsProtected);
        Assert.Equal(60UL, planned.AvailableShrinkSpace);
        Assert.Equal(25UL, clone.VolumeSizeRemaining);
        Assert.Equal(planned.SupportedSizeMinimum, clone.SupportedSizeMinimum);
    }

    [Fact]
    public void Equals_DoesNotTreatHashCollisionsAsEqual()
    {
        var first = CreateHashCollisionPartition("first", 1, 10);
        var second = CreateHashCollisionPartition("second", 2, 20);

        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Equals_UsesCaseInsensitiveStableIdentityAndGeometry()
    {
        var first = CreateExistingPartition();
        var second = CreateExistingPartition();
        second.Id = first.Id.ToUpperInvariant();

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());

        second.Size++;
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void CreateId_ReturnsDistinctPlannedPartitionIdentifiers()
    {
        var first = PlannedPartition.CreateId();
        var second = PlannedPartition.CreateId();

        Assert.StartsWith("planned-", first, StringComparison.Ordinal);
        Assert.StartsWith("planned-", second, StringComparison.Ordinal);
        Assert.NotEqual(first, second);
    }

    private static Partition CreateExistingPartition()
    {
        return new Partition
        {
            Id = "existing",
            Name = "Windows",
            Size = 100,
            StartOffset = 1,
            FileSystem = FileSystem.NTFS,
            IsSystem = false
        };
    }

    private static Partition CreateHashCollisionPartition(
        string id,
        ulong startOffset,
        ulong size)
    {
        return new HashCollisionPartition
        {
            Id = id,
            Name = id,
            Size = size,
            StartOffset = startOffset,
            FileSystem = FileSystem.NTFS,
            IsSystem = false
        };
    }

    private sealed class HashCollisionPartition : Partition
    {
        public override int GetHashCode()
        {
            return 42;
        }
    }
}
