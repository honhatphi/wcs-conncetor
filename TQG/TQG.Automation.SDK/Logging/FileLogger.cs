using System.Text;

namespace TQG.Automation.SDK.Logging;

/// <summary>
/// Triển khai file-based logger ghi log vào file trong thư mục được chỉ định.
/// Tự động tạo file log theo ngày và quản lý đồng thời ghi log từ nhiều thread.
/// </summary>
public class FileLogger : ILogger
{
    private readonly string _componentName;
    private readonly LoggerConfiguration _config;
    private readonly string _logDirectory;
    private readonly object _lock = new();

    public LogLevel MinimumLevel => _config.MinimumLevel;

    /// <summary>
    /// Khởi tạo instance mới của lớp FileLogger.
    /// </summary>
    /// <param name="componentName">Tên component sẽ được hiển thị trong log.</param>
    /// <param name="config">Cấu hình logger để điều khiển hành vi logging.</param>
    public FileLogger(string componentName, LoggerConfiguration config)
    {
        _componentName = componentName ?? throw new ArgumentNullException(nameof(componentName));
        _config = config ?? throw new ArgumentNullException(nameof(config));

        _logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TQG", "Gateway", "logs");
        Directory.CreateDirectory(_logDirectory);
    }

    public bool IsEnabled(LogLevel level)
    {
        return level >= _config.MinimumLevel;
    }

    public void LogDebug(string message)
    {
        if (IsEnabled(LogLevel.Debug) && _config.EnableDebugOutput)
        {
            WriteLog(LogLevel.Debug, message);
        }
    }

    public void LogInformation(string message)
    {
        if (IsEnabled(LogLevel.Information))
        {
            WriteLog(LogLevel.Information, message);
        }
    }

    public void LogWarning(string message)
    {
        if (IsEnabled(LogLevel.Warning))
        {
            WriteLog(LogLevel.Warning, message);
        }
    }

    public void LogError(string message, Exception? exception = null)
    {
        if (IsEnabled(LogLevel.Error))
        {
            var formattedMessage = message;
            if (exception != null)
            {
                formattedMessage += $" | Exception: {exception}";
            }
            WriteLog(LogLevel.Error, formattedMessage);
        }
    }

    public void LogCritical(string message, Exception? exception = null)
    {
        if (IsEnabled(LogLevel.Critical))
        {
            var formattedMessage = message;
            if (exception != null)
            {
                formattedMessage += $" | Exception: {exception}";
            }
            WriteLog(LogLevel.Critical, formattedMessage);
        }
    }

    /// <summary>
    /// Ghi một entry log vào file với thread safety.
    /// </summary>
    /// <param name="level">Mức log.</param>
    /// <param name="message">Thông báo cần ghi.</param>
    private void WriteLog(LogLevel level, string message)
    {
        if (!_config.EnableFileOutput)
            return;

        lock (_lock)
        {
            try
            {
                var logEntry = FormatLogEntry(level, message);
                var fileName = GetLogFileName();
                var filePath = Path.Combine(_logDirectory, fileName);

                File.AppendAllText(filePath, logEntry + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to write to log file: {ex.Message}");
                Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] [{_componentName}] {message}");
            }
        }
    }

    /// <summary>
    /// Định dạng log entry theo cấu hình đã thiết lập.
    /// </summary>
    /// <param name="level">Mức log.</param>
    /// <param name="message">Thông báo cần định dạng.</param>
    /// <returns>Log entry đã được định dạng.</returns>
    private string FormatLogEntry(LogLevel level, string message)
    {
        var parts = new List<string>();

        if (_config.IncludeTimestamp)
        {
            parts.Add($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}]");
        }

        parts.Add($"[{level}]");

        if (_config.IncludeComponentName)
        {
            parts.Add($"[{_componentName}]");
        }

        parts.Add(message);

        return string.Join(" ", parts);
    }

    /// <summary>
    /// Lấy tên file log dựa trên ngày hiện tại.
    /// </summary>
    /// <returns>Tên file log theo định dạng gateway_yyyy-MM-dd.log.</returns>
    private static string GetLogFileName()
    {
        var date = DateTime.Now.ToString("yyyy-MM-dd");
        return $"gateway_{date}.log";
    }
}
