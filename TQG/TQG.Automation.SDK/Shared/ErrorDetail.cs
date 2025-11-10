namespace TQG.Automation.SDK.Shared;

/// <summary>
/// Represents a PLC error with code and descriptive message.
/// </summary>
public sealed class ErrorDetail(int errorCode, string errorMessage)
{
    /// <summary>
    /// Gets the error code from PLC.
    /// </summary>
    public int ErrorCode { get; init; } = errorCode;

    /// <summary>
    /// Gets the human-readable error message.
    /// </summary>
    public string ErrorMessage { get; init; } = errorMessage;

    /// <summary>
    /// Gets the timestamp when the error was detected.
    /// </summary>
    public DateTimeOffset DetectedAt { get; init; } = DateTimeOffset.UtcNow;

    public override string ToString() => $"[{ErrorCode}] {ErrorMessage}";

    public static ErrorDetail Exception(string message) => new(999, message);
}
