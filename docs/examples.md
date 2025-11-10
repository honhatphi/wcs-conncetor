# 7. Ví dụ sử dụng

> Toàn bộ ví dụ sử dụng **AutomationGateway.Instance**. Đăng ký sự kiện trước khi kích hoạt.

## 8.1 Bootstrap khởi tạo từ JSON + đăng ký sự kiện
```csharp
var gw = AutomationGateway.Instance;

// 1) Initialize từ JSON
var json = File.ReadAllText("plc-config.json");
gw.Initialize(json);

// 2) Đăng ký sự kiện trước khi activate
gw.TaskSucceeded += (_, e) => Console.WriteLine($"OK: {e.TaskId} on {e.DeviceId}");
gw.TaskFailed +=   (_, e) => Console.WriteLine($"FAIL: {e.TaskId} - {e.Reason}");
gw.TaskAlarm +=    (_, e) => Console.WriteLine($"ALARM: {e.DeviceId} - {e.Detail}");
gw.BarcodeReceived += async (_, e) => {
    // Ví dụ: gọi dịch vụ xác thực của bạn
    var isValid = await MyService.ValidateBarcodeAsync(e.Barcode);
    if (isValid)
    {
        await gw.SendValidationResult(
            e.TaskId,
            isValid: true,
            destinationLocation: new Location(1,2,3),
            direction: Direction.Bottom,
            gateNumber: 5);
    }
    else
    {
        await gw.SendValidationResult(e.TaskId, isValid: false);
    }
};

// 3) Activate từng thiết bị hoặc tất cả
await gw.ActivateDevice("Shuttle01");

// Hoặc:
await gw.ActivateAllDevicesAsync();
```

## 8.2 Kiểm tra trạng thái & vị trí
```csharp
bool link = gw.IsConnected("Shuttle01");
var status = await gw.GetDeviceStatusAsync("Shuttle01");
var loc = await gw.GetActualLocationAsync("Shuttle01");
Console.WriteLine($"Connected={link}, Status={status}, Location={loc}");
```

## 8.3 Gửi lệnh OUTBOUND
```csharp
await gw.SendCommand(new TransportTask{
  TaskId = Guid.NewGuid().ToString("N"),
  CommandType = CommandType.Outbound,
  DeviceId = "Shuttle01",          // nếu null → auto-assign
  SourceLocation = new Location(1,1,3),
  GateNumber = 4,
  OutDirBlock = Direction.Top,
});
```

## 8.4 Gửi lệnh TRANSFER
```csharp
await gw.SendCommand(new TransportTask{
  TaskId = Guid.NewGuid().ToString("N"),
  CommandType = CommandType.Transfer,
  DeviceId = null, // để auto-assign theo thiết bị rảnh
  SourceLocation = new Location(2,5,3),
  TargetLocation = new Location(2,9,3),
  OutDirBlock = Direction.Bottom
});
```

## 8.5 Gửi nhiều lệnh và đọc SubmissionResult
```csharp
var batch = new List<TransportTask>
{
  new() { TaskId = "t1", CommandType = CommandType.Outbound, SourceLocation = new(1,1,3), GateNumber = 2 },
  new() { TaskId = "t2", CommandType = CommandType.Transfer, SourceLocation = new(1,2,3), TargetLocation = new(1,8,3) },
  new() { TaskId = "t3", CommandType = CommandType.Inbound } // Inbound sẽ chờ barcode validation
};

var result = await gw.SendMultipleCommands(batch);
Console.WriteLine($"Submitted={result.Submitted}, Rejected={result.Rejected}");
foreach (var rej in result.RejectedCommands)
    Console.WriteLine($"Reject: {rej.Request.TaskId} - {rej.Reason}");
```

## 8.6 INBOUND với barcode – xử lý validation
```csharp
// 1) Gửi lệnh inbound
await gw.SendCommand(new TransportTask{
  TaskId = "in-001",
  CommandType = CommandType.Inbound,
  InDirBlock = Direction.Bottom
});

// 2) Trong handler BarcodeReceived đã đăng ký ở 8.1
// → Gọi SendValidationResult(...) trong vòng 2 phút.
```

## 8.7 Điều khiển hàng đợi
```csharp
gw.PauseQueue();
// … thực hiện thao tác quản trị queue nếu cần
gw.ResumeQueue();

bool paused = gw.IsPauseQueue;
bool removed = gw.RemoveCommand("t1");
// Nếu có API xóa nhiều lệnh:
int count = gw.RemoveCommands(new [] { "t2", "t3" });
```

## 8.8 Switch sang Simulation và quay lại Real
```csharp
await gw.SwitchModeAsync("Shuttle01", PlcMode.Simulation);
// … chạy kiểm thử
await gw.SwitchModeAsync("Shuttle01", PlcMode.Real);
```

## 8.9 Reset thiết bị lỗi
```csharp
var resetOk = await gw.ResetDeviceStatusAsync("Shuttle01");
```

## 8.10 Load Layout kho
```csharp
var layoutJson = File.ReadAllText("warehouse-layout.json");
gw.LoadWarehouseLayout(layoutJson);

var layout = gw.GetWarehouseLayout();
Console.WriteLine(layout.ToString());
```

## 8.11 Best practices
- Đăng ký sự kiện **trước** khi Activate để không mất event sớm.
- Inbound validate: khi `isValid = true` bắt buộc `destinationLocation` và `gateNumber`.
- Không reset thiết bị khi Busy.
- Map Exception → mã lỗi chuẩn hoá (xem `errors.md`) và log chi tiết.
- Đặt timeout hợp lý theo loại lệnh, barcode 5 phút.
- Không retry mù với lệnh di chuyển; chỉ retry thao tác đọc an toàn.
