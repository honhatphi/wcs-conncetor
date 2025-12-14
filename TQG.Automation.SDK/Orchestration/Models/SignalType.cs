namespace TQG.Automation.SDK.Orchestration.Models;

/// <summary>
/// Types of signals that can be detected during command execution.
/// </summary>
public enum SignalType
{
    /// <summary>
    /// No signal detected.
    /// </summary>
    None,

    /// <summary>
    /// Alarm signal detected (ErrorCode != 0).
    /// </summary>
    Alarm,

    /// <summary>
    /// Command failed signal detected.
    /// </summary>
    CommandFailed,

    /// <summary>
    /// Command completed successfully signal detected.
    /// </summary>
    CommandCompleted
}
