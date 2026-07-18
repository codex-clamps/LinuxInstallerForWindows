using LinuxInstaller.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace LinuxInstaller.Services;

public sealed class WindowsStorageManager : IStorageManager
{
    private const string StorageNamespace = @"\\.\root\Microsoft\Windows\Storage";

    private const string EfiSystemPartitionType = "C12A7328-F81F-11D2-BA4B-00A0C93EC93B";
    private const string MicrosoftReservedPartitionType = "E3C9E316-0B5C-4DB8-817D-F92DF00215AE";
    private const string MicrosoftRecoveryPartitionType = "DE94BBA4-06D1-4D40-A16A-BFD50179D6AC";

    public Task<IReadOnlyList<Disk>> GetDisksAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Windows storage discovery is only supported on Windows.");
        }

        return Task.Run<IReadOnlyList<Disk>>(
            () => DiscoverDisks(cancellationToken),
            cancellationToken);
    }

    private static IReadOnlyList<Disk> DiscoverDisks(CancellationToken cancellationToken)
    {
        try
        {
            var scope = new ManagementScope(StorageNamespace);
            scope.Connect();
            cancellationToken.ThrowIfCancellationRequested();

            var volumesByKey = ReadVolumes(scope, cancellationToken);
            var diskRecords = ExcludeAmbiguousDiskRecords(
                ReadDiskRecords(scope, cancellationToken));
            var diskIdsByNumber = diskRecords.ToDictionary(disk => disk.Number, disk => disk.Id);
            var partitionsByDisk = ReadPartitions(
                scope,
                diskIdsByNumber,
                volumesByKey,
                cancellationToken);

            return diskRecords
                .OrderBy(disk => disk.Number)
                .Select(disk =>
                {
                    var partitionStyle = GetPartitionStyleName(disk.PartitionStyle);
                    var ineligibilityReason = GetIneligibilityReason(
                        disk.PartitionStyle,
                        disk.BusType,
                        disk.IsReadOnly,
                        disk.IsOffline,
                        disk.IsClustered,
                        disk.HealthStatus,
                        disk.HasDurableIdentity);

                    return new Disk
                    {
                        Id = disk.Id,
                        Number = disk.Number,
                        UniqueId = disk.UniqueId,
                        Path = disk.Path,
                        Name = string.IsNullOrWhiteSpace(disk.Name)
                            ? $"Disk {disk.Number}"
                            : disk.Name,
                        Size = disk.Size,
                        PartitionStyle = partitionStyle,
                        BusTypeCode = disk.BusType,
                        BusType = GetBusTypeName(disk.BusType),
                        IsRemovable = IsRemovableBusType(disk.BusType),
                        IsBootable = disk.BootFromDisk || disk.IsBoot,
                        IsSystem = disk.IsSystem,
                        IsReadOnly = disk.IsReadOnly.GetValueOrDefault(),
                        IsOffline = disk.IsOffline.GetValueOrDefault(),
                        IsEligibleForInstallation = string.IsNullOrEmpty(ineligibilityReason),
                        IneligibilityReason = ineligibilityReason,
                        Partitions = partitionsByDisk.TryGetValue(disk.Number, out var partitions)
                            ? partitions.OrderBy(partition => partition.StartOffset).ToList()
                            : []
                    };
                })
                .ToList();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ManagementException exception) when (IsAccessDenied(exception))
        {
            throw new InvalidOperationException(
                "Windows denied access to the Storage Management provider.",
                exception);
        }
        catch (ManagementException exception)
        {
            throw new InvalidOperationException(
                "The Windows Storage Management provider could not enumerate the system storage topology.",
                exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new InvalidOperationException(
                "Windows denied access to the Storage Management provider.",
                exception);
        }
        catch (COMException exception)
        {
            throw new InvalidOperationException(
                "The Windows Storage Management provider could not be contacted.",
                exception);
        }
    }

    private static List<DiskRecord> ReadDiskRecords(
        ManagementScope scope,
        CancellationToken cancellationToken)
    {
        using var searcher = new ManagementObjectSearcher(
            scope,
            new ObjectQuery("SELECT * FROM MSFT_Disk"));
        using var results = searcher.Get();

        var disks = new List<DiskRecord>();
        foreach (ManagementObject diskObject in results)
        {
            using (diskObject)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var number = GetUInt32(diskObject, "Number");
                var size = GetUInt64(diskObject, "Size");
                if (!number.HasValue || !size.HasValue)
                {
                    continue;
                }

                var guid = GetString(diskObject, "Guid");
                var uniqueId = GetString(diskObject, "UniqueId");
                var objectId = GetString(diskObject, "ObjectId");
                var path = GetString(diskObject, "Path");
                var uniqueIdFormat = GetUInt16(diskObject, "UniqueIdFormat");

                disks.Add(new DiskRecord(
                    CreateDiskId(guid, uniqueId, uniqueIdFormat, objectId, path, number.Value),
                    number.Value,
                    uniqueId,
                    path,
                    GetString(diskObject, "FriendlyName"),
                    size.Value,
                    GetUInt16(diskObject, "PartitionStyle"),
                    GetUInt16(diskObject, "BusType"),
                    GetBoolean(diskObject, "BootFromDisk"),
                    GetBoolean(diskObject, "IsBoot"),
                    GetBoolean(diskObject, "IsSystem"),
                    GetNullableBoolean(diskObject, "IsReadOnly"),
                    GetNullableBoolean(diskObject, "IsOffline"),
                    GetNullableBoolean(diskObject, "IsClustered"),
                    GetUInt16(diskObject, "HealthStatus"),
                    HasDurableDiskIdentity(guid, uniqueId)));
            }
        }

        return disks;
    }

    private static List<DiskRecord> ExcludeAmbiguousDiskRecords(
        IReadOnlyList<DiskRecord> diskRecords)
    {
        var duplicateNumbers = FindDuplicates(
            diskRecords.Select(disk => disk.Number),
            EqualityComparer<uint>.Default);
        var duplicateIds = FindDuplicates(
            diskRecords.Select(disk => disk.Id),
            StringComparer.OrdinalIgnoreCase);

        return diskRecords
            .Where(disk =>
                !duplicateNumbers.Contains(disk.Number) &&
                !duplicateIds.Contains(disk.Id))
            .ToList();
    }

    private static HashSet<T> FindDuplicates<T>(
        IEnumerable<T> values,
        IEqualityComparer<T> comparer)
        where T : notnull
    {
        var seen = new HashSet<T>(comparer);
        var duplicates = new HashSet<T>(comparer);
        foreach (var value in values)
        {
            if (!seen.Add(value))
            {
                duplicates.Add(value);
            }
        }

        return duplicates;
    }

    private static Dictionary<uint, List<Partition>> ReadPartitions(
        ManagementScope scope,
        IReadOnlyDictionary<uint, string> diskIdsByNumber,
        IReadOnlyDictionary<string, VolumeRecord> volumesByKey,
        CancellationToken cancellationToken)
    {
        using var searcher = new ManagementObjectSearcher(
            scope,
            new ObjectQuery("SELECT * FROM MSFT_Partition"));
        using var results = searcher.Get();

        var partitionsByDisk = new Dictionary<uint, List<Partition>>();
        foreach (ManagementObject partitionObject in results)
        {
            using (partitionObject)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var diskNumber = GetUInt32(partitionObject, "DiskNumber");
                var partitionNumber = GetUInt32(partitionObject, "PartitionNumber");
                var size = GetUInt64(partitionObject, "Size");
                var offset = GetUInt64(partitionObject, "Offset");
                if (!diskNumber.HasValue ||
                    !partitionNumber.HasValue ||
                    !size.HasValue ||
                    !offset.HasValue ||
                    !diskIdsByNumber.TryGetValue(diskNumber.Value, out var diskId))
                {
                    continue;
                }

                var accessPaths = GetStringArray(partitionObject, "AccessPaths");
                var driveLetter = NormalizeDriveLetter(GetString(partitionObject, "DriveLetter"));
                var volume = FindVolume(accessPaths, driveLetter, volumesByKey);
                var fileSystem = FS.ToFileSystem(volume?.FileSystem ?? string.Empty);
                var supportedSize = fileSystem == FileSystem.NTFS
                    ? TryGetSupportedSize(partitionObject)
                    : default;
                var guid = GetString(partitionObject, "Guid");
                var uniqueId = GetString(partitionObject, "UniqueId");
                var objectId = GetString(partitionObject, "ObjectId");
                var gptType = GetString(partitionObject, "GptType");
                var partitionType = GetPartitionTypeName(
                    gptType,
                    GetUInt16(partitionObject, "MbrType"));

                var partition = new Partition
                {
                    Id = CreatePartitionId(
                        guid,
                        uniqueId,
                        objectId,
                        diskId,
                        partitionNumber.Value,
                        offset.Value),
                    DiskId = diskId,
                    Number = partitionNumber.Value,
                    UniqueId = uniqueId,
                    Guid = NormalizeGuid(guid),
                    Name = CreatePartitionName(
                        volume?.Label ?? string.Empty,
                        partitionType,
                        driveLetter,
                        partitionNumber.Value),
                    Type = partitionType,
                    Size = size.Value,
                    StartOffset = offset.Value,
                    DriveLetter = driveLetter,
                    AccessPaths = accessPaths,
                    VolumeId = volume?.Id ?? string.Empty,
                    FileSystemLabel = volume?.Label ?? string.Empty,
                    VolumeSize = volume?.Size,
                    VolumeSizeRemaining = volume?.SizeRemaining,
                    FileSystem = fileSystem,
                    IsBoot = GetBoolean(partitionObject, "IsBoot"),
                    IsSystem = GetBoolean(partitionObject, "IsSystem"),
                    IsReadOnly = GetBoolean(partitionObject, "IsReadOnly"),
                    IsHidden = GetBoolean(partitionObject, "IsHidden"),
                    IsExisting = true,
                    SupportedSizeMinimum = supportedSize.Minimum,
                    SupportedSizeMaximum = supportedSize.Maximum
                };

                if (!partitionsByDisk.TryGetValue(diskNumber.Value, out var diskPartitions))
                {
                    diskPartitions = [];
                    partitionsByDisk.Add(diskNumber.Value, diskPartitions);
                }

                diskPartitions.Add(partition);
            }
        }

        return partitionsByDisk;
    }

    private static Dictionary<string, VolumeRecord> ReadVolumes(
        ManagementScope scope,
        CancellationToken cancellationToken)
    {
        using var searcher = new ManagementObjectSearcher(
            scope,
            new ObjectQuery("SELECT * FROM MSFT_Volume"));
        using var results = searcher.Get();

        var volumesByKey = new Dictionary<string, VolumeRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (ManagementObject volumeObject in results)
        {
            using (volumeObject)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var path = GetString(volumeObject, "Path");
                var objectId = GetString(volumeObject, "ObjectId");
                var uniqueId = GetString(volumeObject, "UniqueId");
                var driveLetter = NormalizeDriveLetter(GetString(volumeObject, "DriveLetter"));
                var volume = new VolumeRecord(
                    FirstNonEmpty(path, uniqueId, objectId, driveLetter),
                    GetString(volumeObject, "FileSystem"),
                    GetString(volumeObject, "FileSystemLabel"),
                    GetUInt64(volumeObject, "Size"),
                    GetUInt64(volumeObject, "SizeRemaining"));

                AddVolumeKey(volumesByKey, path, volume);
                AddVolumeKey(volumesByKey, objectId, volume);
                AddVolumeKey(volumesByKey, uniqueId, volume);
                AddVolumeKey(volumesByKey, driveLetter, volume);
            }
        }

        return volumesByKey;
    }

    private static VolumeRecord? FindVolume(
        IReadOnlyList<string> accessPaths,
        string driveLetter,
        IReadOnlyDictionary<string, VolumeRecord> volumesByKey)
    {
        foreach (var accessPath in accessPaths)
        {
            var key = NormalizeStoragePath(accessPath);
            if (!string.IsNullOrEmpty(key) && volumesByKey.TryGetValue(key, out var volume))
            {
                return volume;
            }
        }

        var driveKey = NormalizeStoragePath(driveLetter);
        return !string.IsNullOrEmpty(driveKey) && volumesByKey.TryGetValue(driveKey, out var driveVolume)
            ? driveVolume
            : null;
    }

    private static void AddVolumeKey(
        IDictionary<string, VolumeRecord> volumesByKey,
        string value,
        VolumeRecord volume)
    {
        var key = NormalizeStoragePath(value);
        if (!string.IsNullOrEmpty(key))
        {
            volumesByKey.TryAdd(key, volume);
        }
    }

    private static SupportedSize TryGetSupportedSize(ManagementObject partitionObject)
    {
        try
        {
            // This provider method only calculates the range; it does not resize the partition.
            using var result = partitionObject.InvokeMethod("GetSupportedSize", null, null);
            if (result == null || GetUInt32(result, "ReturnValue") is not 0)
            {
                return default;
            }

            return new SupportedSize(
                GetUInt64(result, "SizeMin"),
                GetUInt64(result, "SizeMax"));
        }
        catch (ManagementException)
        {
            return default;
        }
        catch (UnauthorizedAccessException)
        {
            return default;
        }
        catch (COMException)
        {
            return default;
        }
        catch (InvalidOperationException)
        {
            return default;
        }
    }

    private static string GetIneligibilityReason(
        ushort? partitionStyle,
        ushort? busType,
        bool? isReadOnly,
        bool? isOffline,
        bool? isClustered,
        ushort? healthStatus,
        bool hasDurableIdentity)
    {
        if (!hasDurableIdentity)
        {
            return "Windows did not report a durable physical identity for this disk.";
        }

        if (partitionStyle != 2)
        {
            return "Only GPT disks are supported.";
        }

        if (!isOffline.HasValue || !isReadOnly.HasValue)
        {
            return "Windows could not verify that the disk is online and writable.";
        }

        if (isOffline.Value)
        {
            return "The disk is offline.";
        }

        if (isReadOnly.Value)
        {
            return "The disk is read-only.";
        }

        if (!isClustered.HasValue)
        {
            return "Windows could not verify whether the disk is managed by a cluster.";
        }

        if (isClustered.Value)
        {
            return "Cluster-managed disks are not eligible installation targets.";
        }

        if (healthStatus != 0)
        {
            return healthStatus switch
            {
                1 => "The disk health status is warning.",
                2 => "The disk is unhealthy.",
                _ => "Windows could not verify that the disk is healthy."
            };
        }

        return busType switch
        {
            1 => string.Empty,
            3 => string.Empty,
            10 => string.Empty,
            11 => string.Empty,
            17 => string.Empty,
            18 => string.Empty,
            19 => string.Empty,
            null or 0 => "Windows could not identify the disk bus type.",
            2 => "ATAPI devices are outside the supported installation-disk scope.",
            4 => "IEEE 1394 disks are outside the supported internal-disk scope.",
            5 => "SSA disks are outside the supported internal-disk scope.",
            6 => "Fibre Channel disks are outside the supported internal-disk scope.",
            7 => "USB disks are not eligible installation targets.",
            8 => "Firmware RAID disks are outside the initial supported scope.",
            9 => "iSCSI disks are outside the initial supported scope.",
            12 => "SD media is not eligible installation storage.",
            13 => "MMC media is not eligible installation storage.",
            14 => "Virtual disks are outside the physical-disk installation scope.",
            15 => "File-backed virtual disks are not eligible installation targets.",
            16 => "Storage Spaces disks are outside the initial supported scope.",
            _ => $"Bus type {busType.Value} is outside the supported installation-disk scope."
        };
    }

    private static bool IsAccessDenied(ManagementException exception) =>
        exception.ErrorCode == ManagementStatus.AccessDenied ||
        exception.HResult == unchecked((int)0x80070005) ||
        exception.InnerException is UnauthorizedAccessException ||
        exception.InnerException is COMException
        {
            HResult: unchecked((int)0x80070005)
        };

    private static bool IsRemovableBusType(ushort? busType) => busType is 4 or 7 or 12 or 13;

    private static string GetBusTypeName(ushort? busType) => busType switch
    {
        1 => "SCSI",
        2 => "ATAPI",
        3 => "ATA",
        4 => "IEEE 1394",
        5 => "SSA",
        6 => "Fibre Channel",
        7 => "USB",
        8 => "RAID",
        9 => "iSCSI",
        10 => "SAS",
        11 => "SATA",
        12 => "SD",
        13 => "MMC",
        14 => "Virtual",
        15 => "File-backed Virtual",
        16 => "Storage Spaces",
        17 => "NVMe",
        18 => "SCM",
        19 => "UFS",
        _ => "Unknown"
    };

    private static string CreateDiskId(
        string guid,
        string uniqueId,
        ushort? uniqueIdFormat,
        string objectId,
        string path,
        uint number)
    {
        var normalizedUniqueId = NormalizeStorageIdentifier(uniqueId);
        if (!string.IsNullOrEmpty(normalizedUniqueId))
        {
            return $"unique:{uniqueIdFormat?.ToString() ?? "unknown"}:{normalizedUniqueId}";
        }

        var normalizedGuid = NormalizeGuid(guid);
        if (!string.IsNullOrEmpty(normalizedGuid))
        {
            return $"gpt:{normalizedGuid}";
        }

        return FirstNonEmpty(objectId, path, $"disk-number:{number}");
    }

    private static bool HasDurableDiskIdentity(string guid, string uniqueId) =>
        !string.IsNullOrEmpty(NormalizeStorageIdentifier(uniqueId)) ||
        !string.IsNullOrEmpty(NormalizeGuid(guid));

    private static string CreatePartitionId(
        string guid,
        string uniqueId,
        string objectId,
        string diskId,
        uint partitionNumber,
        ulong offset)
    {
        var normalizedGuid = NormalizeGuid(guid);
        if (!string.IsNullOrEmpty(normalizedGuid))
        {
            return $"{diskId}:gpt:{normalizedGuid}";
        }

        var normalizedUniqueId = NormalizeStorageIdentifier(uniqueId);
        if (!string.IsNullOrEmpty(normalizedUniqueId))
        {
            return $"{diskId}:unique:{normalizedUniqueId}";
        }

        var normalizedObjectId = NormalizeStorageIdentifier(objectId);
        if (!string.IsNullOrEmpty(normalizedObjectId))
        {
            return $"{diskId}:object:{normalizedObjectId}";
        }

        return $"{diskId}:partition:{partitionNumber}:{offset}";
    }

    private static string CreatePartitionName(
        string label,
        string partitionType,
        string driveLetter,
        uint partitionNumber)
    {
        var baseName = !string.IsNullOrWhiteSpace(label)
            ? label.Trim()
            : !string.IsNullOrWhiteSpace(partitionType)
                ? partitionType
                : $"Partition {partitionNumber}";

        return string.IsNullOrEmpty(driveLetter)
            ? baseName
            : $"{baseName} ({driveLetter})";
    }

    private static string GetPartitionStyleName(ushort? partitionStyle) => partitionStyle switch
    {
        1 => "MBR",
        2 => "GPT",
        _ => "RAW"
    };

    private static string GetPartitionTypeName(string gptType, ushort? mbrType)
    {
        var normalizedGptType = NormalizeGuid(gptType);
        if (string.Equals(normalizedGptType, EfiSystemPartitionType, StringComparison.OrdinalIgnoreCase))
        {
            return "EFI System Partition";
        }

        if (string.Equals(normalizedGptType, MicrosoftReservedPartitionType, StringComparison.OrdinalIgnoreCase))
        {
            return "Microsoft Reserved Partition";
        }

        if (string.Equals(normalizedGptType, MicrosoftRecoveryPartitionType, StringComparison.OrdinalIgnoreCase))
        {
            return "Recovery Partition";
        }

        if (!string.IsNullOrEmpty(normalizedGptType))
        {
            return $"GPT {normalizedGptType}";
        }

        return mbrType.HasValue ? $"MBR 0x{mbrType.Value:X2}" : string.Empty;
    }

    private static string NormalizeDriveLetter(string value)
    {
        var trimmed = value.Trim().Trim('\0');
        if (trimmed.Length == 1 && char.IsLetter(trimmed[0]))
        {
            return $"{char.ToUpperInvariant(trimmed[0])}:";
        }

        if (trimmed.Length >= 2 && char.IsLetter(trimmed[0]) && trimmed[1] == ':')
        {
            return $"{char.ToUpperInvariant(trimmed[0])}:";
        }

        return string.Empty;
    }

    private static string NormalizeStoragePath(string value)
    {
        var driveLetter = NormalizeDriveLetter(value);
        return !string.IsNullOrEmpty(driveLetter)
            ? driveLetter
            : value.Trim().Trim('\0').TrimEnd('\\');
    }

    private static string NormalizeGuid(string value) => value
        .Trim()
        .Trim('\0')
        .Trim('{', '}')
        .ToUpperInvariant();

    private static string NormalizeStorageIdentifier(string value) => value
        .Trim()
        .Trim('\0')
        .ToUpperInvariant();

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static object? GetValue(ManagementBaseObject managementObject, string propertyName)
    {
        try
        {
            return managementObject.Properties[propertyName]?.Value;
        }
        catch (ManagementException)
        {
            return null;
        }
    }

    private static string GetString(ManagementBaseObject managementObject, string propertyName)
    {
        var value = GetValue(managementObject, propertyName);
        return value == null
            ? string.Empty
            : Convert.ToString(value)?.Trim().Trim('\0') ?? string.Empty;
    }

    private static IReadOnlyList<string> GetStringArray(
        ManagementBaseObject managementObject,
        string propertyName)
    {
        var value = GetValue(managementObject, propertyName);
        if (value is string stringValue)
        {
            return string.IsNullOrWhiteSpace(stringValue) ? [] : [stringValue.Trim()];
        }

        if (value is not Array values)
        {
            return [];
        }

        return values
            .Cast<object?>()
            .Select(item => Convert.ToString(item)?.Trim().Trim('\0'))
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Cast<string>()
            .ToList();
    }

    private static bool GetBoolean(ManagementBaseObject managementObject, string propertyName)
    {
        var value = GetValue(managementObject, propertyName);
        return value switch
        {
            bool booleanValue => booleanValue,
            byte byteValue => byteValue != 0,
            ushort unsignedShortValue => unsignedShortValue != 0,
            uint unsignedIntegerValue => unsignedIntegerValue != 0,
            _ => bool.TryParse(Convert.ToString(value), out var parsedValue) && parsedValue
        };
    }

    private static bool? GetNullableBoolean(
        ManagementBaseObject managementObject,
        string propertyName)
    {
        var value = GetValue(managementObject, propertyName);
        return value switch
        {
            bool booleanValue => booleanValue,
            byte byteValue => byteValue != 0,
            ushort unsignedShortValue => unsignedShortValue != 0,
            uint unsignedIntegerValue => unsignedIntegerValue != 0,
            _ => bool.TryParse(Convert.ToString(value), out var parsedValue) ? parsedValue : null
        };
    }

    private static ushort? GetUInt16(ManagementBaseObject managementObject, string propertyName)
    {
        var value = GetValue(managementObject, propertyName);
        return value switch
        {
            ushort unsignedShortValue => unsignedShortValue,
            byte byteValue => byteValue,
            uint unsignedIntegerValue when unsignedIntegerValue <= ushort.MaxValue => (ushort)unsignedIntegerValue,
            int integerValue when integerValue is >= 0 and <= ushort.MaxValue => (ushort)integerValue,
            _ => ushort.TryParse(Convert.ToString(value), out var parsedValue) ? parsedValue : null
        };
    }

    private static uint? GetUInt32(ManagementBaseObject managementObject, string propertyName)
    {
        var value = GetValue(managementObject, propertyName);
        return value switch
        {
            uint unsignedIntegerValue => unsignedIntegerValue,
            ushort unsignedShortValue => unsignedShortValue,
            byte byteValue => byteValue,
            ulong unsignedLongValue when unsignedLongValue <= uint.MaxValue => (uint)unsignedLongValue,
            int integerValue when integerValue >= 0 => (uint)integerValue,
            long longValue when longValue is >= 0 and <= uint.MaxValue => (uint)longValue,
            _ => uint.TryParse(Convert.ToString(value), out var parsedValue) ? parsedValue : null
        };
    }

    private static ulong? GetUInt64(ManagementBaseObject managementObject, string propertyName)
    {
        var value = GetValue(managementObject, propertyName);
        return value switch
        {
            ulong unsignedLongValue => unsignedLongValue,
            uint unsignedIntegerValue => unsignedIntegerValue,
            ushort unsignedShortValue => unsignedShortValue,
            byte byteValue => byteValue,
            long longValue when longValue >= 0 => (ulong)longValue,
            int integerValue when integerValue >= 0 => (ulong)integerValue,
            _ => ulong.TryParse(Convert.ToString(value), out var parsedValue) ? parsedValue : null
        };
    }

    private sealed record DiskRecord(
        string Id,
        uint Number,
        string UniqueId,
        string Path,
        string Name,
        ulong Size,
        ushort? PartitionStyle,
        ushort? BusType,
        bool BootFromDisk,
        bool IsBoot,
        bool IsSystem,
        bool? IsReadOnly,
        bool? IsOffline,
        bool? IsClustered,
        ushort? HealthStatus,
        bool HasDurableIdentity);

    private sealed record VolumeRecord(
        string Id,
        string FileSystem,
        string Label,
        ulong? Size,
        ulong? SizeRemaining);

    private readonly record struct SupportedSize(ulong? Minimum, ulong? Maximum);
}
