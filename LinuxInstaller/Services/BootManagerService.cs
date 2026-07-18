using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LinuxInstaller.Services;

public sealed class BootManagerService
{
    private const string BootEntryDescription = "LinuxInstallerForWindows";
    private static readonly Regex EntryIdentifierPattern = new(
        @"\{[0-9a-fA-F-]{36}\}",
        RegexOptions.Compiled);

    private readonly ProcessRunnerService _processRunner;
    private string? _mountedEspPath;

    public BootManagerService(ProcessRunnerService processRunner)
    {
        _processRunner = processRunner;
    }

    public bool IsDryRun => false;

    private static string StateDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "LinuxInstallerForWindows");

    private static string BootEntryStatePath =>
        Path.Combine(StateDirectory, "boot-entry.txt");

    public async Task<string> MountEspAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("ESP mounting is only supported on Windows.");
        }

        if (_mountedEspPath != null)
        {
            return _mountedEspPath;
        }

        var usedRoots = DriveInfo.GetDrives()
            .Select(drive => drive.Name.TrimEnd('\\'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var driveRoot = Enumerable.Range('D', 'Z' - 'D' + 1)
            .Reverse()
            .Select(value => $"{(char)value}:")
            .FirstOrDefault(candidate => !usedRoots.Contains(candidate))
            ?? throw new InvalidOperationException("No free drive letter is available for the EFI System Partition.");

        var result = await _processRunner.RunAsync(
            "mountvol.exe",
            [driveRoot, "/S"],
            cancellationToken);
        result.EnsureSuccess("Mounting the EFI System Partition");

        _mountedEspPath = driveRoot + Path.DirectorySeparatorChar;
        return _mountedEspPath;
    }

    public async Task UnmountEspAsync(CancellationToken cancellationToken = default)
    {
        if (_mountedEspPath == null)
        {
            return;
        }

        var driveRoot = _mountedEspPath.TrimEnd('\\');
        try
        {
            var result = await _processRunner.RunAsync(
                "mountvol.exe",
                [driveRoot, "/D"],
                cancellationToken);
            result.EnsureSuccess("Unmounting the EFI System Partition");
        }
        finally
        {
            _mountedEspPath = null;
        }
    }

    public async Task<string> CreateBcdEntryAsync(
        string espPath,
        string efiRelativePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(espPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(efiRelativePath);

        var driveRoot = Path.GetPathRoot(espPath)?.TrimEnd('\\')
            ?? throw new ArgumentException("The ESP path must include a drive root.", nameof(espPath));
        var normalizedPath = "\\" + efiRelativePath
            .Replace('/', '\\')
            .TrimStart('\\');

        var createResult = await _processRunner.RunAsync(
            "bcdedit.exe",
            ["/create", "/d", BootEntryDescription, "/application", "BOOTAPP"],
            cancellationToken);
        createResult.EnsureSuccess("Creating the GRUB firmware boot entry");

        var identifier = EntryIdentifierPattern.Match(
            createResult.StandardOutput + Environment.NewLine + createResult.StandardError).Value;
        if (string.IsNullOrWhiteSpace(identifier))
        {
            throw new InvalidOperationException(
                "BCD created a boot entry but did not return its identifier.");
        }

        try
        {
            await RunBcdEditAsync(
                ["/set", identifier, "device", $"partition={driveRoot}"],
                "Setting the GRUB boot device",
                cancellationToken);
            await RunBcdEditAsync(
                ["/set", identifier, "path", normalizedPath],
                "Setting the GRUB EFI path",
                cancellationToken);
            await RunBcdEditAsync(
                ["/set", "{fwbootmgr}", "displayorder", identifier, "/addfirst"],
                "Prioritizing GRUB in firmware boot order",
                cancellationToken);
            await RunBcdEditAsync(
                ["/bootsequence", identifier],
                "Selecting GRUB for the next boot",
                cancellationToken);

            Directory.CreateDirectory(StateDirectory);
            await File.WriteAllTextAsync(
                BootEntryStatePath,
                identifier,
                cancellationToken);
            return identifier;
        }
        catch
        {
            await _processRunner.RunAsync(
                "bcdedit.exe",
                ["/delete", identifier, "/cleanup"],
                CancellationToken.None);
            throw;
        }
    }

    public async Task RemoveBcdEntryAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(BootEntryStatePath))
        {
            return;
        }

        var identifier = (await File.ReadAllTextAsync(
            BootEntryStatePath,
            cancellationToken)).Trim();
        if (!EntryIdentifierPattern.IsMatch(identifier))
        {
            throw new InvalidDataException("The stored BCD entry identifier is invalid.");
        }

        var result = await _processRunner.RunAsync(
            "bcdedit.exe",
            ["/delete", identifier, "/cleanup"],
            cancellationToken);
        result.EnsureSuccess("Removing the GRUB firmware boot entry");
        File.Delete(BootEntryStatePath);
    }

    private async Task RunBcdEditAsync(
        IReadOnlyList<string> arguments,
        string operation,
        CancellationToken cancellationToken)
    {
        var result = await _processRunner.RunAsync(
            "bcdedit.exe",
            arguments,
            cancellationToken);
        result.EnsureSuccess(operation);
    }
}
