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
```csharp
public enum CommandType { Inbound, Outbound, Transfer }
public enum DeviceStatus { Offline, Idle, Busy, Error }
public enum Direction { Bottom = 0, Top = 1 }

public record Location(short Floor, short Rail, short Block);

public sealed class TransportTask {
  public string TaskId { get; init; } = default!;
  public string? DeviceId { get; init; }  // nếu null, hệ thống tự assign
  public CommandType CommandType { get; init; }
  public Location? SourceLocation { get; init; } // Outbound/Transfer
  public Location? TargetLocation { get; init; } // Transfer/Inbound sau validate
  public int? GateNumber { get; init; }          // Inbound/Outbound
  public Direction? InDirBlock { get; init; }    // Inbound
  public Direction? OutDirBlock { get; init; }   // Outbound/Transfer
}
```
