namespace TQG.Automation.SDK.Logging;

/// <summary>
/// Định nghĩa các mức độ logging.
/// </summary>
public enum LogLevel
{
    /// <summary>
    /// Log chi tiết để debug.
    /// </summary>
    Debug = 0,

    /// <summary>
    /// Log thông tin chung.
    /// </summary>
    Information = 1,

    /// <summary>
    /// Log cảnh báo.
    /// </summary>
    Warning = 2,

    /// <summary>
    /// Log lỗi.
    /// </summary>
    Error = 3,

    /// <summary>
    /// Log lỗi nghiêm trọng.
    /// </summary>
    Critical = 4
}
