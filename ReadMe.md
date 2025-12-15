# TQG Automation SDK

**.NET 8.0 SDK for PLC-based Warehouse Automation**

---

## Giới thiệu

TQG Automation SDK là thư viện .NET 8.0 để xây dựng hệ thống điều khiển kho tự động (WCS - Warehouse Control System). SDK cung cấp các thành phần để tích hợp với thiết bị PLC Siemens S7 và các thiết bị tương thích, hỗ trợ quản lý lệnh vận chuyển, xác thực barcode, và xử lý alarm.

### Tính năng chính

- **PLC Integration**: Kết nối và điều khiển Siemens S7 PLC (hoặc Emulated mode)
- **Command Orchestration**: Hệ thống điều phối lệnh với channel-based architecture
- **Barcode Validation**: Quy trình xác thực barcode với timeout 5 phút
- **Alarm Handling**: Phát hiện và xử lý alarm với 2 chế độ (Continue/Fail Fast)
- **Event-Driven**: Callback events cho task lifecycle (Success, Failed, Alarm, BarcodeReceived)
- **Logging System**: File-based logging với 3 preset configurations
- **Thread-Safe**: Singleton pattern với async/await patterns

---

## Yêu cầu hệ thống

- **.NET 8.0 SDK** hoặc cao hơn
- **Visual Studio 2022** hoặc VS Code + C# extension
- **Windows 10/11** hoặc Linux (với .NET 8.0 runtime)
- **Network access** đến PLC devices (cho Real mode)

---

## Cấu trúc project

```
TQG.Automation.SDK/
├── AutomationGateway.cs              # Singleton entry point - Public API
├── TQG.Automation.SDK.csproj         # Project file (.NET 8.0)
│
├── Clients/                           # PLC client implementations
│   ├── S7PlcClient.cs                 # Siemens S7 protocol client
│   ├── TcpEmulatedPlcClient.cs        # TCP emulator for testing
│   └── PlcClientExtensions.cs         # Extension methods
│
├── Core/                              # Core abstractions
│   ├── IPlcClient.cs                  # PLC client interface
│   ├── PlcAddress.cs                  # Address parsing utilities
│   ├── PlcErrorCodeMapper.cs          # Error code to message mapping
│   └── PlcMode.cs                     # Real/Emulated mode enum
│
├── Orchestration/                     # Command processing engine
│   ├── Core/
│   │   └── CommandOrchestrator.cs     # Main orchestrator
│   ├── Executors/                     # Command executors by type
│   │   ├── InboundExecutor.cs         # Inbound + barcode validation
│   │   ├── OutboundExecutor.cs        # Outbound flow
│   │   ├── TransferExecutor.cs        # Transfer flow
│   │   ├── CheckExecutor.cs           # CheckPallet flow
│   │   ├── Base/                      # Base executor classes
│   │   └── Strategies/                # Executor strategies
│   ├── Workers/                       # Background workers
│   │   ├── DeviceWorker.cs            # Device-specific task queue
│   │   ├── Matchmaker.cs              # Command-Device matching + dispatch rules
│   │   └── ReplyHub.cs                # PLC feedback handler
│   ├── Models/                        # Internal models
│   │   ├── CommandEnvelope.cs         # Internal command wrapper
│   │   ├── CommandResult.cs           # Execution result
│   │   ├── ExecutionStatus.cs         # Status enum
│   │   └── SignalMonitorContext.cs    # Signal monitoring
│   ├── Infrastructure/                # Internal utilities
│   │   ├── OrchestratorChannels.cs    # Channel definitions
│   │   ├── PendingCommandTracker.cs   # In-flight command tracking
│   │   └── AsyncManualResetEvent.cs   # Async synchronization
│   └── Services/
│       └── SignalMonitorService.cs    # PLC signal monitoring
│
├── Configuration/                     # Configuration models
│   ├── PlcGatewayConfiguration.cs     # Gateway config
│   └── WarehouseLayout.cs             # Warehouse layout validation
│
├── Management/                        # PLC connection management
│   ├── PlcConnectionManager.cs        # Connection lifecycle
│   ├── PlcRegistry.cs                 # Device registry
│   └── PlcClientFactory.cs            # Client factory
│
├── Events/                            # Event args for callbacks
│   ├── TaskSucceededEventArgs.cs
│   ├── TaskFailedEventArgs.cs
│   ├── TaskAlarmEventArgs.cs
│   └── BarcodeReceivedEventArgs.cs
│
├── Exceptions/                        # Custom exceptions
│   ├── PlcException.cs                # Base exception
│   ├── PlcConnectionFailedException.cs
│   ├── PlcDataFormatException.cs
│   ├── PlcInvalidAddressException.cs
│   └── TimeoutException.cs
│
├── Shared/                            # Public DTOs & models
│   ├── TransportTask.cs               # Command request model
│   ├── SubmissionResult.cs            # Submission response
│   ├── CommandType.cs                 # Inbound/Outbound/Transfer/CheckPallet
│   ├── CommandStatus.cs               # Success/Failed/Error
│   ├── DeviceStatus.cs                # Idle/Busy/Error
│   ├── DeviceStatistics.cs            # Device statistics
│   ├── Location.cs                    # Warehouse location
│   ├── Direction.cs                   # Enter/Exit direction
│   ├── PlcConnectionOptions.cs        # Connection config
│   ├── SignalMap.cs                   # PLC signal addresses
│   └── ErrorDetail.cs                 # Error information
│
└── Logging/                           # Logging system
    ├── ILogger.cs                     # Logger interface
    ├── FileLogger.cs                  # File-based implementation
    ├── LogLevel.cs                    # Log levels
    └── LoggerConfiguration.cs         # Logger config
```

---

## Cách build project

### 1. Clone repository

```bash
git clone https://github.com/honhatphi/wcs-conncetor.git
cd wcs-conncetor/TQG
```

### 2. Restore dependencies

```bash
dotnet restore
```

### 3. Build solution

```bash
# Debug build
dotnet build

# Release build
dotnet build -c Release
```
---

## Development setup

### Visual Studio 2022

1. Mở `TQG.sln`
2. Set startup project (nếu có test/sample project)
3. Build solution (Ctrl+Shift+B)
4. Start debugging (F5)

### VS Code

1. Mở folder `TQG/`
2. Install C# extension (ms-dotnettools.csharp)
3. `Ctrl+Shift+P` → ".NET: Generate Assets for Build and Debug"
4. F5 để debug

---

## Kiến trúc tổng quan

### 1. Entry Point: AutomationGateway (Singleton)

```csharp
var gateway = AutomationGateway.Instance;
await gateway.InitializeAsync(plcOptions);
```

### 2. Command Flow

```
WCS/WMS
   ↓
AutomationGateway.SendCommand()
   ↓
ValidateCommandRequest (data validation only)
   ↓
CommandOrchestrator.SubmitCommandAsync()
   ↓
Matchmaker (dispatch rules + device matching)
   ↓
DeviceWorker
   ↓
Executor (Inbound/Outbound/Transfer/CheckPallet)
   ↓
PlcClient (S7 or Emulated)
   ↓
PLC Device
```

### 3. Dispatch Rules (Matchmaker)

| Đang xử lý | Lệnh cần gửi | Kết quả |
|------------|--------------|---------|
| Transfer/CheckPallet | Bất kỳ | ❌ Chờ |
| Bất kỳ | Transfer/CheckPallet | ❌ Chờ |
| Inbound | Outbound | ❌ Chờ |
| Outbound | Inbound | ❌ Chờ |
| Inbound | Inbound | ✅ Cho phép |
| Outbound | Outbound | ✅ Cho phép |
| Không có | Bất kỳ | ✅ Cho phép |

> **Note**: Delay 2 giây giữa các lệnh liên tiếp (lệnh đầu tiên không delay)

### 4. Event Flow

```
PLC Device → ReplyHub → Executor → AutomationGateway
   ↓
TaskSucceeded / TaskFailed / TaskAlarm / BarcodeReceived
   ↓
WCS/WMS Event Handlers
```

### 5. Key Design Patterns

- **Singleton**: AutomationGateway, PlcRegistry
- **Factory**: PlcClientFactory
- **Strategy**: Executors (InboundExecutor, OutboundExecutor, etc.)
- **Observer**: Event-based communication
- **Channel-based**: System.Threading.Channels for async queues

---

## Documentation

- **[API Usage Guide](./docs/README.md)**: Hướng dẫn sử dụng API chi tiết với examples
- **[CHANGELOG.md](./docs/CHANGELOG.md)**: Lịch sử thay đổi API
- **[CHANGELOG-2025-12.md](./docs/CHANGELOG-2025-12.md)**: Changelog tháng 12/2025
- **[CHANGELOG-2025-12-2.md](./docs/CHANGELOG-2025-12-2.md)**: Changelog tuần 2 tháng 12/2025
- **[flows.md](./docs/flows.md)**: Sequence diagrams cho các luồng xử lý
- **[errors.md](./docs/errors.md)**: Danh sách mã lỗi PLC

---

## License

Copyright © 2025 TQG Automation. All rights reserved.

---
