param(
    [string]$Configuration = "Debug",
    [string]$OutputPath = "C:\tmp\LinuxInstaller-storage-smoke.json"
)

$ErrorActionPreference = "Stop"

try {
    if ($PSVersionTable.PSVersion.Major -lt 7) {
        throw "This smoke test requires PowerShell 7 or later. Run it with pwsh.exe."
    }

    $repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
    $binaryDirectory = Join-Path $repositoryRoot "LinuxInstaller\bin\$Configuration\net9.0-windows10.0.19041.0"

    Get-ChildItem -LiteralPath $binaryDirectory -Filter "*.dll" | ForEach-Object {
        try {
            [System.Runtime.Loader.AssemblyLoadContext]::Default.LoadFromAssemblyPath($_.FullName) | Out-Null
        }
        catch {
            # Dependencies already loaded into the default context are safe to ignore.
        }
    }

    $assembly = [System.Runtime.Loader.AssemblyLoadContext]::Default.Assemblies |
        Where-Object { $_.GetName().Name -eq "LinuxInstaller" } |
        Select-Object -First 1
    $managerType = $assembly.GetType("LinuxInstaller.Services.WindowsStorageManager", $true)
    $manager = [Activator]::CreateInstance($managerType)
    $task = $managerType.GetMethod("GetDisksAsync").Invoke(
        $manager,
        @([Threading.CancellationToken]::None))
    $disks = $task.GetAwaiter().GetResult()
    $partitions = @($disks | ForEach-Object Partitions)

    $secondTask = $managerType.GetMethod("GetDisksAsync").Invoke(
        $manager,
        @([Threading.CancellationToken]::None))
    $secondDisks = $secondTask.GetAwaiter().GetResult()

    if ($disks.Count -eq 0) {
        throw "No physical disks were discovered."
    }

    if ($partitions.Count -eq 0) {
        throw "No partitions were discovered."
    }

    if (@($disks | Where-Object { [string]::IsNullOrWhiteSpace($_.Id) }).Count -ne 0) {
        throw "A discovered disk is missing a stable identity."
    }

    if (@($partitions | Where-Object { [string]::IsNullOrWhiteSpace($_.Id) }).Count -ne 0) {
        throw "A discovered partition is missing a stable identity."
    }

    if (@($disks | Group-Object Id | Where-Object Count -gt 1).Count -ne 0) {
        throw "Duplicate disk identities were discovered."
    }

    if (@($partitions | Group-Object Id | Where-Object Count -gt 1).Count -ne 0) {
        throw "Duplicate partition identities were discovered."
    }

    $firstTopology = @($disks | ForEach-Object {
        $disk = $_
        $partitionSignature = @($disk.Partitions |
            Sort-Object Id |
            ForEach-Object { "$($_.Id):$($_.StartOffset):$($_.Size)" }) -join ","
        "$($disk.Id):$($disk.Size):$partitionSignature"
    } | Sort-Object)
    $secondTopology = @($secondDisks | ForEach-Object {
        $disk = $_
        $partitionSignature = @($disk.Partitions |
            Sort-Object Id |
            ForEach-Object { "$($_.Id):$($_.StartOffset):$($_.Size)" }) -join ","
        "$($disk.Id):$($disk.Size):$partitionSignature"
    } | Sort-Object)
    if (Compare-Object -ReferenceObject $firstTopology -DifferenceObject $secondTopology) {
        throw "Storage identities or topology changed between consecutive read-only enumerations."
    }

    if (@($partitions | Where-Object { -not $_.IsExisting -or -not $_.IsProtected }).Count -ne 0) {
        throw "A discovered partition is not protected from editing."
    }

    $mappedVolumes = @($partitions | Where-Object {
        $_.FileSystem.ToString() -eq "NTFS" -and $_.VolumeSizeRemaining -ne $null
    }).Count
    if ($mappedVolumes -eq 0) {
        throw "No NTFS volume mapping was discovered."
    }

    $resizeRanges = @($partitions | Where-Object {
        $_.FileSystem.ToString() -eq "NTFS" -and $_.SupportedSizeMinimum -ne $null
    }).Count
    if ($resizeRanges -eq 0) {
        throw "No supported NTFS resize range was returned under elevation."
    }

    $result = [ordered]@{
        success = $true
        disks = $disks.Count
        partitions = $partitions.Count
        eligibleDisks = @($disks | Where-Object IsEligibleForInstallation).Count
        mappedVolumes = $mappedVolumes
        resizeRanges = $resizeRanges
        busTypes = @($disks | Select-Object -ExpandProperty BusType -Unique)
    }
}
catch {
    $result = [ordered]@{
        success = $false
        errorType = $_.Exception.GetType().FullName
        error = $_.Exception.Message
    }
}

$outputDirectory = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$result | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $OutputPath -Encoding UTF8

if (-not $result.success) {
    exit 1
}
