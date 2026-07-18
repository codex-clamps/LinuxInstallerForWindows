using LinuxInstaller.Models;
using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LinuxInstaller.Services;

public sealed class ConfigGeneratorService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public string GenerateGrubStage2Config(Distro distro, System.Guid installationId)
    {
        ArgumentNullException.ThrowIfNull(distro);
        var title = EscapeGrubString($"Install {distro.DistroName} {distro.Version}".Trim());
        var lines = new[]
        {
            "set default=0",
            "set timeout=3",
            string.Empty,
            $"menuentry \"{title}\" {{",
            "    search --no-floppy --file --set=installer_root /.myinstaller/install.json",
            "    linux ($installer_root)/.myinstaller/installer.vmlinuz lifw.mode=install lifw.config=/.myinstaller/install.json " +
                $"lifw.installation={installationId:N}",
            "    initrd ($installer_root)/.myinstaller/installer.initrd",
            "}",
            string.Empty,
            "menuentry \"Windows Boot Manager\" {",
            "    search --no-floppy --file --set=windows_esp /EFI/Microsoft/Boot/bootmgfw.efi",
            "    chainloader ($windows_esp)/EFI/Microsoft/Boot/bootmgfw.efi",
            "}"
        };
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    public string GenerateInstallConfiguration(
        System.Guid installationId,
        Distro distro,
        PartitionPlan plan,
        UserInfo userInfo)
    {
        ArgumentNullException.ThrowIfNull(distro);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(userInfo);

        if (!plan.IsValid ||
            plan.TargetDisk is not { } targetDisk ||
            plan.PartitionHistory.Count == 0)
        {
            throw new InvalidOperationException("The partition plan is not valid.");
        }

        if (!TryNormalizeGuid(targetDisk.UniqueId, out var targetDiskGuid) &&
            !TryNormalizeGuid(targetDisk.Id, out targetDiskGuid))
        {
            throw new InvalidOperationException(
                "The selected disk does not expose a valid GPT disk identifier.");
        }

        ValidateUserInfo(userInfo);
        var usedNumbers = targetDisk.Partitions
            .Select(partition => partition.Number)
            .Where(number => number > 0)
            .ToHashSet();
        var nextNumber = 1u;

        uint GetNextNumber()
        {
            while (usedNumbers.Contains(nextNumber))
            {
                nextNumber++;
            }

            usedNumbers.Add(nextNumber);
            return nextNumber++;
        }

        var partitions = plan.PartitionHistory.Last()
            .Where(partition =>
                !partition.IsExisting ||
                !string.IsNullOrWhiteSpace(partition.MountPoint))
            .OrderBy(partition => partition.StartOffset)
            .Select(partition =>
            {
                if (!TryNormalizeGuid(partition.Guid, out var partitionGuid))
                {
                    partition.Guid = Partition.CreatePartitionGuid();
                    partitionGuid = partition.Guid;
                }

                var number = partition.IsExisting
                    ? partition.Number
                    : partition.Number > 0 && usedNumbers.Add(partition.Number)
                        ? partition.Number
                        : GetNextNumber();

                return new
                {
                    number,
                    guid = partitionGuid,
                    name = string.IsNullOrWhiteSpace(partition.Name)
                        ? $"Linux partition {number}"
                        : partition.Name,
                    startOffsetBytes = partition.StartOffset,
                    sizeBytes = partition.Size,
                    fileSystem = FS.ToLinuxName(partition.FileSystem),
                    mountPoint = partition.MountPoint,
                    isExisting = partition.IsExisting
                };
            })
            .ToArray();

        var hostname = CreateHostname(distro.RootfsId, userInfo.Username);
        var configuration = new
        {
            schemaVersion = 1,
            installationId = installationId.ToString("N"),
            distroId = distro.RootfsId,
            distroName = $"{distro.DistroName} {distro.Version}".Trim(),
            hostname,
            targetDiskGuid,
            rootfsFileName = "rootfs.tar.zst",
            filesystemToolchainFileName = "filesystem-tools.tar.zst",
            partitions,
            user = new
            {
                username = userInfo.Username,
                fullName = string.IsNullOrWhiteSpace(userInfo.FullName)
                    ? userInfo.Username
                    : userInfo.FullName,
                passwordBase64 = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes(userInfo.Password)),
                autoLogin = userInfo.AutoLogin
            }
        };

        return JsonSerializer.Serialize(configuration, JsonOptions);
    }

    private static void ValidateUserInfo(UserInfo userInfo)
    {
        if (!Regex.IsMatch(userInfo.Username, "^[a-z_][a-z0-9_-]{0,31}$"))
        {
            throw new InvalidOperationException(
                "The Linux username must use lowercase letters, digits, underscore, or hyphen.");
        }

        if (string.IsNullOrEmpty(userInfo.Password) ||
            userInfo.Password.Contains('\r') ||
            userInfo.Password.Contains('\n'))
        {
            throw new InvalidOperationException(
                "The Linux password cannot be empty or contain line breaks.");
        }
    }

    private static string CreateHostname(string distroId, string username)
    {
        var hostname = Regex.Replace(
            $"{distroId}-{username}".ToLowerInvariant(),
            "[^a-z0-9-]",
            "-").Trim('-');
        if (hostname.Length == 0)
        {
            hostname = "linux";
        }

        return hostname.Length <= 63 ? hostname : hostname[..63].TrimEnd('-');
    }

    private static bool TryNormalizeGuid(string value, out string normalized)
    {
        if (System.Guid.TryParse(value, out var guid))
        {
            normalized = guid.ToString("D").ToUpperInvariant();
            return true;
        }

        normalized = string.Empty;
        return false;
    }

    private static string EscapeGrubString(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
    }
}
