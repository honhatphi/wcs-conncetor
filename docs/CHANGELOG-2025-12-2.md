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
| 1001 | Shuttle Lift Time Over | Extended - Shuttle nâng quá thời gian |
| 1002 | Speed not set | Extended - Chưa cài đặt tốc độ |
| 1003 | Shuttle Stop Time Over | Extended - Shuttle dừng quá thời gian |
| 1004 | Shuttle Not Matching Block | Extended - Shuttle không khớp block |
| 1005 | Encountered an obstacle while changing lanes | Extended - Gặp vật cản khi chuyển làn |
| 1006 | Floor mismatch | Extended - Không khớp tầng |
| 1007 | Target location does not match | Extended - Vị trí đích không khớp |
| 1008 | Shuttle not in Elevator | Extended - Shuttle không trong thang máy |
| 1009 | Shuttle lost connection | Extended - Shuttle mất kết nối |
| 1010 | Pallet input location is full | Extended - Vị trí nhập pallet đã đầy |
| 1011 | RFID reader connection lost | Extended - Mất kết nối đầu đọc RFID |
| 1012 | Pallet not detected Location to be picked | Extended - Không phát hiện pallet tại vị trí lấy |
| 1101 | Shuttle Servo_1 Alarm | Extended - Báo động Servo 1 |
| 1102 | Shuttle Servo_2 Alarm | Extended - Báo động Servo 2 |

---

> **Note:** Không có thay đổi cách sử dụng hàm hiện tại.
