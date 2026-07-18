using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LinuxInstaller.Models;

public partial class PartitionPlan : ObservableObject
{
    private const ulong PartitionAlignmentBytes = 1024UL * 1024UL;

    // Represents the disk chosen for installation
    private Disk? _targetDisk;
    public Disk? TargetDisk
    {
        get => _targetDisk;
        set
        {
            if (SetProperty(ref _targetDisk, value))
            {
                Reset();
            }
        }
    }

    // History of partition states
    private List<List<PlannedPartition>> _partitionHistory = [];
    public List<List<PlannedPartition>> PartitionHistory
    {
        get => _partitionHistory;
        set => SetProperty(ref _partitionHistory, value);
    }

    // Constructor
    public PartitionPlan()
    {
        // Partitions and PartitionHistory will be initialized/reset when TargetDisk is set.
    }

    public void Reset()
    {
        PartitionHistory.Clear();
        if (TargetDisk != null)
        {
            // Clone partitions from TargetDisk to ensure independent copies
            PartitionHistory.Add([.. TargetDisk.Partitions.Select(p => PlannedPartition.FromPartition(p))]);
        }
    }

    public void AddPartition(PlannedPartition newPartition)
    {
        ArgumentNullException.ThrowIfNull(newPartition);
        if (PartitionHistory.Count == 0)
        {
            return;
        }

        var currentPartitions = PartitionHistory.Last();
        if (string.IsNullOrWhiteSpace(newPartition.Id) ||
            currentPartitions.Any(partition =>
                string.Equals(
                    partition.Id,
                    newPartition.Id,
                    StringComparison.OrdinalIgnoreCase)))
        {
            do
            {
                newPartition.Id = PlannedPartition.CreateId();
            }
            while (currentPartitions.Any(partition =>
                string.Equals(
                    partition.Id,
                    newPartition.Id,
                    StringComparison.OrdinalIgnoreCase)));
        }

        newPartition.IsExisting = false;
        PartitionHistory.Add([.. PartitionHistory.Last(), newPartition]);
    }

    public void EditPartition(PlannedPartition oldPartition, PlannedPartition updatedPartition)
    {
        ArgumentNullException.ThrowIfNull(oldPartition);
        ArgumentNullException.ThrowIfNull(updatedPartition);
        if (PartitionHistory.Count == 0 ||
            oldPartition.IsProtected ||
            updatedPartition.IsProtected)
        {
            return;
        }

        var index = PartitionHistory.Last().IndexOf(oldPartition);
        if (index != -1)
        {
            updatedPartition.Id = oldPartition.Id;
            updatedPartition.DiskId = oldPartition.DiskId;
            updatedPartition.IsExisting = false;
            List<PlannedPartition> partitions = [.. PartitionHistory.Last()];
            partitions[index] = updatedPartition;
            PartitionHistory.Add(partitions);
        }
    }

    public void DeletePartition(PlannedPartition partitionToDelete)
    {
        ArgumentNullException.ThrowIfNull(partitionToDelete);
        if (PartitionHistory.Count == 0 || partitionToDelete.IsProtected)
        {
            return;
        }

        var partitions = new List<PlannedPartition>(PartitionHistory.Last());
        var index = partitions.IndexOf(partitionToDelete);
        if (index != -1)
        {
            partitions.RemoveAt(index);
            PartitionHistory.Add(partitions);
        }
    }

    public bool IsValid
    {
        get
        {
            if (TargetDisk is not { IsEligibleForInstallation: true } targetDisk ||
                targetDisk.Size <= PartitionAlignmentBytes * 2 ||
                PartitionHistory.Count == 0)
            {
                return false;
            }

            var partitions = PartitionHistory.Last();
            if (partitions.Count == 0 ||
                partitions.Any(partition => string.IsNullOrWhiteSpace(partition.Id)) ||
                partitions.Select(partition => partition.Id)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count() != partitions.Count)
            {
                return false;
            }

            var roots = partitions
                .Where(partition => partition.MountPoint == "/")
                .ToList();
            if (roots.Count != 1 ||
                roots[0].IsProtected ||
                roots[0].IsExisting ||
                roots[0].FileSystem != FileSystem.LINUX)
            {
                return false;
            }

            if (!ProtectedPartitionsAreUnchanged(targetDisk, partitions))
            {
                return false;
            }

            var usableDiskEnd = targetDisk.Size - PartitionAlignmentBytes;
            ulong previousEnd = 0;
            foreach (var partition in partitions.OrderBy(partition => partition.StartOffset))
            {
                if (partition.Size == 0 ||
                    partition.StartOffset >= targetDisk.Size ||
                    partition.Size > ulong.MaxValue - partition.StartOffset)
                {
                    return false;
                }

                var endOffset = partition.StartOffset + partition.Size;
                if (endOffset > targetDisk.Size || partition.StartOffset < previousEnd)
                {
                    return false;
                }

                if (!partition.IsExisting &&
                    (partition.IsProtected ||
                     !string.Equals(
                         partition.DiskId,
                         targetDisk.Id,
                         StringComparison.OrdinalIgnoreCase) ||
                     partition.StartOffset < PartitionAlignmentBytes ||
                     partition.StartOffset >= usableDiskEnd ||
                     endOffset > usableDiskEnd ||
                     partition.StartOffset % PartitionAlignmentBytes != 0 ||
                     partition.Size % PartitionAlignmentBytes != 0))
                {
                    return false;
                }

                previousEnd = endOffset;
            }

            return true;
        }
    }

    private static bool ProtectedPartitionsAreUnchanged(
        Disk targetDisk,
        IReadOnlyList<PlannedPartition> partitions)
    {
        var existingPartitions = partitions
            .Where(partition => partition.IsExisting)
            .ToList();
        if (existingPartitions.Count != targetDisk.Partitions.Count)
        {
            return false;
        }

        return targetDisk.Partitions.All(original =>
            existingPartitions.Count(current => ProtectedPartitionMatches(original, current)) == 1);
    }

    private static bool ProtectedPartitionMatches(
        Partition original,
        PlannedPartition current)
    {
        return current.IsExisting &&
            current.IsProtected &&
            string.IsNullOrWhiteSpace(current.MountPoint) &&
            string.Equals(current.Id, original.Id, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(current.DiskId, original.DiskId, StringComparison.OrdinalIgnoreCase) &&
            current.Number == original.Number &&
            current.UniqueId == original.UniqueId &&
            current.Guid == original.Guid &&
            current.Type == original.Type &&
            current.Size == original.Size &&
            current.StartOffset == original.StartOffset &&
            current.FileSystem == original.FileSystem &&
            current.IsBoot == original.IsBoot &&
            current.IsSystem == original.IsSystem &&
            current.IsReadOnly == original.IsReadOnly &&
            current.IsHidden == original.IsHidden;
    }
}
