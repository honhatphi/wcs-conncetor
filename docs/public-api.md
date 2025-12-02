# 2. Public API

## 2.1 AutomationGateway (facade chính)

### Khởi tạo & vòng đời
```csharp
// Đăng ký thiết bị từ JSON hoặc danh sách options
void Initialize(string configurations);
void Initialize(IEnumerable<PlcConnectionOptions> configurations);

// Kích hoạt kết nối vật lý + verify link PLC
Task ActivateDevice(string deviceId, CancellationToken ct = default);
Task<bool> ActivateAllDevicesAsync(CancellationToken ct = default);

// Hủy kích hoạt
Task DeactivateDevice(string deviceId);
Task DeactivateAllDevicesAsync();
```

### Trạng thái & truy vấn
```csharp
bool IsConnected(string deviceId);
Task<DeviceStatus> GetDeviceStatusAsync(string deviceId, CancellationToken ct = default);
Task<DeviceStatus[]> GetAllDeviceStatusAsync(CancellationToken ct = default);
Task<Location?> GetActualLocationAsync(string deviceId, CancellationToken ct = default);
IEnumerable<string> DeviceIds { get; }
int DeviceCount { get; }
bool IsInitialized { get; }
```

### Điều phối lệnh
```csharp
Task SendCommand(TransportTask task);
Task<SubmissionResult> SendMultipleCommands(IEnumerable<TransportTask> tasks, CancellationToken ct = default);

void PauseQueue();
void ResumeQueue();
bool IsPauseQueue { get; }

bool RemoveCommand(string commandId);
// Mở rộng: xóa nhiều lệnh pending (nếu có)
int RemoveCommands(IEnumerable<string> commandIds);
```

### Barcode Validation (INBOUND)
```csharp
event EventHandler<BarcodeReceivedEventArgs> BarcodeReceived;

Task<bool> SendValidationResult(
    string taskId,
    bool isValid,
    Location? destinationLocation = null,
    Direction? direction = null,
    int? gateNumber = null);
```

### Phục hồi thiết bị
```csharp
Task<bool> ResetDeviceStatusAsync(string deviceId);
```

---

## 2.2 Kiểu dữ liệu điển hình

### Enums
```csharp
/// Loại lệnh điều khiển kho
public enum CommandType 
{ 
    Inbound,     // Nhập hàng từ bên ngoài vào kho
    Outbound,    // Xuất hàng từ kho ra bên ngoài
    Transfer,    // Di chuyển nội bộ trong kho
    CheckPallet  // Kiểm tra pallet tại vị trí
}

/// Trạng thái thiết bị (Offline gây throw PlcConnectionFailedException)
public enum DeviceStatus 
{ 
    Idle,   // Sẵn sàng nhận nhiệm vụ
    Busy,   // Đang thực thi
    Error   // Có lỗi cần xử lý
}

/// Hướng di chuyển vật liệu
public enum Direction 
{ 
    Top,     // Vào/ra từ phía trên
    Bottom   // Vào/ra từ phía dưới
}

/// Trạng thái kết quả lệnh
public enum CommandStatus 
{ 
    Success,  // Hoàn thành (bao gồm cả warning khi có alarm nhưng vẫn complete)
    Failed,   // Thất bại (CommandFailed, timeout, cancelled)
    Error     // Alarm detected (notification trung gian)
}
```

### Location
```csharp
/// Vị trí trong kho với tọa độ chi tiết
public sealed record Location
{
    public required int Floor { get; init; }  // Tầng
    public required int Rail { get; init; }   // Đường ray
    public required int Block { get; init; }  // Cột/Block
    public int Depth { get; init; } = 1;      // Độ sâu trong block

    public override string ToString() => $"F{Floor}R{Rail}B{Block}D{Depth}";
    public static Location Parse(string locationString);
}
```

### TransportTask
```csharp
/// Nhiệm vụ vận chuyển
public class TransportTask 
{
    public required string TaskId { get; set; }
    public string? DeviceId { get; set; }           // null = hệ thống tự assign
    public CommandType CommandType { get; set; }
    public Location? SourceLocation { get; set; }   // Outbound/Transfer/CheckPallet
    public Location? TargetLocation { get; set; }   // Transfer/Inbound (sau validate)
    public int GateNumber { get; set; }             // Inbound/Outbound
    public Direction InDirBlock { get; set; }       // Hướng vào block (Phase 1: default Bottom)
    public Direction OutDirBlock { get; set; }      // Hướng ra block (Phase 1: default Bottom)
}
```

> **Ghi chú Phase 1**: `InDirBlock` và `OutDirBlock` mặc định là `Direction.Bottom`. Client có thể không truyền giá trị này trong giai đoạn 1.

### SubmissionResult
```csharp
/// Kết quả submit lệnh hàng loạt
public sealed record SubmissionResult
{
    public int Submitted { get; init; }                            // Số lệnh submitted thành công
    public int Rejected { get; init; }                             // Số lệnh bị reject
    public IReadOnlyList<RejectCommand> RejectedCommands { get; init; } = [];
}

public record RejectCommand(TransportTask Command, string Reason);
```

### ErrorDetail
```csharp
/// Chi tiết lỗi từ PLC
public sealed class ErrorDetail(int errorCode, string errorMessage)
{
    public int ErrorCode { get; init; }
    public string ErrorMessage { get; init; }
    public DateTimeOffset DetectedAt { get; init; } = DateTimeOffset.UtcNow;

    public override string ToString() => $"[{ErrorCode}] {ErrorMessage}";
}
```

### CommandResultNotification
```csharp
/// Thông báo kết quả thực thi lệnh (qua events)
public sealed record CommandResultNotification
{
    public required string CommandId { get; init; }
    public required string PlcDeviceId { get; init; }
    public required CommandStatus Status { get; init; }
    public string? Message { get; init; }
    public DateTimeOffset CompletedAt { get; init; }
    public TimeSpan Duration { get; init; }
    public ErrorDetail? PlcError { get; init; }
    public object? Data { get; init; }  // Execution steps (mới)
}
```

### Event Args
```csharp
/// Barcode validation request (INBOUND)
public sealed class BarcodeReceivedEventArgs : EventArgs
{
    public required string TaskId { get; init; }
    public required string DeviceId { get; init; }
    public required string Barcode { get; init; }  // 10 ký tự
    public DateTimeOffset RequestedAt { get; init; }
}

/// Task completed successfully
public class TaskSucceededEventArgs(string deviceId, string taskId) : EventArgs
{
    public string DeviceId { get; }
    public string TaskId { get; }
}

/// Task failed
public sealed class TaskFailedEventArgs(string deviceId, string taskId, ErrorDetail errorDetail) : EventArgs
{
    public string DeviceId { get; }
    public string TaskId { get; }
    public ErrorDetail ErrorDetail { get; }
}

/// Alarm detected during execution (informational)
public sealed class TaskAlarmEventArgs(string deviceId, string taskId, ErrorDetail error) : EventArgs
{
    public string DeviceId { get; }
    public string TaskId { get; }
    public ErrorDetail Error { get; }
}
```
