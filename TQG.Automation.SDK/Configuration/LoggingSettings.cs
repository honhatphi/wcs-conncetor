using System.Text.Json.Serialization;
using TQG.Automation.SDK.Logging;

namespace TQG.Automation.SDK.Configuration;

/// <summary>
/// Cấu hình logging từ JSON.
/// </summary>
public sealed class LoggingSettings
{
    /// <summary>
    /// Mức log tối thiểu: Debug, Information, Warning, Error, Critical.
    /// Mặc định: Information.
    /// </summary>
    [JsonPropertyName("minimumLevel")]
    public LogLevel MinimumLevel { get; set; } = LogLevel.Information;

    /// <summary>
    /// Cho phép ghi log vào file.
    /// Mặc định: true.
    /// </summary>
    [JsonPropertyName("enableFileOutput")]
    public bool EnableFileOutput { get; set; } = true;

    /// <summary>
    /// Cho phép output debug (System.Diagnostics.Debug).
    /// Mặc định: false.
    /// </summary>
    [JsonPropertyName("enableDebugOutput")]
    public bool EnableDebugOutput { get; set; } = false;

    /// <summary>
    /// Bao gồm timestamp trong log.
    /// Mặc định: true.
    /// </summary>
    [JsonPropertyName("includeTimestamp")]
    public bool IncludeTimestamp { get; set; } = true;

    /// <summary>
    /// Bao gồm tên component trong log.
    /// Mặc định: true.
    /// </summary>
    [JsonPropertyName("includeComponentName")]
    public bool IncludeComponentName { get; set; } = true;

    /// <summary>
    /// Chuyển đổi sang LoggerConfiguration.
    /// </summary>
    public LoggerConfiguration ToLoggerConfiguration()
    {
        return new LoggerConfiguration
        {
            MinimumLevel = MinimumLevel,
            EnableFileOutput = EnableFileOutput,
            EnableDebugOutput = EnableDebugOutput,
            IncludeTimestamp = IncludeTimestamp,
            IncludeComponentName = IncludeComponentName
        };
    }
}
