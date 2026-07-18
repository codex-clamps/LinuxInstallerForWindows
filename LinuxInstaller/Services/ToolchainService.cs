using LinuxInstaller.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LinuxInstaller.Services;

public sealed class ToolchainService
{
    public const string GrubArtifactId = "grub-uefi-x86_64";
    public const string InstallerKernelArtifactId = "installer-kernel-x86_64";
    public const string InstallerInitramfsArtifactId = "installer-initramfs-x86_64";
    public const string FilesystemToolsArtifactId = "filesystem-tools-x86_64";
    public const string WinBtrfsSignedArtifactId = "winbtrfs-x64-signed";
    public const string Ext4FsdSignedArtifactId = "ext4fsd-signed-installer";

    private const string ManifestUrl =
        "https://github.com/codex-clamps/LinuxInstallerRootfs/releases/download/toolchains-latest/toolchains-manifest.json";

    private static readonly string[] RequiredArtifactIds =
    [
        GrubArtifactId,
        InstallerKernelArtifactId,
        InstallerInitramfsArtifactId,
        FilesystemToolsArtifactId,
        WinBtrfsSignedArtifactId,
        Ext4FsdSignedArtifactId
    ];

    private static readonly HttpClient HttpClient = CreateHttpClient();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static string ApplicationDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LinuxInstallerForWindows");

    private static string CacheDirectory => Path.Combine(ApplicationDirectory, "cache", "toolchains");
    private static string SessionRootDirectory => Path.Combine(ApplicationDirectory, "sessions");
    private static string ManifestCachePath => Path.Combine(CacheDirectory, "toolchains-manifest.json");

    public async Task<ToolchainSession> PrepareSessionAsync(
        IProgress<ToolchainProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report(new ToolchainProgress("Loading installer toolchain manifest...", 0));
        var manifestJson = await GetManifestJsonAsync(cancellationToken);
        var manifest = JsonSerializer.Deserialize<ToolchainManifest>(manifestJson, JsonOptions)
            ?? throw new InvalidDataException("The toolchain manifest is empty.");
        ValidateManifest(manifest);

        var selectedArtifacts = manifest.Artifacts
            .Where(artifact => artifact.RequiredForInstall)
            .ToList();
        var sessionId = System.Guid.NewGuid().ToString("N");
        var sessionDirectory = Path.Combine(SessionRootDirectory, sessionId);
        var expandedRoot = Path.Combine(sessionDirectory, "expanded");
        Directory.CreateDirectory(sessionDirectory);
        Directory.CreateDirectory(expandedRoot);
        Directory.CreateDirectory(CacheDirectory);

        var artifactPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var expandedDirectories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < selectedArtifacts.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var artifact = selectedArtifacts[index];
            var startPercentage = selectedArtifacts.Count == 0
                ? 0
                : index * 100d / selectedArtifacts.Count;
            progress?.Report(new ToolchainProgress(
                $"Downloading {artifact.DisplayName}...",
                startPercentage));

            var cachedPath = await GetOrDownloadArtifactAsync(
                artifact,
                new Progress<double>(value =>
                {
                    var percentage = (index + value / 100d) * 100d /
                        selectedArtifacts.Count;
                    progress?.Report(new ToolchainProgress(
                        $"Downloading {artifact.DisplayName}... {value:0}%",
                        percentage));
                }),
                cancellationToken);

            var sessionPath = Path.Combine(sessionDirectory, artifact.FileName);
            File.Copy(cachedPath, sessionPath, overwrite: true);
            artifactPaths.Add(artifact.Id, sessionPath);

            if (string.Equals(Path.GetExtension(sessionPath), ".zip", StringComparison.OrdinalIgnoreCase))
            {
                var expandedDirectory = Path.Combine(expandedRoot, artifact.Id);
                Directory.CreateDirectory(expandedDirectory);
                ZipFile.ExtractToDirectory(sessionPath, expandedDirectory, overwriteFiles: true);
                expandedDirectories.Add(artifact.Id, expandedDirectory);
            }
        }

        var pathDirectories = expandedDirectories.Values
            .SelectMany(directory => Directory.EnumerateDirectories(
                directory,
                "bin",
                SearchOption.AllDirectories).Prepend(directory))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        Environment.SetEnvironmentVariable(
            "PATH",
            string.Join(Path.PathSeparator, pathDirectories.Append(currentPath)));
        Environment.SetEnvironmentVariable(
            "LINUX_INSTALLER_TOOLCHAIN_SESSION",
            sessionDirectory);

        progress?.Report(new ToolchainProgress("Installer toolchain session is ready.", 100));
        return new ToolchainSession
        {
            SessionId = sessionId,
            DirectoryPath = sessionDirectory,
            ArtifactPaths = artifactPaths,
            ExpandedDirectories = expandedDirectories
        };
    }

    private static async Task<string> GetOrDownloadArtifactAsync(
        ToolchainArtifact artifact,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        ValidateArtifact(artifact);
        var destinationPath = Path.Combine(CacheDirectory, artifact.FileName);
        if (File.Exists(destinationPath) &&
            await HasExpectedChecksumAsync(destinationPath, artifact.Sha256, cancellationToken))
        {
            progress?.Report(100);
            return destinationPath;
        }

        if (File.Exists(destinationPath))
        {
            File.Delete(destinationPath);
        }

        var temporaryPath = destinationPath + ".download";
        if (File.Exists(temporaryPath))
        {
            File.Delete(temporaryPath);
        }

        try
        {
            using var response = await HttpClient.GetAsync(
                artifact.DownloadUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength;
            if (!totalBytes.HasValue && artifact.Size <= (ulong)long.MaxValue)
            {
                totalBytes = (long)artifact.Size;
            }

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using (var destination = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[128 * 1024];
                long downloadedBytes = 0;
                int bytesRead;
                while ((bytesRead = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                    downloadedBytes += bytesRead;
                    if (totalBytes > 0)
                    {
                        progress?.Report(downloadedBytes * 100d / totalBytes.Value);
                    }
                }

                await destination.FlushAsync(cancellationToken);
            }

            if (!await HasExpectedChecksumAsync(temporaryPath, artifact.Sha256, cancellationToken))
            {
                throw new InvalidDataException(
                    $"The downloaded artifact '{artifact.FileName}' failed SHA-256 verification.");
            }

            File.Move(temporaryPath, destinationPath, overwrite: true);
            progress?.Report(100);
            return destinationPath;
        }
        catch
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            throw;
        }
    }

    private static async Task<string> GetManifestJsonAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await HttpClient.GetAsync(ManifestUrl, cancellationToken);
            response.EnsureSuccessStatusCode();
            var manifestJson = await response.Content.ReadAsStringAsync(cancellationToken);
            Directory.CreateDirectory(CacheDirectory);
            await File.WriteAllTextAsync(ManifestCachePath, manifestJson, cancellationToken);
            return manifestJson;
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException && File.Exists(ManifestCachePath))
        {
            return await File.ReadAllTextAsync(ManifestCachePath, cancellationToken);
        }
    }

    private static void ValidateManifest(ToolchainManifest manifest)
    {
        if (manifest.SchemaVersion != 1 || manifest.Artifacts.Count == 0)
        {
            throw new InvalidDataException("The toolchain manifest has an unsupported format.");
        }

        var requiredIds = manifest.Artifacts
            .Where(artifact => artifact.RequiredForInstall)
            .Select(artifact => artifact.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = RequiredArtifactIds.Where(id => !requiredIds.Contains(id)).ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidDataException(
                $"The toolchain release is missing required artifacts: {string.Join(", ", missing)}.");
        }
    }

    private static void ValidateArtifact(ToolchainArtifact artifact)
    {
        if (string.IsNullOrWhiteSpace(artifact.FileName) ||
            Path.GetFileName(artifact.FileName) != artifact.FileName ||
            artifact.FileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidDataException("A toolchain artifact has an invalid file name.");
        }

        if (artifact.Sha256.Length != 64 || !artifact.Sha256.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException(
                $"Artifact '{artifact.FileName}' has an invalid SHA-256 checksum.");
        }

        if (!Uri.TryCreate(artifact.DownloadUrl, UriKind.Absolute, out var downloadUri) ||
            downloadUri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(downloadUri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
            !downloadUri.AbsolutePath.StartsWith(
                "/codex-clamps/LinuxInstallerRootfs/releases/download/toolchains-latest/",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Artifact '{artifact.FileName}' has an untrusted download URL.");
        }
    }

    private static async Task<bool> HasExpectedChecksumAsync(
        string filePath,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return string.Equals(
            Convert.ToHexString(hash),
            expectedSha256,
            StringComparison.OrdinalIgnoreCase);
    }

    private static HttpClient CreateHttpClient()
    {
        var httpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("LinuxInstallerForWindows/2.0");
        return httpClient;
    }
}
