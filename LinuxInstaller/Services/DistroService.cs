using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LinuxInstaller.Models;

namespace LinuxInstaller.Services;

public sealed class DistroService
{
    private const string ManifestUrl =
        "https://github.com/codex-clamps/LinuxInstallerRootfs/releases/download/rootfs-latest/manifest.json";

    private static readonly HttpClient HttpClient = CreateHttpClient();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static string CacheDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LinuxInstallerForWindows",
        "cache",
        "rootfs");

    private static string ManifestCachePath => Path.Combine(CacheDirectory, "manifest.json");

    public async Task<IEnumerable<Distro>> GetDistros(CancellationToken cancellationToken = default)
    {
        var manifestJson = await GetManifestJsonAsync(cancellationToken);
        var manifest = JsonSerializer.Deserialize<RootfsManifest>(manifestJson, JsonOptions)
            ?? throw new InvalidDataException("The rootfs manifest is empty.");

        if (manifest.SchemaVersion != 1 || manifest.Distros.Count == 0)
        {
            throw new InvalidDataException("The rootfs manifest has an unsupported format.");
        }

        return manifest.Distros.Select(entry => new Distro
        {
            RootfsId = entry.Id,
            DistroName = entry.DistroName,
            Version = entry.Version,
            Description = entry.Description,
            Size = entry.Size,
            DownloadUrl = entry.DownloadUrl,
            IconUrl = entry.IconUrl,
            RootfsFileName = entry.FileName,
            RootfsArchitecture = manifest.Architecture,
            RootfsSha256 = entry.Sha256
        }).ToArray();
    }

    public async Task<string> DownloadRootfsAsync(
        Distro distro,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(distro);

        if (!Uri.TryCreate(distro.DownloadUrl, UriKind.Absolute, out var downloadUri) ||
            downloadUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidDataException("The selected distribution has an invalid download URL.");
        }

        ValidateArtifactMetadata(distro);
        Directory.CreateDirectory(CacheDirectory);

        var destinationPath = Path.Combine(CacheDirectory, distro.RootfsFileName);
        if (File.Exists(destinationPath) &&
            await HasExpectedChecksumAsync(destinationPath, distro.RootfsSha256, cancellationToken))
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
                downloadUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength;
            if (!totalBytes.HasValue && distro.Size <= (ulong)long.MaxValue)
            {
                totalBytes = (long)distro.Size;
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

            if (!await HasExpectedChecksumAsync(temporaryPath, distro.RootfsSha256, cancellationToken))
            {
                throw new InvalidDataException("The downloaded rootfs failed SHA-256 verification.");
            }

            File.Move(temporaryPath, destinationPath, true);
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

            try
            {
                Directory.CreateDirectory(CacheDirectory);
                await File.WriteAllTextAsync(ManifestCachePath, manifestJson, cancellationToken);
            }
            catch (IOException)
            {
                // The remote manifest is still usable when the local cache cannot be updated.
            }
            catch (UnauthorizedAccessException)
            {
                // The remote manifest is still usable when the local cache cannot be updated.
            }

            return manifestJson;
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException && File.Exists(ManifestCachePath))
        {
            return await File.ReadAllTextAsync(ManifestCachePath, cancellationToken);
        }
    }

    private static void ValidateArtifactMetadata(Distro distro)
    {
        if (string.IsNullOrWhiteSpace(distro.RootfsFileName) ||
            Path.GetFileName(distro.RootfsFileName) != distro.RootfsFileName ||
            distro.RootfsFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidDataException("The selected distribution has an invalid rootfs file name.");
        }

        if (distro.RootfsSha256.Length != 64 || !distro.RootfsSha256.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException("The selected distribution has an invalid SHA-256 checksum.");
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
        return string.Equals(Convert.ToHexString(hash), expectedSha256, StringComparison.OrdinalIgnoreCase);
    }

    private static HttpClient CreateHttpClient()
    {
        var httpClient = new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("LinuxInstallerForWindows/1.0");
        return httpClient;
    }

    private sealed class RootfsManifest
    {
        public int SchemaVersion { get; init; }
        public string Architecture { get; init; } = string.Empty;
        public List<RootfsManifestEntry> Distros { get; init; } = [];
    }

    private sealed class RootfsManifestEntry
    {
        public string Id { get; init; } = string.Empty;
        public string DistroName { get; init; } = string.Empty;
        public string Version { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public ulong Size { get; init; }
        public string DownloadUrl { get; init; } = string.Empty;
        public string IconUrl { get; init; } = string.Empty;
        public string FileName { get; init; } = string.Empty;
        public string Sha256 { get; init; } = string.Empty;
    }
}
