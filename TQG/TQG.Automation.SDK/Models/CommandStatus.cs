namespace TQG.Automation.SDK.Models;

/// <summary>
/// Status codes for command execution results.
/// Simplified to Success/Failed for external consumers.
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
    /// - Error during execution (e.g., connection issue, stopped on alarm when stopOnAlarm=true)
    /// - Command timeout
    /// - Command cancellation
    /// </summary>
    Failed
}
