namespace LinuxInstaller.Models;

public enum FileSystem
{
    Unknown,
    NTFS,
    FAT32,
    EXFAT,
    EXT4,
    BTRFS
}

public static class FS
{
    public static bool IsInstallable(FileSystem fileSystem) =>
        fileSystem is FileSystem.EXT4 or FileSystem.BTRFS;

    public static string ToString(FileSystem fileSystem) => fileSystem switch
    {
        FileSystem.NTFS => "NTFS",
        FileSystem.FAT32 => "FAT32",
        FileSystem.EXFAT => "exFAT",
        FileSystem.EXT4 => "EXT4",
        FileSystem.BTRFS => "Btrfs",
        _ => "Unknown"
    };

    public static string ToLinuxName(FileSystem fileSystem) => fileSystem switch
    {
        FileSystem.FAT32 => "fat32",
        FileSystem.EXT4 => "ext4",
        FileSystem.BTRFS => "btrfs",
        _ => throw new ArgumentOutOfRangeException(
            nameof(fileSystem),
            fileSystem,
            "The filesystem is not supported by the installer.")
    };

    public static FileSystem ToFileSystem(string fileSystem) =>
        fileSystem.Trim().ToUpperInvariant() switch
        {
            "NTFS" => FileSystem.NTFS,
            "FAT" or "FAT32" or "VFAT" => FileSystem.FAT32,
            "EXFAT" => FileSystem.EXFAT,
            "EXT2" or "EXT3" or "EXT4" or "LINUX" => FileSystem.EXT4,
            "BTRFS" => FileSystem.BTRFS,
            _ => FileSystem.Unknown
        };
}
