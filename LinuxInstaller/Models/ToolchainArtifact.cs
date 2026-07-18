using System.Collections.Generic;

namespace LinuxInstaller.Models;

public sealed class ToolchainManifest
{
    public int SchemaVersion { get; init; }
    public string GeneratedAt { get; init; } = string.Empty;
    public List<ToolchainArtifact> Artifacts { get; init; } = [];
}

public sealed class ToolchainArtifact
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string Platform { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public ulong Size { get; init; }
    public string Sha256 { get; init; } = string.Empty;
    public string DownloadUrl { get; init; } = string.Empty;
    public string SourceUrl { get; init; } = string.Empty;
    public string License { get; init; } = string.Empty;
    public string Notes { get; init; } = string.Empty;
    public bool RequiredForInstall { get; init; }
    public bool Signed { get; init; }
}

public sealed record ToolchainProgress(string Status, double Percentage);
