# 6. Cấu hình (chuẩn hóa theo yêu cầu)

Tài liệu này quy định **định dạng cấu hình chính thức** cho Automation Gateway. Bao gồm:
- Cấu hình PLC Gateway (`plcConnections`).
- Cấu hình Layout kho (`blocks`, `disabledLocations`).

---

## 6.1 PLC Gateway Configuration

### 6.1.1 Cấu trúc tổng quát
```json
{
  "plcConnections": [
    {
      "deviceId": "string",
      "ipAddress": "IPv4",
      "rack": 0,
      "slot": 1,
      "port": 102,
      "mode": "Real | Simulation",
      "connectTimeout": "HH:mm:ss",
      "operationTimeout": "HH:mm:ss",
      "healthCheckInterval": "HH:mm:ss",
      "maxReconnectAttempts": 5,
      "reconnectBaseDelay": "HH:mm:ss",
      "stopOnAlarm": false,
      "commandTimeout": "HH:mm:ss",
      "autoRecoveryEnabled": true,
      "recoveryPollInterval": "HH:mm:ss",
      "signalMap": {
        "...": "DB.."
      }
    }
  ]
}
```

### 6.1.2 Ràng buộc và quy tắc
- **deviceId**: bắt buộc, duy nhất trong toàn bộ cấu hình.
- **ipAddress**: IPv4 hợp lệ.
- **rack/slot/port**: số nguyên không âm. `port` mặc định 102 nếu không chỉ định.
- **mode**: `Real` hoặc `Simulation`.
- **connectTimeout/operationTimeout/commandTimeout**: định dạng `HH:mm:ss`.
- **healthCheckInterval/reconnectBaseDelay/recoveryPollInterval**: định dạng `HH:mm:ss`.
- **maxReconnectAttempts**: số nguyên ≥ 0.
- **autoRecoveryEnabled**: nếu `true`, worker tự kiểm tra điều kiện recover theo `recoveryPollInterval`.
- **stopOnAlarm**: nếu `true`, gặp Alarm sẽ dừng lệnh theo chính sách worker.
- **signalMap**: địa chỉ thanh ghi theo chuẩn Siemens S7. *Không mô tả ý nghĩa các thanh ghi*.

### 6.1.3 Ví dụ cấu hình đầy đủ (chuẩn)
```json
{
  "plcConnections": [
    {
      "deviceId": "Shuttle01",
      "ipAddress": "192.168.4.102",
      "rack": 0,
      "slot": 1,
      "port": 102,
      "mode": "Real",
      "connectTimeout": "00:00:10",
      "operationTimeout": "00:00:10",
      "healthCheckInterval": "00:00:30",
      "maxReconnectAttempts": 5,
      "reconnectBaseDelay": "00:00:02",
      "stopOnAlarm": false,
      "commandTimeout": "00:15:00",
      "autoRecoveryEnabled": false,
      "recoveryPollInterval": "00:00:05",
      "signalMap": {
        "DeviceReady": "DB33.DBX52.0",
        "SoftwareConnected": "DB33.DBX52.1",
        "CommandFailed": "DB33.DBX52.6",
        "InboundTrigger": "DB33.DBX0.0",
        "OutboundTrigger": "DB33.DBX0.1",
        "TransferTrigger": "DB33.DBX0.2",
        "PalletCheckTrigger": "DB33.DBX52.2",
        "StartProcess": "DB33.DBX0.3",
        "CommandAccepted": "DB33.DBX0.4",
        "CommandRejected": "DB33.DBX0.5",
        "InboundCompleted": "DB33.DBX0.6",
        "OutboundCompleted": "DB33.DBX0.7",
        "TransferCompleted": "DB33.DBX1.0",
        "PalletCheckCompleted": "DB88.DBX52.3",
        "AvailablePallet": "DB33.DBX52.4",
        "UnavailablePallet": "DB88.DBX52.5",
        "SourceFloor": "DB33.DBW50",
        "SourceRail": "DB33.DBW6",
        "SourceBlock": "DB33.DBW8",
        "SourceDepth": "DB33.DBW56.0",
        "TargetFloor": "DB33.DBW10",
        "TargetRail": "DB33.DBW12",
        "TargetBlock": "DB33.DBW14",
        "CurrentFloor": "DB33.DBW22",
        "CurrentRail": "DB33.DBW24",
        "CurrentBlock": "DB33.DBW26",
        "CurrentDepth": "DB33.DBW54.0",
        "GateNumber": "DB33.DBW2",
        "ExitDirection": "DB33.DBX1.2",
        "EnterDirection": "DB33.DBX1.3",
        "BarcodeValid": "DB33.DBX20.0",
        "BarcodeInvalid": "DB33.DBX20.1",
        "ErrorAlarm": "DB33.DBX1.1",
        "ErrorCode": "DB33.DBW28",
        "BarcodeChar1": "DB33.DBW30",
        "BarcodeChar2": "DB33.DBW32",
        "BarcodeChar3": "DB33.DBW34",
        "BarcodeChar4": "DB33.DBW36",
        "BarcodeChar5": "DB33.DBW38",
        "BarcodeChar6": "DB33.DBW40",
        "BarcodeChar7": "DB33.DBW42",
        "BarcodeChar8": "DB33.DBW44",
        "BarcodeChar9": "DB33.DBW46",
        "BarcodeChar10": "DB33.DBW48"
      }
    },
    {
      "deviceId": "Shuttle02",
      "ipAddress": "192.168.4.102",
      "rack": 0,
      "slot": 1,
      "port": 102,
      "mode": "Real",
      "connectTimeout": "00:00:10",
      "operationTimeout": "00:00:10",
      "healthCheckInterval": "00:00:30",
      "maxReconnectAttempts": 5,
      "reconnectBaseDelay": "00:00:02",
      "stopOnAlarm": false,
      "commandTimeout": "00:15:00",
      "autoRecoveryEnabled": true,
      "recoveryPollInterval": "00:00:03",
      "signalMap": {
        "DeviceReady": "DB88.DBX52.0",
        "SoftwareConnected": "DB88.DBX52.1",
        "CommandFailed": "DB88.DBX52.6",
        "InboundTrigger": "DB88.DBX0.0",
        "OutboundTrigger": "DB88.DBX0.1",
        "TransferTrigger": "DB88.DBX0.2",
        "PalletCheckTrigger": "DB88.DBX52.2",
        "StartProcess": "DB88.DBX0.3",
        "CommandAccepted": "DB88.DBX0.4",
        "CommandRejected": "DB88.DBX0.5",
        "InboundCompleted": "DB88.DBX0.6",
        "OutboundCompleted": "DB88.DBX0.7",
        "TransferCompleted": "DB88.DBX1.0",
        "PalletCheckCompleted": "DB88.DBX52.3",
        "AvailablePallet": "DB88.DBX52.4",
        "UnavailablePallet": "DB88.DBX52.5",
        "SourceFloor": "DB88.DBW50",
        "SourceRail": "DB88.DBW6",
        "SourceBlock": "DB88.DBW8",
        "SourceDepth": "DB88.DBW56.0",
        "TargetFloor": "DB88.DBW10",
        "TargetRail": "DB88.DBW12",
        "TargetBlock": "DB88.DBW14",
        "CurrentFloor": "DB88.DBW22",
        "CurrentRail": "DB88.DBW24",
        "CurrentBlock": "DB88.DBW26",
        "CurrentDepth": "DB88.DBW54.0",
        "GateNumber": "DB88.DBW2",
        "ExitDirection": "DB88.DBX1.2",
        "EnterDirection": "DB88.DBX1.3",
        "BarcodeValid": "DB88.DBX20.0",
        "BarcodeInvalid": "DB88.DBX20.1",
        "ErrorAlarm": "DB88.DBX1.1",
        "ErrorCode": "DB88.DBW28",
        "BarcodeChar1": "DB88.DBW30",
        "BarcodeChar2": "DB88.DBW32",
        "BarcodeChar3": "DB88.DBW34",
        "BarcodeChar4": "DB88.DBW36",
        "BarcodeChar5": "DB88.DBW38",
        "BarcodeChar6": "DB88.DBW40",
        "BarcodeChar7": "DB88.DBW42",
        "BarcodeChar8": "DB88.DBW44",
        "BarcodeChar9": "DB88.DBW46",
        "BarcodeChar10": "DB88.DBW48"
      }
    }
  ]
}
```

### 6.1.4 Tải cấu hình vào Gateway
```csharp
var json = File.ReadAllText("plc-config.json");
var gw = AutomationGateway.Instance;
gw.Initialize(json);
```

---

## 6.2 Warehouse Layout Configuration

### 6.2.1 Cấu trúc tổng quát
```json
{
  "blocks": [
    {
      "blockNumber": 3,
      "maxFloor": 7,
      "maxRail": 24,
      "maxDepth": 8
    }
  ],
  "disabledLocations": [
    {
      "floor": 7,
      "rail": 4,
      "block": 5,
      "depth": 5
    }
  ]
}
```

### 6.2.2 Ví dụ cấu hình đầy đủ (chuẩn)
```json
{
  "blocks": [
    {
      "blockNumber": 3,
      "maxFloor": 7,
      "maxRail": 24,
      "maxDepth": 8
    },
    {
      "blockNumber": 5,
      "maxFloor": 7,
      "maxRail": 24,
      "maxDepth": 3
    }
  ],
  "disabledLocations": [
    {
      "floor": 7,
      "rail": 4,
      "block": 5,
      "depth": 5
    },
    {
      "floor": 7,
      "rail": 4,
      "block": 5,
      "depth": 4
    },
    {
      "floor": 7,
      "rail": 5,
      "block": 5,
      "depth": 5
    },
    {
      "floor": 7,
      "rail": 5,
      "block": 5,
      "depth": 4
    },
    {
      "floor": 7,
      "rail": 6,
      "block": 5,
      "depth": 5
    },
    {
      "floor": 7,
      "rail": 6,
      "block": 5,
      "depth": 4
    },
    {
      "floor": 7,
      "rail": 7,
      "block": 5,
      "depth": 5
    },
    {
      "floor": 7,
      "rail": 7,
      "block": 5,
      "depth": 4
    }
  ]
}
```

### 6.2.3 Tải Layout vào Gateway
```csharp
var layoutJson = File.ReadAllText("warehouse-layout.json");
var gw = AutomationGateway.Instance;
gw.LoadWarehouseLayout(layoutJson);
```

---

## 6.3 Checklist hợp lệ
- Không trùng `deviceId` trong `plcConnections`.
- Tham số thời gian theo chuẩn `HH:mm:ss`.
- `mode` hợp lệ: `Real` hoặc `Simulation`.
- Các địa chỉ trong `signalMap` theo đúng định dạng S7.
- Các vị trí bị vô hiệu hóa phải nằm trong phạm vi các `blocks` tương ứng.
