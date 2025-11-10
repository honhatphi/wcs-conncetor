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

## 3.3 Error Recovery & Alarm Handling

Khi phát hiện alarm (`ErrorAlarm = true`) trong quá trình thực thi, hệ thống xử lý theo flow sau:

### 3.3.1 Recovery Flow - FailOnAlarm = false (Continue Mode)

```mermaid
sequenceDiagram
    participant App as Phần mềm
    participant GW as AutomationGateway
    participant Dispatcher as TaskDispatcher
    participant Monitor as DeviceMonitor
    participant HMI as Nhân viên vận hành
    
    Note over GW,Monitor: Task đang thực thi bình thường
    
    Monitor->>GW: Phát hiện ErrorAlarm = true
    GW->>App: TaskAlarm event (Thông báo alarm)
    
    alt FailOnAlarm = true (Fail Fast)
        Note over GW,Dispatcher: Xử lý lỗi ngay lập tức
        GW->>Dispatcher: PauseQueue() (Tạm dừng)
        GW->>Dispatcher: RemoveTask() (Loại bỏ task)
        GW->>App: TaskFailed event (Báo lỗi)
        
        rect rgb(255, 230, 230)
            Note over App,HMI: Recovery Process
            App->>HMI: Thông báo lỗi cần khắc phục
            HMI->>Monitor: Xử lý lỗi (log, thông báo, etc.)
            HMI->>Monitor: Khắc phục sự cố thủ công
            HMI->>Monitor: ResetDeviceStatus() (Cập nhật HMI)
        end
        
        HMI->>Monitor: Báo hoàn tất khắc phục
        Monitor->>GW: Thiết bị sẵn sàng
        App->>GW: ResumeQueue() để tiếp tục
        
    else FailOnAlarm = false (Continue Mode - Default)
        Note over GW,Dispatcher: Tiếp tục thực thi, chờ kết quả
        GW->>Dispatcher: [Lưi xảy ra] (Không xóa task)
        
        rect rgb(255, 255, 230)
            Note over App,HMI: Recovery Process (Non-blocking)
            App->>HMI: Thông báo alarm (Warning)
            HMI->>Monitor: Theo dõi tình huống
        end
        
        alt PLC tự khắc phục và hoàn thành
            Monitor->>GW: Completed flag = true
            GW->>App: TaskSucceeded (Warning status)
            Note over App: Task hoàn thành với cảnh báo
            
        else PLC không khắc phục được
            HMI->>Monitor: Khắc phục thủ công
            HMI->>Monitor: Cập nhật kết quả ở HMI
            
            alt Khắc phục thành công
                HMI->>Monitor: Set Completed flag
                Monitor->>GW: Completed flag = true
                GW->>App: TaskSucceeded (Warning status)
            else Không khắc phục được
                HMI->>Monitor: Set Failed flag
                Monitor->>GW: Failed flag = true
                GW->>App: TaskFailed event
            end
        end
    end
```

### 3.3.2 Workflow Chi Tiết

#### Khi FailOnAlarm = true (Fail Fast Mode):

1. **Phát hiện Alarm**:
   - `DeviceMonitor` phát hiện `ErrorAlarm = true`
   - Raise `TaskAlarm` event ngay lập tức
   - **Dừng ngay** và raise `TaskFailed` event

2. **Recovery Actions**:
   - System tự động gọi `PauseQueue()` (tạm dừng queue)
   - Xóa task khỏi queue bằng `RemoveTask()`
   - Thông báo lỗi cho nhân viên vận hành

3. **Manual Intervention**:
   - Nhân viên vận hành khắc phục sự cố
   - Cập nhật trạng thái thiết bị tại HMI
   - Gọi `ResetDeviceStatus()` khi hoàn tất

4. **Resume**:
   - App gọi `ResumeQueue()` để tiếp tục xử lý

#### Khi FailOnAlarm = false (Continue Mode - Default):

1. **Phát hiện Alarm**:
   - `DeviceMonitor` phát hiện `ErrorAlarm = true`
   - Raise `TaskAlarm` event (thông báo warning)
   - **Tiếp tục chờ** kết quả từ PLC

2. **Parallel Recovery**:
   - Task vẫn tiếp tục thực thi
   - Nhân viên được thông báo để theo dõi
   - PLC có thể tự khắc phục hoặc cần can thiệp

3. **Outcome Scenarios**:
   
   **a) PLC tự recovery thành công:**
   - PLC set `Completed = true`
   - Raise `TaskSucceeded` với `Warning` status
   - Task hoàn thành bình thường
   
   **b) Cần can thiệp thủ công:**
   - Nhân viên khắc phục tại HMI
   - Cập nhật cờ `Completed` hoặc `Failed`
   - System nhận kết quả và raise event tương ứng

### 3.3.3 So Sánh Hai Chế Độ

| Tiêu chí | FailOnAlarm = true | FailOnAlarm = false |
|----------|-------------------|---------------------|
| **Phản ứng** | Fail ngay lập tức | Tiếp tục chờ kết quả |
| **Queue** | Pause tự động | Không ảnh hưởng |
| **Recovery** | Blocking (phải xử lý xong mới tiếp tục) | Non-blocking (parallel) |
| **Use Case** | Critical operations, safety-first | Non-critical, cho phép retry |
| **Task Status** | Failed immediately | Succeeded (Warning) hoặc Failed |
| **Manual Intervention** | Bắt buộc trước khi resume | Optional, chỉ khi PLC không tự recovery |

### 3.3.4 Best Practices

**Khuyến nghị sử dụng FailOnAlarm = true khi:**
- ✅ Operations ảnh hưởng an toàn
- ✅ Không thể chấp nhận lỗi (critical path)
- ✅ Cần can thiệp ngay lập tức
- ✅ Ví dụ: Check pallet, Safety gates

**Khuyến nghị sử dụng FailOnAlarm = false khi:**
- ✅ Operations có thể retry/recovery
- ✅ PLC có khả năng tự khắc phục
- ✅ Không muốn block toàn bộ queue
- ✅ Ví dụ: Transfer tasks, Outbound operations

**Lưu ý đặc biệt:**
- 🔔 `TaskAlarm` event **luôn được raise** trong cả hai mode
- 🚫 `CheckPallet` command **luôn fail** khi có alarm (bỏ qua cấu hình)
- ⚠️ Alarm chỉ notify **một lần** để tránh spam
- 🔄 Có thể thay đổi `FailOnAlarm` runtime bằng `SwitchModeAsync()`

---

## 3.4 Pause/Resume Queue
```mermaid
flowchart LR
  A[PauseQueue] -->|IsPauseQueue = true| ORC[Orchestrator]
  ORC --> P[Pending commands giữ nguyên]
  R[ResumeQueue] -->|IsPauseQueue = false| ORC
```

## 3.5 SwitchMode Runtime
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
