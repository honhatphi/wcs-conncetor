# Changelog - Tháng 12/2025 (Phần cập nhật)

### Cập nhật

1. **Hiển thị cảnh báo quá kích thước khi nhập hàng**
   - Mã lỗi 15: "Warning Pallet not meeting requirements"

2. **Thêm cảnh báo pallet có ở băng tải xuất**
   - Mã lỗi 32: "Warning Pallet in Conveyor 5"

3. **Khắc phục lỗi phần mềm không nhận được tín hiệu báo hoàn thành từ PLC**
   - Giảm thời gian chờ kết quả của DLL từ 1s xuống còn 200ms
   - Tăng thời gian chờ đọc kết quả của HMI

4. **Loại bỏ cơ chế ràng buộc gửi lệnh nhập/xuất**
   - Đã loại bỏ cơ chế ràng buộc không gửi được lệnh nhập khi xuất và ngược lại khi gửi lệnh.
   - Việc ràng buộc sẽ được DLL tự xử lý khi phân phối lệnh đến thiết bị

5. **Bổ sung cơ chế Device Error State**
   - Khi bất kỳ slot nào gặp lỗi (Failed/Timeout), toàn bộ device sẽ bị khóa
   - Các slot khác của cùng device không thể nhận lệnh mới cho đến khi recovery hoàn tất
   - Bộ phân bổ lệnh kiểm tra trạng thái device trước khi phân phối

6. **Bổ sung logging cho quá trình Recovery**
   - Thêm log Warning/Info trong `WaitForManualRecoveryAsync` và `WaitForAutoRecoveryAsync`
   - Giúp người vận hành biết chính xác trạng thái recovery và lý do thất bại
   - **Khi nào cần xem log:** Sau khi xử lý lỗi và gọi `ResetDeviceStatusAsync()` nhưng không thấy thiết bị tiếp tục thực hiện lệnh mới

7. **Bổ sung logging cho bộ phân bổ lệnh**
   - Log khi lệnh được phân bổ thành công đến slot
   - Log khi lệnh không thể phân bổ (do alarm, lỗi thiết bị, xung đột lệnh...)
   - Log khi không tìm được slot phù hợp
   - **Mục đích:** Tra cứu nguyên nhân khi lệnh không được thực thi như mong đợi

8. **Bổ sung logging cho bộ xử lý kết quả**
   - Log khi lệnh hoàn thành (Success/Failed/Timeout)
   - Log khi phát hiện alarm từ thiết bị
   - Log khi hệ thống shutdown và số lượng kết quả còn lại được xử lý
   - **Mục đích:** Theo dõi vòng đời lệnh và trạng thái alarm

9. **Bổ sung logging cho bộ giám sát tín hiệu**
   - Log khi bắt đầu giám sát lệnh
   - Log khi phát hiện alarm (ErrorCode != 0)
   - Log khi lệnh thành công hoặc thất bại
   - Log thời gian thực thi cho từng sự kiện
   - **Mục đích:** Theo dõi chi tiết quá trình thực thi lệnh và phát hiện lỗi

10. **Hỗ trợ cấu hình logging qua JSON**
   - Thêm section `logging` trong file cấu hình JSON
   - Cho phép cấu hình: `minimumLevel`, `enableFileOutput`, `enableDebugOutput`
   - Mặc định: `minimumLevel = Information`
   - Để bật Debug logging: đặt `minimumLevel = "Debug"` và `enableDebugOutput = true`

### Cấu hình Logging qua JSON

```json
{
  "logging": {
    "minimumLevel": "Debug",
    "enableFileOutput": true,
    "enableDebugOutput": true,
    "includeTimestamp": true,
    "includeComponentName": true
  },
  "plcConnections": [...]
}
```

**Các giá trị `minimumLevel`:**
- `Debug` - Log chi tiết (bao gồm thông tin phân bổ lệnh)
- `Information` - Log thông tin chung (mặc định)
- `Warning` - Chỉ log cảnh báo và lỗi
- `Error` - Chỉ log lỗi
- `Critical` - Chỉ log lỗi nghiêm trọng

### Quy trình Recovery

1. Reset thiết bị vật lý: Nhấn nút reset trên HMI/thiết bị để xóa lỗi
2. Kiểm tra DeviceReady: Đảm bảo đèn Ready sáng trên HMI
3. Gọi `ResetDeviceStatusAsync()`: Phần mềm kích hoạt recovery
4. Kiểm tra log: Xác nhận "Manual recovery successful"


### Mã lỗi mới được thêm

| Code | Message | Mô tả |
|------|---------|-------|
| 32 | Warning Pallet in Conveyor 5 | Cảnh báo pallet trên băng tải xuất |

### Quy tắc phân phối lệnh

| Đang xử lý | Lệnh cần gửi | Kết quả |
|------------|--------------|---------|
| **Chuyển** | Bất kỳ | ❌ Chờ |
| Bất kỳ | **Chuyển** | ❌ Chờ |
| Nhập | Nhập | ✅ Cho phép |
| Nhập | Xuất | ❌ Chờ |
| Xuất | Xuất | ✅ Cho phép |
| Xuất | Nhập | ❌ Chờ |
| Không có | Bất kỳ | ✅ Cho phép |

**Tóm tắt:**
- **Chuyển** là lệnh đơn, không chạy cùng với lệnh khác.
- **Nhập/Xuất** không được chạy đồng thời nhưng có thể chạy song song cùng loại (2 lệnh Nhập hoặc 2 lệnh Xuất)

### AutomationGateway

8. **Điều chỉnh phương thức lấy task đang thực thi**
   - Cũ: `string? GetCurrentTask(string deviceId)` - Trả về 1 TaskId hoặc null
   - Mới: `string[] GetCurrentTasks(string deviceId)` - Trả về mảng TaskId của tất cả lệnh đang thực thi

9. **Thêm `SlotId` vào các Event Args**
   - `TaskSucceededEventArgs`: Thêm `SlotId` để phân biệt slot thực thi
   - `TaskFailedEventArgs`: Thêm `SlotId` để phân biệt slot thực thi
   - `TaskAlarmEventArgs`: Thêm `SlotId` để phân biệt slot xảy ra alarm
   - Giá trị tham chiếu từ `SlotConfiguration.SlotId`

**Lưu ý quan trọng:**
- Phải reset thiết bị **trước** khi gọi `ResetDeviceStatusAsync()`
- Nếu recovery thất bại, kiểm tra log để biết nguyên nhân và thử lại
- Trong khi chờ recovery, tất cả slot của device bị khóa không nhận lệnh mới

---

> **Note:** Không có thay đổi cách sử dụng hàm hiện tại.
