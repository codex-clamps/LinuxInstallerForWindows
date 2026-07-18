using System;

namespace LinuxInstaller.Models;

public enum FirmwareBootMode
{
    Unknown,
    LegacyBios,
    Uefi
}

public enum SecureBootState
{
    Unknown,
    Disabled,
    Enabled
}

public enum BitLockerVolumeState
{
    Unknown,
    FullyDecrypted,
    EncryptedProtectionOff,
    EncryptedProtectionOn
}

public sealed record SystemAnalysisSnapshot
{
    public required bool IsAdministrator { get; init; }
    public required FirmwareBootMode BootMode { get; init; }
    public required SecureBootState SecureBoot { get; init; }
    public required string SystemDrive { get; init; }
    public required BitLockerVolumeState SystemDriveBitLocker { get; init; }
    public required DateTimeOffset CollectedAtUtc { get; init; }
}
