using System;
using System.Collections.Generic;
using System.IO;

namespace LinuxInstaller.Models;

public sealed class ToolchainSession
{
    public required string SessionId { get; init; }
    public required string DirectoryPath { get; init; }
    public required IReadOnlyDictionary<string, string> ArtifactPaths { get; init; }
    public required IReadOnlyDictionary<string, string> ExpandedDirectories { get; init; }

    public string GetArtifactPath(string artifactId)
    {
        if (!ArtifactPaths.TryGetValue(artifactId, out var path) || !File.Exists(path))
        {
            throw new InvalidOperationException(
                $"Required toolchain artifact '{artifactId}' is not available in this session.");
        }

        return path;
    }
}
