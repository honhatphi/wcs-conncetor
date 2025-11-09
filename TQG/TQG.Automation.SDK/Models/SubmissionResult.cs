namespace TQG.Automation.SDK.Models;

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
    public IReadOnlyList<(CommandRequest Command, string Reason)> RejectedCommands { get; init; } = [];
}
