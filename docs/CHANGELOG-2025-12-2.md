# Changelog - Tháng 12/2025

## Ngày cập nhật: 15/12/2025

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

5. **Điều chỉnh phương thức lấy task đang thực thi**
   - Cũ: `string? GetCurrentTask(string deviceId)` - Trả về 1 TaskId hoặc null
   - Mới: `string[] GetCurrentTasks(string deviceId)` - Trả về mảng TaskId của tất cả lệnh đang thực thi

6. **Thêm `SlotId` vào các Event Args**
   - `TaskSucceededEventArgs`: Thêm `SlotId` để phân biệt slot thực thi
   - `TaskFailedEventArgs`: Thêm `SlotId` để phân biệt slot thực thi
   - `TaskAlarmEventArgs`: Thêm `SlotId` để phân biệt slot xảy ra alarm
   - Giá trị tham chiếu từ `SlotConfiguration.SlotId`

---

> **Note:** Không có thay đổi cách sử dụng hàm hiện tại.
