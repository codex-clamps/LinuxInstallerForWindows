using LinuxInstaller.Models;
using LinuxInstaller.Services;
using LinuxInstaller.ViewModels;

namespace LinuxInstaller.Tests.ViewModels;

public class InstallationSummaryViewModelTests
{
    [Fact]
    public void IsoSummaryWithoutPartitionTarget_IsSafeToRender()
    {
        var config = new InstallationConfigService
        {
            SelectedInstallWorkflow = InstallWorkflowType.Iso,
            SelectedIsoPath = "C:\\images\\linux.iso"
        };
        var viewModel = new InstallationSummaryViewModel(
            new NavigationService(),
            config);

        var items = viewModel.SummaryItems;
        var partitionSummary = viewModel.PartitionSummaryContent;

        var isoItem = Assert.Single(items);
        Assert.Equal("Selected ISO Image", isoItem.Title);
        Assert.Empty(partitionSummary);
        Assert.True(viewModel.CanProceed);
    }

    [Fact]
    public void EmptyPartitionHistory_DoesNotDereferenceMissingTarget()
    {
        var config = new InstallationConfigService();
        config.PartitionPlan.PartitionHistory = [];
        var viewModel = new InstallationSummaryViewModel(
            new NavigationService(),
            config);

        var partitionSummary = viewModel.PartitionSummaryContent;

        Assert.Empty(partitionSummary);
    }

    [Fact]
    public void MissingWorkflowInputs_AreRenderedDefensivelyAndBlocked()
    {
        var config = new InstallationConfigService
        {
            SelectedInstallWorkflow = InstallWorkflowType.Distro
        };
        var viewModel = new InstallationSummaryViewModel(
            new NavigationService(),
            config);

        var items = viewModel.SummaryItems;

        var distroItem = Assert.Single(items, item => item.Title == "Distro");
        Assert.Contains(distroItem.Content, pair =>
            pair.Key == "Status" && pair.Value == "No distribution selected");
        Assert.False(viewModel.CanProceed);

        config.SelectedInstallWorkflow = InstallWorkflowType.Iso;
        config.SelectedIsoPath = " ";
        Assert.False(viewModel.CanProceed);
    }

    [Fact]
    public async Task CompleteDistroConfiguration_CanProceed()
    {
        var disk = new Disk
        {
            Id = "disk",
            Name = "Disk",
            Size = AutomaticPartitionPlanner.MinimumPartitionSizeBytes +
                (2 * AutomaticPartitionPlanner.PartitionAlignmentBytes),
            PartitionStyle = "GPT",
            IsEligibleForInstallation = true,
            Partitions = []
        };
        var plan = new PartitionPlan { TargetDisk = disk };
        plan.AddPartition(new PlannedPartition
        {
            Id = "root",
            DiskId = disk.Id,
            Name = "Linux root",
            Size = AutomaticPartitionPlanner.MinimumPartitionSizeBytes,
            StartOffset = AutomaticPartitionPlanner.PartitionAlignmentBytes,
            FileSystem = FileSystem.LINUX,
            MountPoint = "/",
            IsSystem = false,
            IsExisting = false
        });
        var config = new InstallationConfigService
        {
            SelectedInstallWorkflow = InstallWorkflowType.Distro,
            SelectedDistro = (await new DistroService().GetDistros()).First(),
            PartitionPlan = plan,
            UserInfo = new UserInfo
            {
                Username = "linux-user",
                Password = "secret",
                ConfirmPassword = "secret"
            }
        };
        var viewModel = new InstallationSummaryViewModel(
            new NavigationService(),
            config);

        Assert.True(viewModel.CanProceed);

        config.UserInfo.ConfirmPassword = "different";
        Assert.False(viewModel.CanProceed);
    }
}
