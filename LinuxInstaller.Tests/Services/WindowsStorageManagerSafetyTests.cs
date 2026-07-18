using LinuxInstaller.Services;
using System.Management;
using System.Reflection;

namespace LinuxInstaller.Tests.Services;

public class WindowsStorageManagerSafetyTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(17)]
    [InlineData(18)]
    [InlineData(19)]
    public void GetIneligibilityReason_AllowsOnlyKnownInternalBusTypes(int busType)
    {
        var reason = GetIneligibilityReason(busType: (ushort)busType);

        Assert.Empty(reason);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(20)]
    [InlineData(ushort.MaxValue)]
    public void GetIneligibilityReason_RejectsUnsupportedAndFutureBusTypes(int busType)
    {
        var reason = GetIneligibilityReason(busType: (ushort)busType);

        Assert.NotEmpty(reason);
    }

    [Fact]
    public void GetIneligibilityReason_RejectsMissingBusType()
    {
        var reason = GetIneligibilityReason(busType: null);

        Assert.NotEmpty(reason);
    }

    [Fact]
    public void GetIneligibilityReason_RejectsUnknownWritableAndClusterState()
    {
        Assert.NotEmpty(GetIneligibilityReason(isReadOnly: null));
        Assert.NotEmpty(GetIneligibilityReason(isOffline: null));
        Assert.NotEmpty(GetIneligibilityReason(isClustered: null));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    public void GetIneligibilityReason_RejectsNonHealthyOrUnknownHealth(int healthStatus)
    {
        var reason = GetIneligibilityReason(healthStatus: (ushort)healthStatus);

        Assert.NotEmpty(reason);
    }

    [Fact]
    public void IsAccessDenied_RecognizesManagementExceptionWithUnauthorizedInnerException()
    {
        var exception = new ManagementException(
            "Access denied.",
            new UnauthorizedAccessException());

        var result = InvokePrivateStatic("IsAccessDenied", exception);

        Assert.True(Assert.IsType<bool>(result));
    }

    [Fact]
    public void CreateDiskId_PrefersPhysicalUniqueIdOverGptGuid()
    {
        var id = Assert.IsType<string>(
            InvokePrivateStatic(
                "CreateDiskId",
                "{11111111-1111-1111-1111-111111111111}",
                " physical-id ",
                (ushort?)8,
                string.Empty,
                string.Empty,
                (uint)0));
        var clonedGuidDiskId = Assert.IsType<string>(
            InvokePrivateStatic(
                "CreateDiskId",
                "{11111111-1111-1111-1111-111111111111}",
                "another-physical-id",
                (ushort?)8,
                string.Empty,
                string.Empty,
                (uint)1));

        Assert.Equal("unique:8:PHYSICAL-ID", id);
        Assert.NotEqual(id, clonedGuidDiskId);
    }

    [Fact]
    public void GetIneligibilityReason_RejectsTransientFallbackIdentity()
    {
        var reason = GetIneligibilityReason(hasDurableIdentity: false);

        Assert.Contains("durable physical identity", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("", "", false)]
    [InlineData("", "physical-id", true)]
    [InlineData("{11111111-1111-1111-1111-111111111111}", "", true)]
    public void HasDurableDiskIdentity_RequiresUniqueIdOrGptGuid(
        string guid,
        string uniqueId,
        bool expected)
    {
        var result = InvokePrivateStatic("HasDurableDiskIdentity", guid, uniqueId);

        Assert.Equal(expected, Assert.IsType<bool>(result));
    }

    [Fact]
    public void CreatePartitionId_ScopesProviderIdentityToDisk()
    {
        const string partitionGuid = "{22222222-2222-2222-2222-222222222222}";

        var first = Assert.IsType<string>(
            InvokePrivateStatic(
                "CreatePartitionId",
                partitionGuid,
                string.Empty,
                string.Empty,
                "disk-a",
                (uint)1,
                (ulong)1048576));
        var second = Assert.IsType<string>(
            InvokePrivateStatic(
                "CreatePartitionId",
                partitionGuid,
                string.Empty,
                string.Empty,
                "disk-b",
                (uint)1,
                (ulong)1048576));

        Assert.StartsWith("disk-a:", first, StringComparison.Ordinal);
        Assert.StartsWith("disk-b:", second, StringComparison.Ordinal);
        Assert.NotEqual(first, second);

        var uniqueIdOnFirstDisk = Assert.IsType<string>(
            InvokePrivateStatic(
                "CreatePartitionId",
                string.Empty,
                "same-partition-id",
                string.Empty,
                "disk-a",
                (uint)1,
                (ulong)1048576));
        var uniqueIdOnSecondDisk = Assert.IsType<string>(
            InvokePrivateStatic(
                "CreatePartitionId",
                string.Empty,
                "same-partition-id",
                string.Empty,
                "disk-b",
                (uint)1,
                (ulong)1048576));

        Assert.NotEqual(uniqueIdOnFirstDisk, uniqueIdOnSecondDisk);
    }

    private static string GetIneligibilityReason(
        ushort? partitionStyle = 2,
        ushort? busType = 17,
        bool? isReadOnly = false,
        bool? isOffline = false,
        bool? isClustered = false,
        ushort? healthStatus = 0,
        bool hasDurableIdentity = true) =>
        Assert.IsType<string>(
            InvokePrivateStatic(
                "GetIneligibilityReason",
                partitionStyle,
                busType,
                isReadOnly,
                isOffline,
                isClustered,
                healthStatus,
                hasDurableIdentity));

    private static object? InvokePrivateStatic(string methodName, params object?[] arguments)
    {
        var method = typeof(WindowsStorageManager).GetMethod(
            methodName,
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        return method.Invoke(null, arguments);
    }
}
