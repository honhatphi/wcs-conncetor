# Changelog API – Automation Gateway (December 2025)

Tài liệu ghi nhận các thay đổi trong tháng 12/2025.

---

## 1) Tổng quan thay đổi

- **Giảm Poll Interval**: Từ 500ms xuống 200ms giữa mỗi lần poll tín hiệu PLC
- **Tái cấu trúc InboundExecutor**: Triển khai cơ chế Signal Monitor chạy song song
- **Cải thiện cơ chế kiểm tra lỗi**: Chuyển từ check `ErrorAlarm` flag sang check trực tiếp `ErrorCode`
- **Bổ sung thông tin execution steps** trong log và event notification

---

## 2) Chi tiết thay đổi

### 2.1 Poll Interval Configuration

| Thông số | Cũ | Mới | Ghi chú |
|----------|-----|-----|---------|
| Poll Interval | 500ms | 200ms | Giảm 60% thời gian giữa mỗi lần poll |

**Áp dụng cho các Executor:**
- `CheckExecutor` - Kiểm tra pallet
- `InboundExecutor` - Nhập hàng  
- `OutboundExecutor` - Xuất hàng
- `TransferExecutor` - Di chuyển nội bộ

**Lý do thay đổi:**
- Phản hồi nhanh hơn khi phát hiện tín hiệu hoàn thành/lỗi
- Giảm độ trễ trong việc detect alarm
- Cải thiện trải nghiệm người dùng với feedback realtime hơn

### 2.2 Cơ chế Signal Monitor mới (InboundExecutor)

**Cũ:**
- Kiểm tra tín hiệu lỗi tuần tự trong từng bước thực thi
- Check `ErrorAlarm` flag trước, sau đó mới đọc `ErrorCode`
- Logic xử lý alarm nằm rải rác trong các method

**Mới:**
- Triển khai **Signal Monitor** chạy song song với execution flow
- Sử dụng `CancellationTokenSource` liên kết để cancel execution khi detect terminating signal
- Định nghĩa rõ ràng các loại tín hiệu qua `SignalType` enum:
  - `None` - Không có tín hiệu
  - `Alarm` - Phát hiện lỗi
  - `CommandFailed` - Lệnh thất bại
  - `InboundCompleted` - Hoàn thành nhập hàng

```csharp
internal enum SignalType
{
    None,
    Alarm,
    CommandFailed,
    InboundCompleted
}

internal sealed record SignalMonitorResult
{
    public required SignalType Type { get; init; }
    public ErrorDetail? Error { get; init; }
    public DateTimeOffset DetectedAt { get; init; }
}
```

**Ưu điểm:**
- Phát hiện tín hiệu dừng/lỗi ngay lập tức trong quá trình execution
- Không bị block bởi các bước khác (đọc barcode, validate, ...)
- Code structure rõ ràng, dễ maintain

### 2.3 Thay đổi cơ chế kiểm tra lỗi

**Lý do:**
- `ErrorCode = 0` đồng nghĩa không có lỗi, không cần check `ErrorAlarm` flag
- Giảm số lần gọi PLC (từ 2 xuống 1 cho mỗi lần check)
- Đơn giản hóa logic, dễ debug

### 2.4 Bổ sung Execution Steps trong notification

**Thay đổi trong `CommandResultNotification`:**
```csharp
public sealed record CommandResultNotification
{
    // ... existing properties
    
    /// <summary>
    /// Optional: PLC-specific data returned during execution.
    /// Contains execution steps for debugging/monitoring.
    /// </summary>
    public object? Data { get; init; }  // NEW
}
```

**Thay đổi trong logging:**
```csharp
// Cũ
_logger.LogWarning($"EVENT: TaskAlarm raised - TaskId: {id}, DeviceId: {device}");

// Mới - bao gồm execution steps
_logger.LogWarning($"EVENT: TaskAlarm raised - TaskId: {id}," +
    $" DeviceId: {device}," +
    $" ErrorCode: {error.ErrorCode}," +
    $" ErrorMessage: {error.ErrorMessage}," +
    $" Steps: {notification.Data}");
```

## 4) Migration Guide

### 4.1 Không có breaking changes

Các thay đổi hoàn toàn internal, không ảnh hưởng đến public API.

### 4.2 Tận dụng execution steps mới

```csharp
gateway.TaskFailed += (sender, args) =>
{
    // args.Data giờ chứa execution steps
    Logger.Error($"Task {args.CommandId} failed");
    
    // Log chi tiết các bước đã thực hiện
    if (args is CommandResultNotification notification && notification.Data != null)
    {
        Logger.Debug($"Execution steps: {notification.Data}");
    }
};
```

---

## 5) Tác động hiệu năng

| Metric | Trước | Sau | Cải thiện |
|--------|-------|-----|-----------|
| Signal Detection Latency | ~500ms | ~200ms | 60% faster |
| Alarm Response Time | Sequential | Parallel | Realtime |
| PLC Calls per Alarm Check | 2 | 1 | 50% reduction |

---

## 7) Phiên bản áp dụng

- **Version**: 2.1.0
- **Release Date**: December 2025
- **Backward Compatible**: ✅ Hoàn toàn tương thích ngược

