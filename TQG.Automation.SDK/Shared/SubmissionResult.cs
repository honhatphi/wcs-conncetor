namespace TQG.Automation.SDK.Shared;

/// <summary>
/// Result of bulk command submission operation.
/// </summary>
public sealed record SubmissionResult
{
    /// <summary>
    /// Number of commands successfully submitted to the queue.
    /// </summary>
    public int Submitted { get; init; }

    /// <summary>
    /// Number of commands rejected during submission.
    /// </summary>
    public int Rejected { get; init; }

    /// <summary>
    /// Details of rejected commands with reasons.
    /// </summary>
    public IReadOnlyList<RejectCommand> RejectedCommands { get; init; } = [];
}


public record RejectCommand(TransportTask Command, string Reason);