namespace TQG.Automation.SDK.Orchestration.Models;

/// <summary>
/// Internal execution status codes (maps to public CommandStatus).
/// Simplified to 4 statuses for clearer state management.
/// </summary>
internal enum ExecutionStatus
{
    /// <summary>
    /// Command completed successfully.
    /// </summary>
    Success,

    /// <summary>
    /// Alarm detected during execution (INTERMEDIATE status).
    /// Command is still executing - not yet completed.
    /// Used for alarm notifications while command continues.
    /// </summary>
    Alarm,

    /// <summary>
    /// Command failed (includes PLC CommandFailed, cancelled, connection errors).
    /// </summary>
    Failed,

    /// <summary>
    /// Command execution timed out.
    /// </summary>
    Timeout
}
