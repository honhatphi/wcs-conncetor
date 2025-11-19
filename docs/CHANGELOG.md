# Changelog API – Automation Gateway
Tài liệu này ghi nhận thay đổi public API giữa **AutomationGatewayBase** (cũ) và **AutomationGateway** (mới).

## 1) Tổng quan thay đổi
- Chuẩn hoá **facade duy nhất**: `AutomationGateway.Instance`.
- Bổ sung **khởi tạo từ JSON** và **tải layout kho**.
- Đơn giản hoá **SendValidationResult**: bỏ `deviceId`, thêm `optional direction`, `nullable gate`.
- Thêm **ResetDeviceStatusAsync** có ràng buộc trạng thái.
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
- `Task<SubmissionResult> SendMultipleCommands(IEnumerable<TransportTask> tasks)`
  - Trả về `SubmissionResult` với thông tin `Submitted`, `Rejected`, `RejectedCommands`
  - Validate toàn bộ tasks trước khi submit
  - Tasks không hợp lệ được reject với lý do cụ thể

**Cũ**
- `Task SendCommand(TransportTask task)`
  - Không có return value (void)
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

### 2.6 Phục hồi thiết bị
**Mới**
- `Task<bool> ResetDeviceStatusAsync(string deviceId)` — chặn khi Busy, raise recovery orchestration

**Cũ**
- Khả năng reset/monitor phân tán theo `DeviceMonitor`, không có API centralized.

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
| Barcode Validation | `SendValidationResult(deviceId, taskId, ...)` | `SendValidationResult(taskId, ...) : bool` | Bỏ deviceId, timeout 5 phút |
| Alarm Handling | Không có event | `TaskAlarm` event | Phát hiện alarm ngay lập tức |
| Layout | Không có | `LoadWarehouseLayout(json)`, `GetWarehouseLayout()` | Validate vị trí kho |
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

## 5) Điều chỉnh mã nguồn nhanh

1. **Khởi tạo**: thay constructor `AutomationGatewayBase(devices, config)` bằng `AutomationGateway.Instance.Initialize(...)`.
2. **Barcode**: đổi gọi `SendValidationResult(deviceId, ...)` thành `SendValidationResult(taskId, ...)` và truyền `destinationLocation`, `gateNumber` khi chấp nhận.
   - ⏱️ **Lưu ý**: Timeout tăng từ 2 phút lên **5 phút**
3. **Batch**: thay `List<TransportTask>` thành `IEnumerable<TransportTask>` và xử lý `SubmissionResult` trả về.
4. **Queue**: nếu cần hủy lệnh pending theo ID, dùng `RemoveCommand(commandId)`.
5. **Layout**: nạp layout bằng `LoadWarehouseLayout` trước khi gửi lệnh để hệ thống tự validate vị trí.
6. **Recovery**: dùng `ResetDeviceStatusAsync` để phục hồi thiết bị lỗi thay thao tác thủ công ở tầng thấp.
7. **Alarm Handling**: Đăng ký event `TaskAlarm` và cấu hình `FailOnAlarm` theo nhu cầu:
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
- Event model thống nhất qua `AutomationGateway` thay vì trải trên `BarcodeHandler/TaskDispatcher`.
- **TaskAlarm event mới**: Phải đăng ký để nhận thông báo alarm ngay lập tức
- **FailOnAlarm config**: Mặc định `false` (continue mode), cần set `true` cho critical operations
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

3. **Event mới: TaskAlarm**
   - Phải đăng ký event handler nếu cần theo dõi alarm
   - Alarm behavior phụ thuộc vào `FailOnAlarm` config