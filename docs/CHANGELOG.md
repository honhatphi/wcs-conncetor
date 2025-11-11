# Changelog API – Automation Gateway
Tài liệu này ghi nhận thay đổi API công khai giữa **AutomationGatewayBase** (cũ) và **AutomationGateway** (mới).

## 1) Tổng quan thay đổi
- Chuẩn hoá **facade duy nhất**: `AutomationGateway.Instance`.
- Bổ sung **khởi tạo từ JSON** và **tải layout kho**.
- Đơn giản hoá **SendValidationResult**: bỏ `deviceId`, thêm `optional direction`, `nullable gate`.
- Thêm **SwitchModeAsync** (Real ↔ Simulation) và **ResetDeviceStatusAsync** có ràng buộc trạng thái.
- Chuẩn hoá **Pause/Resume/IsPauseQueue** và **RemoveCommand**.
- Mở rộng API **truy vấn thiết bị**: `DeviceIds`, `DeviceCount`, `IsInitialized`.

## 2) Thay đổi theo nhóm API

### 2.1 Khởi tạo & vòng đời
**Mới**
- `void Initialize(IEnumerable<PlcConnectionOptions> configurations)`
- `void Initialize(string configurations)` — nạp từ JSON
- `void LoadWarehouseLayout(string layoutJson)`
- `WarehouseLayout GetWarehouseLayout()`
- `Task ActivateDevice(string deviceId)` (giữ nguyên hành vi, moved)
- `Task<bool> ActivateAllDevicesAsync()` (mới)
- `Task DeactivateDevice(string deviceId)` (giữ nguyên hành vi, moved)
- `Task DeactivateAllDevicesAsync()` (mới)
- Thuộc tính: `IsInitialized`, `DeviceIds`, `DeviceCount`

**Cũ**
- Khởi tạo qua constructor abstract `AutomationGatewayBase(devices, appConfig)`
- Không có `LoadWarehouseLayout` / `GetWarehouseLayout`
- Không có `ActivateAllDevicesAsync` / `DeactivateAllDevicesAsync`
- Không có `IsInitialized`, `DeviceIds`, `DeviceCount`

### 2.2 Orchestrator & Hàng đợi
**Mới**
- `void PauseQueue()` / `void ResumeQueue()`
- `bool IsPauseQueue { get; }`
- `bool RemoveCommand(string commandId)`

**Cũ**
- `PauseQueue()`, `ResumeQueue()`, `IsPauseQueue` tồn tại ở mức Dispatcher, không có `RemoveCommand(string)` cho pending theo ID.

### 2.3 Gửi lệnh
**Mới**
- `Task<SubmissionResult> SendCommand(TransportTask task)`
  - Trả về `SubmissionResult` với thông tin validate
  - Hỗ trợ 4 loại command: **Inbound**, **Outbound**, **Transfer**, **CheckPallet**
- `Task<SubmissionResult> SendMultipleCommands(IEnumerable<TransportTask> tasks)`
  - Trả về `SubmissionResult` với thông tin `Submitted`, `Rejected`, `RejectedCommands`
  - Validate toàn bộ tasks trước khi submit
  - Tasks không hợp lệ được reject với lý do cụ thể

**CheckPallet Command - Mới**
- `CommandType.CheckPallet` - Kiểm tra sự hiện diện của pallet tại vị trí
- **Flow**: Write Source Location + Depth → Trigger → Start Process → Wait Result
- **Result**: Trả về `PalletAvailable` hoặc `PalletUnavailable` trong `TaskSucceeded` event
- **Alarm Behavior**: **Luôn fail ngay** khi có alarm (bỏ qua `FailOnAlarm` config)
- **Use Case**: Kiểm tra trước khi thực hiện Outbound/Transfer operations

**Cũ**
- `Task SendCommand(TransportTask task)`
  - Không có return value (void)
  - Chỉ hỗ trợ 3 loại command: Inbound, Outbound, Transfer
  - **Không có CheckPallet command**
- `Task SendMultipleCommands(List<TransportTask> tasks)`
  - Không có return value cụ thể
  - Yêu cầu tiền kiểm tra kết nối cho từng `DeviceId` trước batch (ở Base)

### 2.4 Barcode Validation (Inbound)
**Mới**
- `event EventHandler<BarcodeReceivedEventArgs> BarcodeReceived`
- `Task<bool> SendValidationResult(string taskId, bool isValid, Location? destinationLocation = null, Direction? direction = null, int? gateNumber = null)`
  - Không cần `deviceId`
  - `direction` tùy chọn
  - `gateNumber` `nullable` và kiểm tra > 0 khi `isValid=true`
  - Trả `bool` báo nhận kết quả hay đã timeout
  - **Timeout: 5 phút** (300 giây) - thời gian chờ response từ client

**Cũ**
- `Task SendValidationResult(string deviceId, string taskId, bool isValid, Location? targetLocation, Direction direction, short gateNumber)`
  - Bắt buộc `deviceId`
  - `direction` bắt buộc và phải là giá trị hợp lệ
  - `gateNumber` không âm
  - Không có kết quả trả về `bool`
  - **Timeout: 2 phút** (comment cũ, code thực tế là 5 phút)

### 2.5 Trạng thái & truy vấn
**Mới**
- `bool IsConnected(string deviceId)`
- `Task<DeviceStatus> GetDeviceStatusAsync(string deviceId)`
- `Task<DeviceStatus[]> GetAllDeviceStatusAsync()`
- `Task<Location?> GetActualLocationAsync(string deviceId)`

**Cũ**
- `bool IsConnected(string deviceId)`
- `DeviceStatus GetDeviceStatus(string deviceId)` hoặc tương đương async nội bộ
- `Task<Location?> GetActualLocationAsync(string deviceId)`
- Có thêm `Task<List<DeviceInfo>> GetIdleDevicesAsync()` ở Base (KHÔNG còn public ở bản mới).

### 2.6 Chế độ kết nối & Phục hồi
**Mới**
- `Task SwitchModeAsync(string deviceId, PlcMode newMode, CancellationToken ct = default)` — chuyển Real/Simulation runtime
- `Task<bool> ResetDeviceStatusAsync(string deviceId)` — chặn khi Busy, raise recovery orchestration

**Cũ**
- Khả năng reset/monitor phân tán theo `DeviceMonitor`, không có `SwitchModeAsync` runtime.

### 2.7 Sự kiện
**Mới**
- `TaskSucceeded` - Phát sinh khi task hoàn thành thành công
- `TaskFailed` - Phát sinh khi task thất bại
- `TaskAlarm` - **[MỚI]** Phát sinh ngay khi phát hiện alarm (`ErrorAlarm = true`)
  - Được raise **trước** TaskSucceeded/TaskFailed
  - Chỉ raise **một lần** để tránh duplicate notification
  - Task có thể tiếp tục hoặc fail tùy vào `FailOnAlarm` config
- `BarcodeReceived` - Phát sinh khi nhận barcode từ PLC (Inbound only)

**Cũ**
- `TaskSucceeded`, `TaskFailed`, `BarcodeReceived`
- Không có `TaskAlarm` event


## 3) Thay đổi kết quả trả về quan trọng

| Nhóm | Cũ | Mới | Ghi chú |
|------|----|----|---------|
| Send Command | `Task SendCommand(...)` | `Task<SubmissionResult> SendCommand(...)` | Trả về validation result |
| Batch Submit | `Task SendMultipleCommands(List)` | `Task<SubmissionResult> SendMultipleCommands(IEnumerable)` | Trả về validation result |
| **CheckPallet** | ❌ Không có | ✅ `CommandType.CheckPallet` | **Command mới**: Kiểm tra pallet tại vị trí |
| CheckPallet Result | - | `PalletAvailable`, `PalletUnavailable` | Trả về trong `TaskSucceeded` event |
| CheckPallet Alarm | - | **Luôn fail** khi có alarm | Bỏ qua `FailOnAlarm` config |
| Barcode Validation | `SendValidationResult(deviceId, taskId, ...)` | `SendValidationResult(taskId, ...) : bool` | Bỏ deviceId, timeout 5 phút |
| Alarm Handling | Không có event | `TaskAlarm` event | Phát hiện alarm ngay lập tức |
| Layout | Không có | `LoadWarehouseLayout(json)`, `GetWarehouseLayout()` | Validate vị trí kho |
| Mode | Không có | `SwitchModeAsync(deviceId, PlcMode)` | Real ↔ Emulated runtime |
| Recovery | Reset rải rác | `ResetDeviceStatusAsync(deviceId) : bool` | Centralized recovery |

## 4) Alarm Handling - FailOnAlarm Configuration

**Mới thêm trong PlcConnectionOptions:**

```csharp
public bool FailOnAlarm { get; init; } = false;
```

### Behavior theo cấu hình:

**FailOnAlarm = false (Default - Continue Mode)**
- ⚠️ `TaskAlarm` event được raise ngay khi detect alarm
- ⏳ Task tiếp tục thực thi sau alarm
- ✅ Nếu PLC complete → `TaskSucceeded` với Warning status
- ❌ Nếu PLC failed → `TaskFailed`

**FailOnAlarm = true (Fail Fast Mode)**
- ⚠️ `TaskAlarm` event được raise ngay khi detect alarm
- ❌ Task fail ngay lập tức sau alarm
- ⛔ Không chờ PLC complete/failed

**Use cases:**
- `false`: Non-critical operations, cho phép PLC tự recover
- `true`: Critical operations, safety-first scenarios

**Lưu ý:**
- CheckPallet command luôn fail khi có alarm (bỏ qua FailOnAlarm)
- Alarm notification chỉ raise một lần (tránh duplicate)

## 4.1) CheckPallet Command - Chi tiết

**Tính năng mới hoàn toàn trong phiên bản này:**

### Command Type
```csharp
public enum CommandType
{
    Inbound,
    Outbound,
    Transfer,
    CheckPallet  // ← MỚI
}
```

### Cách sử dụng
```csharp
var checkTask = new TransportTask
{
    TaskId = "CHECK_001",
    CommandType = CommandType.CheckPallet,
    SourceLocation = new Location 
    { 
        Floor = 1, 
        Rail = 2, 
        Block = 3, 
        Depth = 1  // Bắt buộc cho CheckPallet
    },
    // Không cần DestinationLocation cho CheckPallet
};

var result = await gateway.SendCommand(checkTask);
```

### Execution Flow
1. **Write Parameters**: Source Location (Floor, Rail, Block, Depth)
2. **Trigger Command**: Set `Req_CheckPallet` flag
3. **Start Process**: Set `StartProcess` flag
4. **Wait for Result**: Poll các flags:
   - `ErrorAlarm` - Nếu true → **Fail ngay** (bỏ qua FailOnAlarm)
   - `CommandFailed` - Nếu true → Fail
   - `Done_CheckPallet` - Completion flag
   - `AvailablePallet` - Pallet có tại vị trí
   - `UnavailablePallet` - Không có pallet

### Result Processing
```csharp
gateway.TaskSucceeded += (s, e) =>
{
    if (e.Result.CommandType == CommandType.CheckPallet)
    {
        if (e.Result.PalletAvailable)
        {
            Console.WriteLine("✅ Pallet found at location");
            // Có thể tiếp tục với Outbound/Transfer
        }
        else if (e.Result.PalletUnavailable)
        {
            Console.WriteLine("❌ No pallet at location");
            // Xử lý trường hợp không có pallet
        }
    }
};
```

### Đặc điểm quan trọng

**1. Alarm Behavior - Khác biệt với các command khác:**
- ⚠️ CheckPallet **LUÔN FAIL** khi có alarm
- ❌ **BỎ QUA** cấu hình `FailOnAlarm`
- 🎯 **Lý do**: CheckPallet là validation step, không thể tiếp tục khi có lỗi

**2. Device Capabilities:**
```csharp
public class DeviceCapabilities
{
    public bool SupportsCheckPallet { get; init; } = true;
    // Mặc định là true, có thể tắt cho devices không hỗ trợ
}
```

**3. Signal Map Requirements:**
```csharp
public class SignalMap
{
    // CheckPallet specific signals
    public string PalletCheckTrigger { get; init; } = "DB1.DBX0.7";
    public string PalletCheckCompleted { get; init; } = "DB2.DBX0.7";
    public string AvailablePallet { get; init; } = "DB2.DBX1.0";
    public string UnavailablePallet { get; init; } = "DB2.DBX1.1";
}
```

### Use Cases

1. **Pre-validation trước Outbound:**
   ```csharp
   // 1. Check pallet existence
   await gateway.SendCommand(checkTask);
   
   // 2. Nếu available, thực hiện outbound
   if (palletFound)
   {
       await gateway.SendCommand(outboundTask);
   }
   ```

2. **Inventory verification:**
   - Kiểm tra tồn kho thực tế
   - So sánh với database
   - Phát hiện mismatch

3. **Safety check:**
   - Đảm bảo không có pallet trước khi inbound
   - Validate empty slot trước transfer

### Migration Notes

**Nếu bạn đang implement CheckPallet logic riêng:**
1. Xóa custom check logic
2. Sử dụng `CommandType.CheckPallet`
3. Xử lý `PalletAvailable`/`PalletUnavailable` trong event
4. Cấu hình `SignalMap` cho PLC signals

**PLC Requirements:**
- PLC phải implement các signals CheckPallet
- Response time khuyến nghị: < 5 giây
- Timeout mặc định: 30 giây (configurable)

---

## 5) Điều chỉnh mã nguồn nhanh

1. **Khởi tạo**: thay constructor `AutomationGatewayBase(devices, config)` bằng `AutomationGateway.Instance.Initialize(...)`.
2. **Barcode**: đổi gọi `SendValidationResult(deviceId, ...)` thành `SendValidationResult(taskId, ...)` và truyền `destinationLocation`, `gateNumber` khi chấp nhận.
   - ⏱️ **Lưu ý**: Timeout tăng từ 2 phút lên **5 phút**
3. **Batch**: thay `List<TransportTask>` thành `IEnumerable<TransportTask>` và xử lý `SubmissionResult` trả về.
4. **Queue**: nếu cần hủy lệnh pending theo ID, dùng `RemoveCommand(commandId)`.
5. **Layout**: nạp layout bằng `LoadWarehouseLayout` trước khi gửi lệnh để hệ thống tự validate vị trí.
6. **Mode/Recovery**: dùng `SwitchModeAsync` và `ResetDeviceStatusAsync` thay thao tác thủ công ở tầng thấp.
7. **CheckPallet**: Sử dụng `CommandType.CheckPallet` thay vì custom logic:
   ```csharp
   // Gửi check command
   var checkTask = new TransportTask
   {
       TaskId = "CHECK_001",
       CommandType = CommandType.CheckPallet,
       SourceLocation = new Location { Floor = 1, Rail = 2, Block = 3, Depth = 1 }
   };
   await gateway.SendCommand(checkTask);
   
   // Xử lý kết quả
   gateway.TaskSucceeded += (s, e) =>
   {
       if (e.Result.CommandType == CommandType.CheckPallet)
       {
           bool palletExists = e.Result.PalletAvailable;
           // Xử lý logic dựa trên kết quả
       }
   };
   ```
8. **Alarm Handling**: Đăng ký event `TaskAlarm` và cấu hình `FailOnAlarm` theo nhu cầu:
   ```csharp
   gateway.TaskAlarm += (s, e) => {
       Logger.Log($"Alarm on {e.DeviceId} during {e.CommandId}");
   };
   
   // Config
   new PlcConnectionOptions {
       FailOnAlarm = false  // hoặc true tùy use case
   }
   ```

## 6) Ghi chú tương thích
- Các enum `CommandType`, `DeviceStatus`, `Direction` giữ nguyên ý nghĩa, nhưng validation đã chuyển sang `AutomationGateway` và `WarehouseLayout`.
- **CommandType.CheckPallet mới**: Kiểm tra pallet tại vị trí, trả về `PalletAvailable`/`PalletUnavailable`
- Event model thống nhất qua `AutomationGateway` thay vì trải trên `BarcodeHandler/TaskDispatcher`.
- **TaskAlarm event mới**: Phải đăng ký để nhận thông báo alarm ngay lập tức
- **FailOnAlarm config**: Mặc định `false` (continue mode), cần set `true` cho critical operations
- **CheckPallet exception**: Luôn fail khi có alarm, bỏ qua `FailOnAlarm` setting
- **Barcode timeout**: Tăng từ 2 phút lên 5 phút để có thời gian xử lý validation đủ

## 7) Breaking Changes Summary

⚠️ **Các thay đổi BREAKING:**

1. **SendValidationResult signature thay đổi**
   - Loại bỏ tham số `deviceId`
   - `direction` và `gateNumber` giờ là optional/nullable
   - Trả về `bool` thay vì `void`

2. **SendMultipleCommands return type**
   - Thay đổi từ `Task` → `Task<SubmissionResult>`
   - Cần xử lý result để biết tasks nào bị reject

3. **CommandType enum mới**
   - Thêm `CommandType.CheckPallet`
   - Cần cập nhật switch/case xử lý CommandType nếu có

4. **Event mới: TaskAlarm**
   - Phải đăng ký event handler nếu cần theo dõi alarm
   - Alarm behavior phụ thuộc vào `FailOnAlarm` config

5. **Timeout thay đổi**
   - Barcode validation timeout: 2 phút → **5 phút**
   - Cần review logic timeout trong client code

6. **Result properties mới**
   - `PalletAvailable` và `PalletUnavailable` trong `CommandResult`
   - Chỉ áp dụng cho `CheckPallet` commands
