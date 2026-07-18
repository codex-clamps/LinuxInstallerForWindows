using System.Collections.Generic;

namespace LinuxInstaller.Models;

public class Disk
{
    public required string Id { get; set; }
    public uint Number { get; set; }
    public string UniqueId { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public required ulong Size { get; set; }
    public string PartitionStyle { get; set; } = string.Empty;
    public ushort? BusTypeCode { get; set; }
    public string BusType { get; set; } = string.Empty;
    public bool IsRemovable { get; set; }
    public bool IsBootable { get; set; }
    public bool IsSystem { get; set; }
    public bool IsReadOnly { get; set; }
    public bool IsOffline { get; set; }
    public bool IsEligibleForInstallation { get; set; }
    public string IneligibilityReason { get; set; } = string.Empty;
    public required List<Partition> Partitions { get; set; }
}
