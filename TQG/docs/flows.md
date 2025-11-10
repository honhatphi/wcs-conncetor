# 3. Luồng xử lý (Flows)

## 3.0 Alarm Handling - FailOnAlarm Configuration

SDK hỗ trợ hai chế độ xử lý alarm thông qua cấu hình `FailOnAlarm` trong `PlcConnectionOptions`:

### FailOnAlarm = true (Fail Fast Mode)
- ⚠️ **TaskAlarm event** được raise ngay khi phát hiện `ErrorAlarm = true`
- ❌ Task **fail ngay lập tức** sau khi raise alarm event
- ⛔ **Không chờ** PLC hoàn thành hoặc set `CommandFailed` flag
- 📋 **Use case**: Critical operations, safety-first scenarios

### FailOnAlarm = false (Continue Mode - Default)
- ⚠️ **TaskAlarm event** được raise ngay khi phát hiện `ErrorAlarm = true`
- ⏳ Task **tiếp tục thực thi** sau khi raise alarm event
- 🔄 Chờ PLC xử lý và kiểm tra flag `Completed` hoặc `CommandFailed`
- ✅ Nếu PLC set `Completed` flag → **TaskSucceeded** với Warning status
- ❌ Nếu PLC set `CommandFailed` flag → **TaskFailed**
- 📋 **Use case**: Non-critical operations, allow PLC to recover

**Lưu ý:** 
- TaskAlarm luôn được raise **trước** TaskSucceeded/TaskFailed
- Alarm notification chỉ raise **một lần** để tránh duplicate
- CheckPallet command luôn fail khi có alarm (bỏ qua FailOnAlarm setting)

## 3.1 Outbound/Transfer – Tổng quát
```mermaid
sequenceDiagram
  participant App as Client App
  participant GW as AutomationGateway
  participant ORC as Orchestrator
  participant W as Device Worker
  participant PLC as PLC

  App->>GW: SendCommand(task)
  GW->>ORC: Validate + Enqueue(envelope)
  ORC->>W: Assign when device Idle
  W->>PLC: Write command + params
  PLC-->>W: Status progress
  
  alt Alarm Detected (ErrorAlarm = true)
    W-->>GW: TaskAlarm (immediate notification)
    GW-->>App: TaskAlarm event
    
    alt FailOnAlarm = true
      W-->>GW: TaskFailed
      GW-->>App: TaskFailed event
    else FailOnAlarm = false
      Note over W,PLC: Continue execution despite alarm
      alt Command Completed (Completed flag)
        W-->>GW: TaskSucceeded (Warning status)
        GW-->>App: TaskSucceeded event
      else Command Failed (Failed flag)
        W-->>GW: TaskFailed
        GW-->>App: TaskFailed event
      end
    end
  else No Alarm
    alt Success
      W-->>GW: TaskSucceeded
      GW-->>App: TaskSucceeded event
    else Fail
      W-->>GW: TaskFailed
      GW-->>App: TaskFailed event
    end
  end
```

## 3.2 Inbound với Barcode Validation
```mermaid
sequenceDiagram
  participant App as Client App
  participant GW as AutomationGateway
  participant ORC as Orchestrator
  participant W as Inbound Worker
  participant PLC as PLC

  App->>GW: SendCommand(inbound)
  GW->>ORC: Enqueue
  ORC->>W: Assign when device Idle
  W->>PLC: Start scan
  PLC-->>GW: Barcode read → BarcodeReceived
  GW-->>App: BarcodeReceived event
  App->>GW: SendValidationResult(taskId, isValid[, dest, dir, gate])
  
  alt isValid == true
    GW->>W: Deliver validation (dest + gate + dir)
    W->>PLC: Write validation flags + parameters
    W->>PLC: Continue execution
    
    alt Alarm Detected (ErrorAlarm = true)
      W-->>GW: TaskAlarm (immediate notification)
      GW-->>App: TaskAlarm event
      
      alt FailOnAlarm = true
        W-->>GW: TaskFailed
        GW-->>App: TaskFailed event
      else FailOnAlarm = false
        Note over W,PLC: Continue execution despite alarm
        alt Command Completed (InboundCompleted flag)
          W-->>GW: TaskSucceeded (Warning status)
          GW-->>App: TaskSucceeded event
        else Command Failed (Failed flag)
          W-->>GW: TaskFailed
          GW-->>App: TaskFailed event
        end
      end
    else No Alarm
      alt Success
        PLC-->>W: InboundCompleted
        W-->>GW: TaskSucceeded
        GW-->>App: TaskSucceeded event
      else Fail
        PLC-->>W: CommandFailed
        W-->>GW: TaskFailed
        GW-->>App: TaskFailed event
      end
    end
  else isValid == false OR Timeout (5 minutes)
    W->>PLC: Write rejection flags
    W-->>GW: TaskFailed
    GW-->>App: TaskFailed event
  end
```

## 3.3 Pause/Resume Queue
```mermaid
flowchart LR
  A[PauseQueue] -->|IsPauseQueue = true| ORC[Orchestrator]
  ORC --> P[Pending commands giữ nguyên]
  R[ResumeQueue] -->|IsPauseQueue = false| ORC
```

## 3.4 SwitchMode Runtime
```mermaid
sequenceDiagram
  participant App
  participant GW
  participant REG as Registry
  participant MGR as Old Manager
  participant MGR2 as New Manager

  App->>GW: SwitchModeAsync(device, Real/Simulation)
  GW->>REG: Get Manager(device)
  REG-->>GW: MGR
  GW->>MGR: Disconnect
  GW->>REG: Replace with MGR2(new mode)
  GW->>MGR2: Connect + Verify link
  GW-->>App: Done
```
