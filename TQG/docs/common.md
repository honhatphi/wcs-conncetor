# 1. Tổng quan

**Automation Gateway** là *facade* trung tâm để ứng dụng tương tác với hệ thống PLC nhiều thiết bị:
- Đăng ký và quản lý kết nối thiết bị.
- Orchestrate hàng đợi lệnh theo thiết bị.
- Xử lý INBOUND có pipeline xác thực barcode.
- Cung cấp sự kiện thành công/thất bại/alarm.
- Hỗ trợ chuyển **Real/Simulation** runtime.
- Hướng Clean Architecture: tách I/O PLC, Orchestrator, Layout, và Event model.
