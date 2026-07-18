using LinuxInstaller.Models;
using LinuxInstaller.Services;
using LinuxInstaller.ViewModels;

namespace LinuxInstaller.Tests.ViewModels;

public class DistroPickerViewModelTests
{
    [Fact]
    public void FailedAutomaticPlan_PreservesExistingPlanAndWorkflow()
    {
        var config = new InstallationConfigService
        {
            SelectedPartitionWorkflow = PartitionWorkflowType.Manual
        };
        var existingPlan = new PartitionPlan { TargetDisk = CreateDisk("existing") };
        existingPlan.AddPartition(CreateRootPartition("existing"));
        config.PartitionPlan = existingPlan;
        var viewModel = CreateViewModel(config);
        var failedResult = AutomaticPartitionPlanner.CreatePlan([]);

        var applied = viewModel.TryApplyAutomaticPartitionPlan(failedResult);

        Assert.False(applied);
        Assert.Same(existingPlan, config.PartitionPlan);
        Assert.Equal(PartitionWorkflowType.Manual, config.SelectedPartitionWorkflow);
    }

    [Fact]
    public void SuccessfulAutomaticPlan_ReplacesPlanAfterPlanningSucceeds()
    {
        var config = new InstallationConfigService();
        var disk = CreateDisk("automatic");
        var result = AutomaticPartitionPlanner.CreatePlan([disk]);
        var viewModel = CreateViewModel(config);

        var applied = viewModel.TryApplyAutomaticPartitionPlan(result);

        Assert.True(applied);
        Assert.Equal(PartitionWorkflowType.Automatic, config.SelectedPartitionWorkflow);
        Assert.Same(disk, config.PartitionPlan.TargetDisk);
        var root = Assert.Single(config.PartitionPlan.PartitionHistory.Last());
        Assert.Equal("/", root.MountPoint);
        Assert.False(root.IsExisting);
    }

    private static DistroPickerViewModel CreateViewModel(InstallationConfigService config)
    {
        return new DistroPickerViewModel(
            new NavigationService(),
            new DistroService(),
            config,
            new PartitionService(new FakeStorageManager()));
    }

    private static Disk CreateDisk(string id)
    {
        return new Disk
        {
            Id = id,
            Name = id,
            Size = AutomaticPartitionPlanner.MinimumPartitionSizeBytes +
                (2 * AutomaticPartitionPlanner.PartitionAlignmentBytes),
            PartitionStyle = "GPT",
            IsEligibleForInstallation = true,
            Partitions = []
        };
    }

    private static PlannedPartition CreateRootPartition(string diskId)
    {
        return new PlannedPartition
        {
            Id = "root",
            DiskId = diskId,
            Name = "Linux root",
            Size = AutomaticPartitionPlanner.MinimumPartitionSizeBytes,
            StartOffset = AutomaticPartitionPlanner.PartitionAlignmentBytes,
            FileSystem = FileSystem.LINUX,
            MountPoint = "/",
            IsSystem = false,
            IsExisting = false
        };
    }

    private sealed class FakeStorageManager : IStorageManager
    {
        public Task<IReadOnlyList<Disk>> GetDisksAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<Disk>>([]);
        }
    }
}
