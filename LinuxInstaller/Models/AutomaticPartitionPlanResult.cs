namespace LinuxInstaller.Models;

public enum AutomaticPartitionPlanFailure
{
    None,
    NoEligibleDisk,
    NoEfiSystemPartition,
    NoSuitableFreeSpace
}

public sealed class AutomaticPartitionPlanResult
{
    private AutomaticPartitionPlanResult(
        Disk? targetDisk,
        Partition? efiPartition,
        PlannedPartition? rootPartition,
        AutomaticPartitionPlanFailure failure)
    {
        TargetDisk = targetDisk;
        EfiPartition = efiPartition;
        RootPartition = rootPartition;
        Failure = failure;
    }

    public bool IsSuccess => Failure == AutomaticPartitionPlanFailure.None;
    public Disk? TargetDisk { get; }
    public Partition? EfiPartition { get; }
    public PlannedPartition? RootPartition { get; }
    public AutomaticPartitionPlanFailure Failure { get; }

    internal static AutomaticPartitionPlanResult Success(
        Disk targetDisk,
        Partition efiPartition,
        PlannedPartition rootPartition)
    {
        return new AutomaticPartitionPlanResult(
            targetDisk,
            efiPartition,
            rootPartition,
            AutomaticPartitionPlanFailure.None);
    }

    internal static AutomaticPartitionPlanResult Failed(AutomaticPartitionPlanFailure failure)
    {
        return new AutomaticPartitionPlanResult(null, null, null, failure);
    }
}
