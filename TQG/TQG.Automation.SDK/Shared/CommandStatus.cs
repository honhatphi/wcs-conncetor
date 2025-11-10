namespace TQG.Automation.SDK.Shared;

/// <summary>
/// Status codes for command execution results.
/// </summary>
public enum CommandStatus
{
    /// <summary>
    /// Command completed successfully.
    /// Includes:
    /// - Normal successful completion
    /// - Completion with warnings (alarm detected but command still completed)
    /// </summary>
    Success,

    /// <summary>
    /// Command failed to complete.
    /// Includes:
    /// - PLC signaled CommandFailed flag
    /// - Command timeout
    /// - Command cancellation
    /// </summary>
    Failed,

    /// <summary>
    /// Error/Alarm detected during execution (intermediate notification).
    /// This is an informational status sent immediately when ErrorAlarm is detected.
    /// The command may continue or fail depending on failOnAlarm configuration.
    /// Final result will be Success, Failed, or another Error status.
    /// </summary>
    Error
}
