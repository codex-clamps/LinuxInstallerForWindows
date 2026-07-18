namespace LinuxInstaller.Models;

public enum AutomaticPartitionPlanFailure
{
    None,
    NoEligibleDisk,
    NoSuitableFreeSpace
}

public sealed class AutomaticPartitionPlanResult
{
    private AutomaticPartitionPlanResult(
        Disk? targetDisk,
        PlannedPartition? rootPartition,
        AutomaticPartitionPlanFailure failure)
    {
        TargetDisk = targetDisk;
        RootPartition = rootPartition;
        Failure = failure;
    }

    public bool IsSuccess => Failure == AutomaticPartitionPlanFailure.None;
    public Disk? TargetDisk { get; }
    public PlannedPartition? RootPartition { get; }
    public AutomaticPartitionPlanFailure Failure { get; }

    internal static AutomaticPartitionPlanResult Success(
        Disk targetDisk,
        PlannedPartition rootPartition)
    {
        return new AutomaticPartitionPlanResult(
            targetDisk,
            rootPartition,
            AutomaticPartitionPlanFailure.None);
    }

    internal static AutomaticPartitionPlanResult Failed(AutomaticPartitionPlanFailure failure)
    {
        return new AutomaticPartitionPlanResult(null, null, failure);
    }
}
