using LinuxInstaller.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LinuxInstaller.Services;

public sealed class PartitionService
{
    private readonly IStorageManager _storageManager;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly object _cacheLock = new();
    private IReadOnlyList<Disk>? _cachedDisks;
    private long _cacheGeneration;

    public PartitionService(IStorageManager storageManager)
    {
        _storageManager = storageManager ?? throw new ArgumentNullException(nameof(storageManager));
    }

    public async Task<IReadOnlyList<Disk>> GetAvailableDisksAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        if (!forceRefresh && TryGetCachedDisks(out var cachedDisks))
        {
            return cachedDisks;
        }

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            long refreshGeneration;
            lock (_cacheLock)
            {
                if (!forceRefresh && _cachedDisks != null)
                {
                    return CloneDisks(_cachedDisks);
                }

                refreshGeneration = _cacheGeneration;
            }

            var discoveredDisks = CloneDisks(
                ExcludeAmbiguousDisks(
                    await _storageManager.GetDisksAsync(cancellationToken)));
            lock (_cacheLock)
            {
                if (refreshGeneration == _cacheGeneration)
                {
                    _cachedDisks = discoveredDisks;
                }
            }

            return CloneDisks(discoveredDisks);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public async Task<IReadOnlyList<Partition>> GetPartitionsAsync(
        string diskId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(diskId);

        var disks = await GetAvailableDisksAsync(cancellationToken: cancellationToken);
        return disks.FirstOrDefault(
            disk => string.Equals(disk.Id, diskId, StringComparison.Ordinal))?.Partitions ?? [];
    }

    public void InvalidateCache()
    {
        lock (_cacheLock)
        {
            _cacheGeneration++;
            _cachedDisks = null;
        }
    }

    private bool TryGetCachedDisks(out IReadOnlyList<Disk> disks)
    {
        lock (_cacheLock)
        {
            if (_cachedDisks != null)
            {
                disks = CloneDisks(_cachedDisks);
                return true;
            }
        }

        disks = [];
        return false;
    }

    private static IReadOnlyList<Disk> ExcludeAmbiguousDisks(
        IReadOnlyList<Disk> disks)
    {
        var duplicateIds = disks
            .Where(disk => !string.IsNullOrWhiteSpace(disk.Id))
            .GroupBy(disk => disk.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Skip(1).Any())
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (duplicateIds.Count == 0 &&
            disks.All(disk => !string.IsNullOrWhiteSpace(disk.Id)))
        {
            return disks;
        }

        return disks
            .Where(disk =>
                !string.IsNullOrWhiteSpace(disk.Id) &&
                !duplicateIds.Contains(disk.Id))
            .ToList();
    }

    private static IReadOnlyList<Disk> CloneDisks(IEnumerable<Disk> disks) =>
        disks.Select(CloneDisk).ToList();

    private static Disk CloneDisk(Disk disk) => new()
    {
        Id = disk.Id,
        Number = disk.Number,
        UniqueId = disk.UniqueId,
        Path = disk.Path,
        Name = disk.Name,
        Size = disk.Size,
        PartitionStyle = disk.PartitionStyle,
        BusTypeCode = disk.BusTypeCode,
        BusType = disk.BusType,
        IsRemovable = disk.IsRemovable,
        IsBootable = disk.IsBootable,
        IsSystem = disk.IsSystem,
        IsReadOnly = disk.IsReadOnly,
        IsOffline = disk.IsOffline,
        IsEligibleForInstallation = disk.IsEligibleForInstallation,
        IneligibilityReason = disk.IneligibilityReason,
        Partitions = disk.Partitions.Select(partition => partition.Clone()).ToList()
    };
}
