using System.ComponentModel;

namespace TQG.Automation.SDK.Shared;

/// <summary>
/// Represents the operational status of a device.
/// Note: If device is offline or link not established, GetDeviceStatusAsync will throw PlcConnectionFailedException.
/// </summary>
public enum DeviceStatus
{
    /// <summary>
    /// Thiết bị rảnh rỗi và sẵn sàng nhận nhiệm vụ mới.
    /// </summary>
    [Description("Idle")]
    Idle,

    /// <summary>
    /// Thiết bị đang thực thi một nhiệm vụ.
    /// </summary>
    [Description("Busy")]
    Busy,

    /// <summary>
    /// Thiết bị ở trạng thái lỗi và cần được xử lý.
    /// </summary>
    [Description("Error")]
    Error,
}
