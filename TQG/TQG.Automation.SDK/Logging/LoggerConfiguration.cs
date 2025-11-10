namespace TQG.Automation.SDK.Logging;

/// <summary>
/// Cấu hình cho logger.
/// </summary>
public sealed class LoggerConfiguration
{
    /// <summary>
    /// Mức log tối thiểu.
    /// </summary>
    public LogLevel MinimumLevel { get; init; } = LogLevel.Information;

    /// <summary>
    /// Cho phép ghi log vào file.
    /// </summary>
    public bool EnableFileOutput { get; init; } = true;

    /// <summary>
    /// Cho phép output debug.
    /// </summary>
    public bool EnableDebugOutput { get; init; } = true;

    /// <summary>
    /// Include timestamp trong log.
    /// </summary>
    public bool IncludeTimestamp { get; init; } = true;

    /// <summary>
    /// Include component name trong log.
    /// </summary>
    public bool IncludeComponentName { get; init; } = true;

    /// <summary>
    /// Tạo cấu hình mặc định.
    /// </summary>
    public static LoggerConfiguration Default => new()
    {
        MinimumLevel = LogLevel.Information,
        EnableFileOutput = true,
        EnableDebugOutput = false,
        IncludeTimestamp = true,
        IncludeComponentName = true
    };

    /// <summary>
    /// Tạo cấu hình cho debug.
    /// </summary>
    public static LoggerConfiguration Debug => new()
    {
        MinimumLevel = LogLevel.Debug,
        EnableFileOutput = true,
        EnableDebugOutput = true,
        IncludeTimestamp = true,
        IncludeComponentName = true
    };
}
