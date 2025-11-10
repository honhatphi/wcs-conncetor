namespace TQG.Automation.SDK.Shared;

public class TransportTask
{
    public required string TaskId { get; set; }

    public string? DeviceId { get; set; }

    public CommandType CommandType { get; set; }

    public Location? SourceLocation { get; set; }

    public Location? TargetLocation { get; set; }

    public int GateNumber { get; set; }

    public Direction InDirBlock { get; set; }

    public Direction OutDirBlock { get; set; }
}