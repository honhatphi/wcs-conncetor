
# 5. Mã lỗi và Exception

## 5.1 Nguyên tắc
- Public API ném Exception theo ngữ cảnh; tầng ứng dụng **map** sang mã lỗi chuẩn hóa.
- Logging chi tiết ở server

## 5.2 Exception chính
- `PlcConnectionFailedException` — thiết bị không tìm thấy/offline/link chưa sẵn sàng/PLC không phản hồi.
- `TimeoutException` — thao tác PLC quá hạn.
- `InvalidOperationException` — sai trạng thái hệ thống (chưa Initialize, Busy nhưng reset,...).
- `ArgumentException` / `ArgumentNullException` — tham số không hợp lệ.

## 5.3 Bảng mã lỗi chuẩn hóa (application)
| Code | Exception gốc | Mô tả | Hành động |
|------|----------------|------|-----------|
| AGW-1001 | PlcConnectionFailedException | Thiết bị không sẵn sàng | Activate hoặc kiểm tra kết nối |
| AGW-1002 | TimeoutException | Hết thời gian chờ | Tăng timeout/kiểm tra PLC |
| AGW-1003 | InvalidOperationException | Trạng thái không hợp lệ | Kiểm tra IsInitialized, IsPauseQueue, Busy |
| AGW-1004 | ArgumentException | Tham số không hợp lệ | Bổ sung/hiệu chỉnh tham số |
| AGW-2001 | ValidationTimeout | Barcode validation quá hạn | Cho phép retry hoặc huỷ |

---

## 5.4 PLC Error Code Mapper

### 5.4.1 Bảng thông điệp lỗi PLC

| Code | Message |
|---:|---|
| 1 | Shuttle Lift Time Over |
| 2 | Speed not set |
| 3 | Shuttle Stop Time Over |
| 4 | Shuttle Not Matching Block |
| 5 | Encountered an obstacle while changing lanes |
| 6 | Floor mismatch |
| 7 | Target location does not match |
| 8 | Shuttle not in Elevator |
| 9 | Shuttle lost connection |
| 10 | Pallet input location is full |
| 11 | RFID reader connection lost |
| 12 | Pallet not detected at pick location |
| 13 | Elevator Stop Time Over |
| 14 | Elevator coordinate limit exceeded |
| 15 | Warning: Pallet not meeting requirements |
| 16 | Conveyor Lift Motor Error |
| 17 | Gate No. 1 opening/closing time over |
| 18 | Gate No. 2 opening/closing time over |
| 19 | No Pallet Detected on Elevator |
| 20 | Invalid input/output location |
| 21 | Can't control Shuttle |
| 22 | Check position: Shuttle is not in correct position |
| 23 | Shuttle not on Elevator |
| 24 | Manual mode: required floor must be current floor |
| 25 | Inverter Error |
| 26 | Elevator reaches travel limit |
| 27 | QR code reader timeout |
| 28 | Timeout stop Conveyor 6 & 4 or 8 & 7 |
| 29 | QR Code read error |
| 30 | Cannot start OUTBOUND while INBOUND is in progress |
| 31 | Cannot start INBOUND while OUTBOUND is in progress |
| 100 | Emergency stop |
| 101 | Shuttle Servo_1 Alarm |
| 102 | Shuttle Servo_2 Alarm |

### 5.4.2 Gợi ý tích hợp với sự kiện TaskFailed
```csharp
gw.TaskFailed += (_, e) =>
{
    // e.ErrorCode lấy từ PLC (nếu có)
    var humanReadable = PlcErrorCodeMapper.GetMessage(e.ErrorCode ?? -1);
    Console.WriteLine($"FAIL: {e.TaskId} - {humanReadable} (code={{e.ErrorCode}})");
};
```
