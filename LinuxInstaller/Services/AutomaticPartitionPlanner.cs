using LinuxInstaller.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LinuxInstaller.Services;

public static class AutomaticPartitionPlanner
{
    public const ulong MinimumPartitionSizeBytes = 16UL * 1024UL * 1024UL * 1024UL;
    public const ulong PartitionAlignmentBytes = 1UL * 1024UL * 1024UL;

    public static AutomaticPartitionPlanResult CreatePlan(IReadOnlyList<Disk> disks)
    {
        ArgumentNullException.ThrowIfNull(disks);

        var eligibleDisks = disks.Where(disk => disk.IsEligibleForInstallation).ToList();
        if (eligibleDisks.Count == 0)
        {
            return AutomaticPartitionPlanResult.Failed(
                AutomaticPartitionPlanFailure.NoEligibleDisk);
        }

        var foundEfiPartition = false;
        foreach (var disk in eligibleDisks)
        {
            var efiPartition = disk.Partitions
                .Where(partition =>
                    partition.IsEfiSystemPartition &&
                    partition.FileSystem == FileSystem.FAT32 &&
                    System.Guid.TryParse(partition.Guid, out _))
                .OrderByDescending(partition => partition.IsSystem)
                .ThenBy(partition => partition.Number)
                .FirstOrDefault();
            if (efiPartition == null)
            {
                continue;
            }

            foundEfiPartition = true;
            if (disk.Size <= PartitionAlignmentBytes * 2)
            {
                continue;
            }

            var usableDiskEnd = disk.Size - PartitionAlignmentBytes;
            var partitionRanges = disk.Partitions
                .Where(partition => partition.Size > 0 && partition.StartOffset < usableDiskEnd)
                .Select(partition => BoundToUsableDisk(partition, usableDiskEnd))
                .Where(range => range.End > range.Start)
                .OrderBy(range => range.Start)
                .ThenBy(range => range.End);

            var lastOffset = PartitionAlignmentBytes;
            foreach (var range in partitionRanges)
            {
                if (range.Start > lastOffset)
                {
                    var rootPartition = CreateRootPartition(disk, lastOffset, range.Start);
                    if (rootPartition != null)
                    {
                        return AutomaticPartitionPlanResult.Success(
                            disk,
                            efiPartition,
                            rootPartition);
                    }
                }

                if (range.End > lastOffset)
                {
                    lastOffset = range.End;
                }
            }

            if (usableDiskEnd > lastOffset)
            {
                var rootPartition = CreateRootPartition(disk, lastOffset, usableDiskEnd);
                if (rootPartition != null)
                {
                    return AutomaticPartitionPlanResult.Success(
                        disk,
                        efiPartition,
                        rootPartition);
                }
            }
        }

        return AutomaticPartitionPlanResult.Failed(
            foundEfiPartition
                ? AutomaticPartitionPlanFailure.NoSuitableFreeSpace
                : AutomaticPartitionPlanFailure.NoEfiSystemPartition);
    }

    private static PartitionRange BoundToUsableDisk(Partition partition, ulong usableDiskEnd)
    {
        var maximumSize = usableDiskEnd - partition.StartOffset;
        var boundedSize = partition.Size > maximumSize ? maximumSize : partition.Size;
        return new PartitionRange(partition.StartOffset, partition.StartOffset + boundedSize);
    }

    private static PlannedPartition? CreateRootPartition(
        Disk disk,
        ulong startOffset,
        ulong endOffset)
    {
        var alignedStartOffset = AlignUp(startOffset, PartitionAlignmentBytes);
        var alignedEndOffset = AlignDown(endOffset, PartitionAlignmentBytes);

        if (alignedEndOffset <= alignedStartOffset ||
            alignedEndOffset - alignedStartOffset < MinimumPartitionSizeBytes)
        {
            return null;
        }

        return new PlannedPartition
        {
            Id = PlannedPartition.CreateId(),
            DiskId = disk.Id,
            Guid = Partition.CreatePartitionGuid(),
            Name = "Linux root partition",
            Size = alignedEndOffset - alignedStartOffset,
            StartOffset = alignedStartOffset,
            FileSystem = FileSystem.EXT4,
            MountPoint = "/",
            IsSystem = false,
            IsExisting = false
        };
    }

    private static ulong AlignUp(ulong value, ulong alignment)
    {
        var remainder = value % alignment;
        return remainder == 0 ? value : value + alignment - remainder;
    }

    private static ulong AlignDown(ulong value, ulong alignment)
    {
        return value - value % alignment;
    }

    private readonly record struct PartitionRange(ulong Start, ulong End);
}
