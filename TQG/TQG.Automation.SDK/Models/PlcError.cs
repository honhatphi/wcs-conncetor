namespace TQG.Automation.SDK.Models;

/// <summary>
/// Represents a PLC error with code and descriptive message.
/// </summary>
public sealed class PlcError(int code, string message, string? commandId = null)
{
    /// <summary>
    /// Gets the error code from PLC.
    /// </summary>
    public int Code { get; init; } = code;

    /// <summary>
    /// Gets the human-readable error message.
    /// </summary>
    public string Message { get; init; } = message;

    /// <summary>
    /// Gets the timestamp when the error was detected.
    /// </summary>
    public DateTimeOffset DetectedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets the command ID that encountered this error (if applicable).
    /// </summary>
    public string? CommandId { get; init; } = commandId;

    public override string ToString() => $"[{Code}] {Message}";
}
