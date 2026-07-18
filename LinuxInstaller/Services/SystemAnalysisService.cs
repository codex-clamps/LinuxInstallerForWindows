using LinuxInstaller.Models;
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;

namespace LinuxInstaller.Services;

public sealed class SystemAnalysisService : ISystemAnalysisService
{
    private const string SecureBootRegistryPath = @"SYSTEM\CurrentControlSet\Control\SecureBoot\State";
    private const string BitLockerNamespace = @"\\.\root\CIMV2\Security\MicrosoftVolumeEncryption";

    public async Task<SystemAnalysisSnapshot> AnalyzeAsync(
        string? driveLetter = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var systemDrive = NormalizeDriveLetter(driveLetter);
        var bootMode = GetFirmwareBootMode();
        var bitLockerState = await Task.Run(
            () => GetBitLockerVolumeState(systemDrive),
            cancellationToken);

        return new SystemAnalysisSnapshot
        {
            IsAdministrator = GetAdministratorStatus(),
            BootMode = bootMode,
            SecureBoot = GetSecureBootState(bootMode),
            SystemDrive = systemDrive,
            SystemDriveBitLocker = bitLockerState,
            CollectedAtUtc = DateTimeOffset.UtcNow
        };
    }

    public Task RelaunchAsAdminAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Administrator relaunch is only supported on Windows.");
        }

        if (GetAdministratorStatus())
        {
            return Task.CompletedTask;
        }

        var executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("The current executable path could not be determined.");
        var commandLineArguments = Environment.GetCommandLineArgs();
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = Environment.CurrentDirectory
        };

        var isDotnetHost = string.Equals(
            Path.GetFileNameWithoutExtension(executablePath),
            "dotnet",
            StringComparison.OrdinalIgnoreCase);
        var firstArgumentIndex = isDotnetHost ? 0 : 1;

        for (var index = firstArgumentIndex; index < commandLineArguments.Length; index++)
        {
            startInfo.ArgumentList.Add(commandLineArguments[index]);
        }

        _ = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The elevated process could not be started.");

        return Task.CompletedTask;
    }

    private static bool GetAdministratorStatus()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static FirmwareBootMode GetFirmwareBootMode()
    {
        if (!OperatingSystem.IsWindows() || !GetFirmwareType(out var firmwareType))
        {
            return FirmwareBootMode.Unknown;
        }

        return firmwareType switch
        {
            WindowsFirmwareType.Bios => FirmwareBootMode.LegacyBios,
            WindowsFirmwareType.Uefi => FirmwareBootMode.Uefi,
            _ => FirmwareBootMode.Unknown
        };
    }

    private static SecureBootState GetSecureBootState(FirmwareBootMode bootMode)
    {
        if (bootMode == FirmwareBootMode.LegacyBios)
        {
            return SecureBootState.Disabled;
        }

        if (!OperatingSystem.IsWindows() || bootMode != FirmwareBootMode.Uefi)
        {
            return SecureBootState.Unknown;
        }

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(SecureBootRegistryPath);
            return key?.GetValue("UEFISecureBootEnabled") switch
            {
                int value when value == 1 => SecureBootState.Enabled,
                int => SecureBootState.Disabled,
                _ => SecureBootState.Unknown
            };
        }
        catch (SecurityException)
        {
            return SecureBootState.Unknown;
        }
        catch (UnauthorizedAccessException)
        {
            return SecureBootState.Unknown;
        }
    }

    private static BitLockerVolumeState GetBitLockerVolumeState(string driveLetter)
    {
        if (!OperatingSystem.IsWindows())
        {
            return BitLockerVolumeState.Unknown;
        }

        try
        {
            var scope = new ManagementScope(BitLockerNamespace);
            scope.Connect();

            var query = new ObjectQuery(
                $"SELECT ProtectionStatus FROM Win32_EncryptableVolume WHERE DriveLetter = '{driveLetter}'");
            using var searcher = new ManagementObjectSearcher(scope, query);
            using var volumes = searcher.Get();

            foreach (ManagementObject volume in volumes)
            {
                using (volume)
                using (var conversionStatusResult = volume.InvokeMethod(
                    "GetConversionStatus",
                    null,
                    null))
                {
                    if (conversionStatusResult == null ||
                        Convert.ToUInt32(conversionStatusResult["ReturnValue"]) != 0)
                    {
                        return BitLockerVolumeState.Unknown;
                    }

                    var conversionStatus = Convert.ToUInt32(conversionStatusResult["ConversionStatus"]);
                    var protectionStatus = Convert.ToUInt32(volume["ProtectionStatus"]);

                    if (conversionStatus == 0)
                    {
                        return BitLockerVolumeState.FullyDecrypted;
                    }

                    return protectionStatus switch
                    {
                        1 => BitLockerVolumeState.EncryptedProtectionOn,
                        0 => BitLockerVolumeState.EncryptedProtectionOff,
                        _ => BitLockerVolumeState.Unknown
                    };
                }
            }
        }
        catch (ManagementException)
        {
            return BitLockerVolumeState.Unknown;
        }
        catch (UnauthorizedAccessException)
        {
            return BitLockerVolumeState.Unknown;
        }
        catch (COMException)
        {
            return BitLockerVolumeState.Unknown;
        }
        catch (InvalidCastException)
        {
            return BitLockerVolumeState.Unknown;
        }
        catch (FormatException)
        {
            return BitLockerVolumeState.Unknown;
        }
        catch (OverflowException)
        {
            return BitLockerVolumeState.Unknown;
        }

        return BitLockerVolumeState.Unknown;
    }

    private static string NormalizeDriveLetter(string? driveLetter)
    {
        var value = string.IsNullOrWhiteSpace(driveLetter)
            ? Path.GetPathRoot(Environment.SystemDirectory)
            : driveLetter.Trim();

        if (value is { Length: >= 2 } && char.IsLetter(value[0]) && value[1] == ':')
        {
            return $"{char.ToUpperInvariant(value[0])}:";
        }

        throw new ArgumentException("A Windows drive letter such as C: is required.", nameof(driveLetter));
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFirmwareType(out WindowsFirmwareType firmwareType);

    private enum WindowsFirmwareType
    {
        Unknown,
        Bios,
        Uefi,
        Max
    }
}
