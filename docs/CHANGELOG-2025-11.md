
## Breaking Changes Summary

⚠️ **API Removals:**
- `Task SwitchModeAsync(string deviceId, PlcMode newMode, CancellationToken ct)` - **REMOVED**

✅ **New Behaviors:**
- Device deactivation stops all background services

📋 **Event Changes:**
- `TaskAlarmEventArgs` now includes `DeviceId` property

---
