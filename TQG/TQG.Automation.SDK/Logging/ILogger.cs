namespace TQG.Automation.SDK.Logging;

/// <summary>
/// Interface định nghĩa các phương thức logging cơ bản.
/// </summary>
public interface ILogger
{
    /// <summary>
    /// Mức log tối thiểu được ghi nhận.
    /// </summary>
    LogLevel MinimumLevel { get; }

    /// <summary>
    /// Kiểm tra xem một mức log có được enable hay không.
    /// </summary>
    /// <param name="level">Mức log cần kiểm tra.</param>
    /// <returns>True nếu mức log được enable.</returns>
    bool IsEnabled(LogLevel level);

    /// <summary>
    /// Ghi log ở mức Debug.
    /// </summary>
    /// <param name="message">Thông báo log.</param>
    void LogDebug(string message);

    /// <summary>
    /// Ghi log ở mức Information.
    /// </summary>
    /// <param name="message">Thông báo log.</param>
    void LogInformation(string message);

    /// <summary>
    /// Ghi log ở mức Warning.
    /// </summary>
    /// <param name="message">Thông báo log.</param>
    void LogWarning(string message);

    /// <summary>
    /// Ghi log ở mức Error.
    /// </summary>
    /// <param name="message">Thông báo log.</param>
    /// <param name="exception">Exception tùy chọn.</param>
    void LogError(string message, Exception? exception = null);

    /// <summary>
    /// Ghi log ở mức Critical.
    /// </summary>
    /// <param name="message">Thông báo log.</param>
    /// <param name="exception">Exception tùy chọn.</param>
    void LogCritical(string message, Exception? exception = null);
}
