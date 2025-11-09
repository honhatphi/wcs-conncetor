namespace TQG.Automation.SDK.Core;

/// <summary>
/// Represents the result of a PLC operation, encapsulating success, error, and data.
/// </summary>
internal readonly record struct PlcResult
{
    /// <summary>
    /// Gets whether the operation succeeded.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Gets the error message if the operation failed; otherwise, null.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Gets the data returned by the operation; otherwise, null.
    /// </summary>
    public object? Data { get; init; }

    /// <summary>
    /// Creates a successful result with data.
    /// </summary>
    /// <param name="data">The operation result data.</param>
    /// <returns>A successful PlcResult.</returns>
    public static PlcResult Ok(object? data = null)
        => new() { Success = true, Data = data };

    /// <summary>
    /// Creates a failed result with an error message.
    /// </summary>
    /// <param name="error">The error message.</param>
    /// <returns>A failed PlcResult.</returns>
    public static PlcResult Fail(string error)
        => new() { Success = false, Error = error };

    /// <summary>
    /// Creates a result from an exception.
    /// </summary>
    /// <param name="exception">The exception that occurred.</param>
    /// <returns>A failed PlcResult with the exception message.</returns>
    public static PlcResult FromException(Exception exception)
        => new() { Success = false, Error = exception.Message };
}
