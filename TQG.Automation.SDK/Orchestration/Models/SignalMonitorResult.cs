using TQG.Automation.SDK.Shared;

namespace TQG.Automation.SDK.Orchestration.Models;

/// <summary>
/// Result from the signal monitor indicating what signal was detected.
/// </summary>
public sealed record SignalMonitorResult
{
    /// <summary>
    /// The type of signal that was detected.
    /// </summary>
    public required SignalType Type { get; init; }

    /// <summary>
    /// Error details if an alarm or failure was detected.
    /// </summary>
    public ErrorDetail? Error { get; init; }

    /// <summary>
    /// Timestamp when the signal was detected.
    /// </summary>
    public DateTimeOffset DetectedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Creates a result indicating no signal was detected.
    /// </summary>
    public static SignalMonitorResult None() => new() { Type = SignalType.None };

    /// <summary>
    /// Creates a result indicating an alarm was detected.
    /// </summary>
    public static SignalMonitorResult Alarm(ErrorDetail error) => new()
    {
        Type = SignalType.Alarm,
        Error = error
    };

    /// <summary>
    /// Creates a result indicating command failed.
    /// </summary>
    public static SignalMonitorResult Failed(ErrorDetail? error = null) => new()
    {
        Type = SignalType.CommandFailed,
        Error = error
    };

    /// <summary>
    /// Creates a result indicating command completed successfully.
    /// </summary>
    public static SignalMonitorResult Completed(ErrorDetail? warningError = null) => new()
    {
        Type = SignalType.CommandCompleted,
        Error = warningError
    };
}
