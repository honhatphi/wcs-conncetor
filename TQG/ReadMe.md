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
├── AutomationGateway.cs              # Singleton entry point
├── Clients/                           # PLC client implementations
│   ├── IPlcClient.cs
│   ├── S7PlcClient.cs                 # Siemens S7 protocol
│   └── TcpEmulatedPlcClient.cs        # TCP emulator
├── Orchestration/                     # Command processing engine
│   ├── Core/
│   │   └── CommandOrchestrator.cs     # Main orchestrator
│   ├── Executors/                     # Command executors
│   │   ├── InboundExecutor.cs         # Inbound + barcode validation
│   │   ├── OutboundExecutor.cs        # Outbound flow
│   │   ├── TransferExecutor.cs        # Transfer flow
│   │   └── CheckExecutor.cs           # Check flow
│   ├── Workers/                       # Background workers
│   │   ├── DeviceWorker.cs            # Device-specific task queue
│   │   ├── Matchmaker.cs              # Command-Device matching
│   │   └── ReplyHub.cs                # PLC feedback handler
│   └── Infrastructure/                # Internal utilities
│       ├── OrchestratorChannels.cs    # Channel definitions
│       └── PendingCommandTracker.cs   # In-flight command tracking
├── Logging/                           # Logging system
│   ├── ILogger.cs
│   ├── FileLogger.cs
│   ├── LogLevel.cs
│   └── LoggerConfiguration.cs
├── Management/                        # PLC connection management
│   ├── PlcConnectionManager.cs
│   ├── PlcRegistry.cs
│   └── PlcClientFactory.cs
├── Events/                            # Event args
│   ├── TaskSucceededEventArgs.cs
│   ├── TaskFailedEventArgs.cs
│   ├── TaskAlarmEventArgs.cs
│   └── BarcodeReceivedEventArgs.cs
├── Shared/                            # Public models
│   ├── TransportTask.cs
│   ├── SubmissionResult.cs
│   ├── CommandType.cs
│   ├── DeviceStatus.cs
│   └── PlcConnectionOptions.cs
└── Configuration/
    ├── WarehouseLayout.cs
    └── PlcGatewayConfiguration.cs
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
CommandOrchestrator → Matchmaker → DeviceWorker
   ↓
Executor (Inbound/Outbound/Transfer/Check)
   ↓
PlcClient (S7 or Emulated)
   ↓
PLC Device
```

### 3. Event Flow

```
PLC Device → ReplyHub → Executor → AutomationGateway
   ↓
TaskSucceeded / TaskFailed / TaskAlarm / BarcodeReceived
   ↓
WCS/WMS Event Handlers
```

### 4. Key Design Patterns

- **Singleton**: AutomationGateway, PlcRegistry
- **Factory**: PlcClientFactory
- **Strategy**: Executors (InboundExecutor, OutboundExecutor, etc.)
- **Observer**: Event-based communication
- **Channel-based**: System.Threading.Channels for async queues

---

## Documentation

- **[API Usage Guide](./docs/README.md)**: Hướng dẫn sử dụng API chi tiết với examples
- **[CHANGELOG.md](./docs/CHANGELOG.md)**: So sánh API cũ vs API mới
- **[flows.md](./docs/flows.md)**: Sequence diagrams cho các luồng xử lý

---

## License

Copyright © 2025 TQG Automation. All rights reserved.

---
