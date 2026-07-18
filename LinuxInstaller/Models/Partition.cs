using System;
using System.Collections.Generic;

namespace LinuxInstaller.Models;

public class Partition
{
    public required string Id { get; set; } = string.Empty;
    public string DiskId { get; set; } = string.Empty;
    public uint Number { get; set; }
    public string UniqueId { get; set; } = string.Empty;
    public string Guid { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public required ulong Size { get; set; }
    public required ulong StartOffset { get; set; }
    public string DriveLetter { get; set; } = string.Empty;
    public IReadOnlyList<string> AccessPaths { get; set; } = [];
    public string VolumeId { get; set; } = string.Empty;
    public string FileSystemLabel { get; set; } = string.Empty;
    public ulong? VolumeSize { get; set; }
    public ulong? VolumeSizeRemaining { get; set; }
    public FileSystem FileSystem { get; set; }
    public bool IsBoot { get; set; }
    public required bool IsSystem { get; set; }
    public bool IsReadOnly { get; set; }
    public bool IsHidden { get; set; }
    public bool IsExisting { get; set; } = true;
    public ulong? SupportedSizeMinimum { get; set; }
    public ulong? SupportedSizeMaximum { get; set; }

    public ulong EndOffset => StartOffset + Size;
    public bool IsProtected => IsExisting || IsSystem || IsBoot || IsReadOnly || IsHidden;
    public ulong? AvailableShrinkSpace => SupportedSizeMinimum is { } minimum && Size > minimum
        ? Size - minimum
        : null;

    public virtual Partition Clone()
    {
        return new Partition
        {
            Id = Id,
            DiskId = DiskId,
            Number = Number,
            UniqueId = UniqueId,
            Guid = Guid,
            Name = Name,
            Type = Type,
            Size = Size,
            StartOffset = StartOffset,
            DriveLetter = DriveLetter,
            AccessPaths = [.. AccessPaths],
            VolumeId = VolumeId,
            FileSystemLabel = FileSystemLabel,
            VolumeSize = VolumeSize,
            VolumeSizeRemaining = VolumeSizeRemaining,
            FileSystem = FileSystem,
            IsBoot = IsBoot,
            IsSystem = IsSystem,
            IsReadOnly = IsReadOnly,
            IsHidden = IsHidden,
            IsExisting = IsExisting,
            SupportedSizeMinimum = SupportedSizeMinimum,
            SupportedSizeMaximum = SupportedSizeMaximum
        };
    }

    public override bool Equals(object? obj)
    {
        return obj is Partition other &&
            StringComparer.OrdinalIgnoreCase.Equals(Id, other.Id) &&
            Size == other.Size &&
            StartOffset == other.StartOffset;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(
            StringComparer.OrdinalIgnoreCase.GetHashCode(Id),
            Size,
            StartOffset);
    }
}

public class PlannedPartition : Partition
{
    public PlannedPartition()
    {
        IsExisting = false;
    }

    public string MountPoint { get; set; } = string.Empty;

    public static string CreateId()
    {
        return $"planned-{System.Guid.NewGuid():N}";
    }

    public static PlannedPartition FromPartition(Partition partition, string mountPoint = "")
    {
        return new PlannedPartition
        {
            Id = partition.Id,
            DiskId = partition.DiskId,
            Number = partition.Number,
            UniqueId = partition.UniqueId,
            Guid = partition.Guid,
            Name = partition.Name,
            Type = partition.Type,
            Size = partition.Size,
            StartOffset = partition.StartOffset,
            DriveLetter = partition.DriveLetter,
            AccessPaths = [.. partition.AccessPaths],
            VolumeId = partition.VolumeId,
            FileSystemLabel = partition.FileSystemLabel,
            VolumeSize = partition.VolumeSize,
            VolumeSizeRemaining = partition.VolumeSizeRemaining,
            FileSystem = partition.FileSystem,
            IsBoot = partition.IsBoot,
            IsSystem = partition.IsSystem,
            IsReadOnly = partition.IsReadOnly,
            IsHidden = partition.IsHidden,
            IsExisting = true,
            SupportedSizeMinimum = partition.SupportedSizeMinimum,
            SupportedSizeMaximum = partition.SupportedSizeMaximum,
            MountPoint = mountPoint
        };
    }

    public override PlannedPartition Clone()
    {
        return new PlannedPartition
        {
            Id = Id,
            DiskId = DiskId,
            Number = Number,
            UniqueId = UniqueId,
            Guid = Guid,
            Name = Name,
            Type = Type,
            Size = Size,
            StartOffset = StartOffset,
            DriveLetter = DriveLetter,
            AccessPaths = [.. AccessPaths],
            VolumeId = VolumeId,
            FileSystemLabel = FileSystemLabel,
            VolumeSize = VolumeSize,
            VolumeSizeRemaining = VolumeSizeRemaining,
            FileSystem = FileSystem,
            IsBoot = IsBoot,
            IsSystem = IsSystem,
            IsReadOnly = IsReadOnly,
            IsHidden = IsHidden,
            IsExisting = IsExisting,
            SupportedSizeMinimum = SupportedSizeMinimum,
            SupportedSizeMaximum = SupportedSizeMaximum,
            MountPoint = MountPoint
        };
    }
}
