# Changelog

All notable changes to TQG Automation SDK will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- **Logging System** - Hệ thống logging toàn diện với các tính năng:
  - `ILogger` interface với các mức log: Debug, Information, Warning, Error, Critical
  - `FileLogger` implementation với file rotation và size management
  - `LogLevel` enum để kiểm soát mức độ chi tiết của logs
  - `LoggerConfiguration` với predefined configs (Default, Development, Production, Silent)
  - Hỗ trợ multiple output destinations (Console, File, Debug)
  - Tùy chỉnh format log (timestamp, component name)
  - Automatic log file rotation based on size and retention policy

### Changed
- **AutomationGateway Architecture** - Cải tiến kiến trúc tổng thể:
  - Chuyển sang Singleton pattern với `AutomationGateway.Instance`
  - Tích hợp `FileLogger` cho tất cả components
  - Cải thiện logging trong initialization và lifecycle methods
  - Enhanced error messages và diagnostic information

- **API Simplification** - Đơn giản hóa API surface:
  - `GetDeviceStatusAsync()` trả về `DeviceStatus` enum thay vì complex object
  - Loại bỏ các intermediate models không cần thiết
  - Cải thiện exception messages và error handling
  - Consistent naming conventions cho tất cả async methods

### Improved
- **Documentation** - Cập nhật toàn diện documentation:
  - README.md mới phản ánh đúng kiến trúc hiện tại
  - Chi tiết về Logging System và configuration
  - Thêm nhiều code examples thực tế
  - Best practices và troubleshooting guides
  - Performance tuning recommendations

- **Error Handling** - Cải thiện error reporting:
  - Clearer exception messages với context information
  - Better validation và early failure detection
  - Improved logging around error scenarios
  - Structured error information trong events

### Technical Details

#### Logging System Implementation
```
TQG.Automation.SDK/Logging/
├── ILogger.cs                 # Core logging interface
├── FileLogger.cs              # File-based logger với rotation
├── LogLevel.cs                # Log level enumeration
└── LoggerConfiguration.cs     # Configuration model với presets
```

**Key Features:**
- Thread-safe file writing với locking mechanism
- Automatic log file rotation khi đạt max size
- Configurable retention policy (số files giữ lại)
- Support cho console, file, và debug output
- Flexible formatting với timestamp và component name
- Predefined configurations cho different environments

#### AutomationGateway Changes

**Before:**
```csharp
var gateway = new AutomationGateway(devices, config);
```

**After:**
```csharp
var gateway = AutomationGateway.Instance;
gateway.Initialize(configurations);
```

**Benefits:**
- Single instance đảm bảo resource management tốt hơn
- Thread-safe initialization
- Centralized logging configuration
- Better lifecycle management

#### DeviceStatus Simplification

**Before:**
```csharp
var status = await gateway.GetDeviceStatusAsync("device1");
// Returns complex object with multiple properties
```

**After:**
```csharp
var status = await gateway.GetDeviceStatusAsync("device1");
// Returns DeviceStatus enum: Idle, Busy, Error, Offline
```

**Status Determination Logic:**
1. Check ErrorAlarm flag → `Error`
2. Check active command → `Busy`
3. Check device ready → `Idle` or `Error`

### Migration Guide

#### For Existing Code Using Old API

1. **Update to Singleton Pattern:**
```csharp
// Old
var gateway = new AutomationGateway(devices, config);

// New
var gateway = AutomationGateway.Instance;
gateway.Initialize(configurations);
```

2. **Update Device Status Handling:**
```csharp
// Old
var status = await gateway.GetDeviceStatusAsync("device1");
if (status.IsReady) { ... }

// New
var status = await gateway.GetDeviceStatusAsync("device1");
if (status == DeviceStatus.Idle) { ... }
```

3. **Configure Logging:**
```csharp
// Add to initialization
var loggerConfig = LoggerConfiguration.Production;
// Logger is now automatically integrated
```

4. **Update Event Handlers:**
```csharp
// TaskAlarm event is now available
gateway.TaskAlarm += (s, e) => {
    Console.WriteLine($"Alarm on {e.DeviceId}: {e.CommandId}");
};
```

### Breaking Changes

⚠️ **Important:** This release contains breaking changes

1. **Constructor Changes:**
   - `AutomationGateway` constructor is now private
   - Must use `AutomationGateway.Instance` instead

2. **Return Type Changes:**
   - `GetDeviceStatusAsync()` returns `DeviceStatus` enum instead of object
   - Update any code that checks status properties

3. **Configuration Changes:**
   - New `LoggerConfiguration` required for logging setup
   - Update initialization code to include logging config

### Deprecations

- ❌ `new AutomationGateway()` - Use `AutomationGateway.Instance`
- ❌ Complex status objects - Use `DeviceStatus` enum

### Known Issues

- None reported yet

### Dependencies

- .NET 8.0 or higher
- No new external dependencies added

## [Previous Version]

### Initial Release
- PLC communication với Siemens S7
- Command orchestration system
- Event-based task notifications
- Multi-device management
- TCP emulator cho testing
- Warehouse layout validation

---

## Version History

### Version Numbering
- **Major**: Breaking changes
- **Minor**: New features (backward compatible)
- **Patch**: Bug fixes và improvements

### Support Policy
- Latest major version: Full support
- Previous major version: Security updates only
- Older versions: No support

---

**Note:** Để xem thêm chi tiết về các thay đổi, vui lòng tham khảo commit history hoặc liên hệ development team.
