using LinuxInstaller.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LinuxInstaller.Services;

public sealed class DiskpartService
{
    private static readonly Regex DriveLetterPattern = new(
        "^[A-Za-z]:?$",
        RegexOptions.Compiled);

    private readonly ProcessRunnerService _processRunner;
    private readonly IStorageManager _storageManager;

    public DiskpartService(
        ProcessRunnerService processRunner,
        IStorageManager storageManager)
    {
        _processRunner = processRunner;
        _storageManager = storageManager;
    }

    public bool IsDryRun => false;

    public async Task<bool> ShrinkPartitionAsync(
        string driveLetter,
        int sizeInMb,
        CancellationToken cancellationToken = default)
    {
        if (!DriveLetterPattern.IsMatch(driveLetter) || sizeInMb <= 0)
        {
            throw new ArgumentException("A valid drive letter and positive shrink size are required.");
        }

        var normalizedDriveLetter = driveLetter.TrimEnd(':').ToUpperInvariant();
        var script = $"""
            select volume {normalizedDriveLetter}
            shrink desired={sizeInMb} minimum={sizeInMb}
            exit
            """;
        var result = await ExecuteScriptAsync(script, cancellationToken);
        return result.ExitCode == 0;
    }

    public Task<IReadOnlyList<Disk>> ListDisksAsync(
        CancellationToken cancellationToken = default)
    {
        return _storageManager.GetDisksAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ListVolumesAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await ExecuteScriptAsync("list volume\r\nexit\r\n", cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(result.StandardError);
        }

        return result.StandardOutput
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
    }

    public async Task<(int ExitCode, string StandardOutput, string StandardError)> ExecuteScriptAsync(
        string scriptContent,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptContent);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("DiskPart is only available on Windows.");
        }

        var scriptPath = Path.Combine(
            Path.GetTempPath(),
            $"linux-installer-{System.Guid.NewGuid():N}.diskpart");
        try
        {
            await File.WriteAllTextAsync(
                scriptPath,
                scriptContent,
                Encoding.ASCII,
                cancellationToken);
            var result = await _processRunner.RunAsync(
                "diskpart.exe",
                ["/s", scriptPath],
                cancellationToken);
            return (result.ExitCode, result.StandardOutput, result.StandardError);
        }
        finally
        {
            if (File.Exists(scriptPath))
            {
                File.Delete(scriptPath);
            }
        }
    }
}
