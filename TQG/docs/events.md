# 4. Sự kiện

## TaskSucceeded
- Khi lệnh hoàn tất thành công.
- Args: TaskId, DeviceId, CommandType, StartedAt, CompletedAt.

## TaskFailed
- Khi lệnh thất bại hoặc bị reject.
- Args: TaskId, DeviceId, ErrorDetail.

## TaskAlarm
- Khi phát hiện Alarm trong khi thực thi.
- Args: TaskId, DeviceId, ErrorDetail.
- Thông tin tức thời; quyết định fail/continue phụ thuộc cấu hình.

## BarcodeReceived
- Khi PLC đã quét xong barcode trong INBOUND và yêu cầu xác thực.
- Ứng dụng phải gọi `SendValidationResult` trong 5 phút.
