using LinuxInstaller.ViewModels;

namespace LinuxInstaller.Tests.ViewModels;

public class PartitionDialogViewModelTests
{
    private const ulong MiB = 1024UL * 1024UL;

    [Theory]
    [InlineData(1.9, "MB", 1)]
    [InlineData(0.5, "GB", 512)]
    [InlineData(0.5, "MB", 0)]
    public void ConvertToAlignedBytes_AlwaysReturnsWholeMiBBoundaries(
        double value,
        string unit,
        ulong expectedMiB)
    {
        var bytes = PartitionDialogViewModel.ConvertToAlignedBytes((decimal)value, unit);

        Assert.Equal(expectedMiB * MiB, bytes);
        Assert.Equal(0UL, bytes % MiB);
    }

    [Fact]
    public void ConvertToAlignedBytes_RejectsNegativeValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PartitionDialogViewModel.ConvertToAlignedBytes(-1, "MB"));
    }
}
