using System.Threading.Channels;
using TQG.Automation.SDK.Logging;
using TQG.Automation.SDK.Orchestration.Infrastructure;
using TQG.Automation.SDK.Orchestration.Models;
using TQG.Automation.SDK.Shared;

namespace TQG.Automation.SDK.Orchestration.Workers;

/// <summary>
/// Scheduling engine that matches pending commands from InputChannel with available slots
/// from AvailabilityChannel, then writes scheduled commands to slot-specific channels.
/// Implements priority-based scheduling (High before Normal) with FIFO within same priority.
/// </summary>
internal sealed class Matchmaker
{
    private readonly OrchestratorChannels _channels;
    private readonly AsyncManualResetEvent _pauseGate;
    private readonly PendingCommandTracker _tracker;
    private ILogger? _logger;

    /// <summary>
    /// Slot capabilities organized by device and slot.
    /// Key: deviceId, Value: Dictionary&lt;slotId, capabilities&gt;
    /// </summary>
    private readonly Dictionary<string, Dictionary<int, DeviceCapabilities>> _slotCapabilities;

    /// <summary>
    /// Delay between dispatching commands to stagger workload.
    /// Applied between consecutive dispatches regardless of iteration.
    /// </summary>
    private readonly TimeSpan _dispatchStaggerDelay = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Tracks the last dispatch time to enforce delay between consecutive commands.
    /// </summary>
    private DateTimeOffset _lastDispatchTime = DateTimeOffset.MinValue;

    /// <summary>
    /// Tracks last logged block reason to avoid spamming logs in polling loop.
    /// Only logs when reason changes.
    /// </summary>
    private string? _lastLoggedBlockReason;

    public Matchmaker(
        OrchestratorChannels channels,
        AsyncManualResetEvent pauseGate,
        PendingCommandTracker tracker)
    {
        _channels = channels ?? throw new ArgumentNullException(nameof(channels));
        _pauseGate = pauseGate ?? throw new ArgumentNullException(nameof(pauseGate));
        _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
        _slotCapabilities = new Dictionary<string, Dictionary<int, DeviceCapabilities>>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Sets the logger for this matchmaker.
    /// </summary>
    public void SetLogger(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Registers slot capabilities for command matching.
    /// Must be called before RunAsync() to enable capability-based filtering.
    /// </summary>
    /// <param name="deviceId">Device identifier.</param>
    /// <param name="slotId">Slot identifier (1, 2, 3...).</param>
    /// <param name="capabilities">Slot capabilities.</param>
    public void RegisterSlotCapabilities(string deviceId, int slotId, DeviceCapabilities capabilities)
    {
        ArgumentException.ThrowIfNullOrEmpty(deviceId);
        ArgumentNullException.ThrowIfNull(capabilities);

        if (!_slotCapabilities.TryGetValue(deviceId, out var deviceSlots))
        {
            deviceSlots = new Dictionary<int, DeviceCapabilities>();
            _slotCapabilities[deviceId] = deviceSlots;
        }

        deviceSlots[slotId] = capabilities;
    }

    /// <summary>
    /// Registers device capabilities for command matching.
    /// DEPRECATED: Use RegisterSlotCapabilities for multi-slot architecture.
    /// Kept for backward compatibility - treats device as having a single slot (slotId=0).
    /// </summary>
    [Obsolete("Use RegisterSlotCapabilities for multi-slot architecture")]
    public void RegisterDeviceCapabilities(string deviceId, DeviceCapabilities capabilities)
    {
        RegisterSlotCapabilities(deviceId, 0, capabilities);
    }

    /// <summary>
    /// Runs the matchmaker loop: reads commands and availability, matches them, schedules to devices.
    /// Auto-pauses when queue becomes empty to save resources.
    /// Runs until cancellation is requested.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var pendingCommands = new Queue<CommandEnvelope>();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await WaitIfPausedAsync(cancellationToken);

                CollectPendingCommands(pendingCommands);

                if (ShouldAutoPause(pendingCommands))
                {
                    AutoPauseAndWait();
                    await WaitForNewDataAsync(cancellationToken);
                    continue;
                }

                if (HasAvailableDevices())
                {
                    await MatchCommandsToDevicesAsync(pendingCommands, cancellationToken);
                }

                if (ShouldWaitForNewData(pendingCommands))
                {
                    await WaitForNewDataAsync(cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        finally
        {
            RequeueUnmatchedCommands(pendingCommands);
        }
    }

    #region Main Loop Helpers

    /// <summary>
    /// Waits if scheduling is paused via pause gate.
    /// </summary>
    private async Task WaitIfPausedAsync(CancellationToken cancellationToken)
    {
        await _pauseGate.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Collects new commands from input channel and adds valid ones to pending queue.
    /// Skips commands that were removed before scheduling.
    /// </summary>
    private void CollectPendingCommands(Queue<CommandEnvelope> pendingCommands)
    {
        while (_channels.InputChannel.Reader.TryRead(out var command))
        {
            if (IsCommandRemoved(command.CommandId))
                continue;

            pendingCommands.Enqueue(command);
        }
    }

    /// <summary>
    /// Checks if should auto-pause (queue empty).
    /// </summary>
    private static bool ShouldAutoPause(Queue<CommandEnvelope> pendingCommands)
    {
        return pendingCommands.Count == 0;
    }

    /// <summary>
    /// Auto-pauses scheduling until new command arrives.
    /// </summary>
    private void AutoPauseAndWait()
    {
        _pauseGate.Reset();
    }

    /// <summary>
    /// Checks if there are available slots in the channel.
    /// </summary>
    private bool HasAvailableDevices()
    {
        return _channels.AvailabilityChannel.Reader.Count > 0;
    }

    /// <summary>
    /// Checks if should wait for new data.
    /// </summary>
    private static bool ShouldWaitForNewData(Queue<CommandEnvelope> pendingCommands)
    {
        return pendingCommands.Count == 0;
    }

    /// <summary>
    /// Waits for new commands or device availability.
    /// </summary>
    private async Task WaitForNewDataAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.WhenAny(
                _channels.InputChannel.Reader.WaitToReadAsync(cancellationToken).AsTask(),
                _channels.AvailabilityChannel.Reader.WaitToReadAsync(cancellationToken).AsTask(),
                Task.Delay(1000, cancellationToken)
            );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
    }

    /// <summary>
    /// Re-queues unmatched commands back to pending state on shutdown.
    /// </summary>
    private void RequeueUnmatchedCommands(Queue<CommandEnvelope> pendingCommands)
    {
        while (pendingCommands.Count > 0)
        {
            var command = pendingCommands.Dequeue();
            _tracker.MarkAsPending(command.CommandId, command);
        }
    }

    #endregion

    #region Command-Device Matching

    /// <summary>
    /// Matches pending commands with available slots and dispatches them.
    /// Strict FIFO: Commands are processed in order, never skipped.
    /// </summary>
    private async Task MatchCommandsToDevicesAsync(
        Queue<CommandEnvelope> pendingCommands,
        CancellationToken cancellationToken)
    {
        var availableSlots = CollectAvailableSlots();

        await ProcessCommandQueueAsync(pendingCommands, availableSlots, cancellationToken);

        await ReturnUnusedSlotsToPoolAsync(availableSlots, cancellationToken);
    }

    /// <summary>
    /// Collects all currently available slots from availability channel.
    /// Returns list of (DeviceId, SlotId) tuples.
    /// </summary>
    private List<SlotInfo> CollectAvailableSlots()
    {
        var slots = new List<SlotInfo>();
        while (_channels.AvailabilityChannel.Reader.TryRead(out var ticket))
        {
            slots.Add(new SlotInfo(ticket.PlcDeviceId, ticket.SlotId));
        }
        return slots;
    }

    /// <summary>
    /// Processes command queue in strict FIFO order, matching with available slots.
    /// Stops when next command cannot be matched (waits for slot).
    /// </summary>
    private async Task ProcessCommandQueueAsync(
        Queue<CommandEnvelope> pendingCommands,
        List<SlotInfo> availableSlots,
        CancellationToken cancellationToken)
    {
        while (pendingCommands.Count > 0 && availableSlots.Count > 0)
        {
            var command = pendingCommands.Peek();

            if (IsCommandRemoved(command.CommandId))
            {
                pendingCommands.Dequeue();
                continue;
            }

            var matchResult = TryFindMatchingSlot(command, availableSlots);

            if (!matchResult.Found)
            {
                // Required slot not available, STOP and wait
                break;
            }

            // Match found: dequeue command and remove slot from pool
            pendingCommands.Dequeue();
            availableSlots.RemoveAt(matchResult.SlotIndex);

            // Apply stagger delay between consecutive dispatches (across all iterations)
            await ApplyDispatchStaggerDelayAsync(
                command,
                matchResult.DeviceId,
                matchResult.SlotId,
                matchResult.SlotIndex,
                availableSlots,
                cancellationToken).ConfigureAwait(false);

            // Mark and dispatch
            _tracker.MarkAsProcessing(command.CommandId, matchResult.DeviceId);
            await DispatchCommandToSlotAsync(
                command,
                matchResult.DeviceId,
                matchResult.SlotId,
                pendingCommands,
                cancellationToken).ConfigureAwait(false);

            // Update last dispatch time
            _lastDispatchTime = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>
    /// Finds matching slot for a command.
    /// Returns deviceId, slotId, and index if found, otherwise indicates no match.
    /// Considers slot capabilities, device error state, and Inbound/Outbound conflict rules.
    /// Prefers slots in order (Slot 1 before Slot 2) for predictability.
    /// 
    /// Conflict Rules (apply at DEVICE level, across ALL slots):
    /// - Cannot dispatch to any device that is in error state (needs recovery first)
    /// - Cannot dispatch Inbound to any slot if another slot is processing Outbound
    /// - Cannot dispatch Outbound to any slot if another slot is processing Inbound
    /// - Transfer commands only go to slots that support Transfer
    /// </summary>
    private SlotMatchResult TryFindMatchingSlot(
        CommandEnvelope command,
        List<SlotInfo> availableSlots)
    {
        // Check Inbound/Outbound conflict across all devices
        if (!CanDispatchCommandType(command.CommandType, out var blockReason))
        {
            LogBlockReasonOnce($"{command.CommandId}:conflict:{blockReason}",
                $"[Matchmaker] No slot match for {command.CommandId}: {blockReason}");
            return SlotMatchResult.NotFound;
        }

        if (HasDeviceAffinity(command))
        {
            // Device-specific command: must use specified device
            // First check if the specified device is in error state
            if (_tracker.IsDeviceInError(command.PlcDeviceId!))
            {
                LogBlockReasonOnce($"{command.CommandId}:device-error:{command.PlcDeviceId}",
                    $"[Matchmaker] No slot match for {command.CommandId}: target device {command.PlcDeviceId} is in error state");
                return SlotMatchResult.NotFound; // Device in error, cannot dispatch
            }

            // Find slots belonging to the specified device, ordered by SlotId
            var deviceSlots = availableSlots
                .Select((slot, index) => (slot, index))
                .Where(x => x.slot.DeviceId.Equals(command.PlcDeviceId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.slot.SlotId)
                .ToList();

            foreach (var (slot, index) in deviceSlots)
            {
                if (SlotSupportsCommand(slot.DeviceId, slot.SlotId, command.CommandType))
                {
                    ClearBlockReasonLog(); // Found match, clear logged reason
                    return new SlotMatchResult(true, slot.DeviceId, slot.SlotId, index);
                }
            }

            // No compatible slot available on required device - don't log in loop, just return
            return SlotMatchResult.NotFound;
        }
        else
        {
            // Any-device command: find first available slot that supports this command type
            // Order by DeviceId then SlotId for predictability
            // Skip devices that are in error state
            var orderedSlots = availableSlots
                .Select((slot, index) => (slot, index))
                .Where(x => !_tracker.IsDeviceInError(x.slot.DeviceId)) // Filter out devices in error
                .OrderBy(x => x.slot.DeviceId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.slot.SlotId)
                .ToList();

            foreach (var (slot, index) in orderedSlots)
            {
                if (SlotSupportsCommand(slot.DeviceId, slot.SlotId, command.CommandType))
                {
                    ClearBlockReasonLog(); // Found match, clear logged reason
                    return new SlotMatchResult(true, slot.DeviceId, slot.SlotId, index);
                }
            }

            // No compatible slot available - don't log in loop, just return
            return SlotMatchResult.NotFound;
        }
    }

    /// <summary>
    /// Logs block reason only once when it changes. Prevents log spam in polling loop.
    /// </summary>
    private void LogBlockReasonOnce(string reason, string message)
    {
        if (_lastLoggedBlockReason != reason)
        {
            _lastLoggedBlockReason = reason;
            _logger?.LogDebug(message);
        }
    }

    /// <summary>
    /// Clears the last logged block reason. Called when dispatch succeeds.
    /// </summary>
    private void ClearBlockReasonLog()
    {
        _lastLoggedBlockReason = null;
    }

    /// <summary>
    /// Checks if a command type can be dispatched based on current processing state.
    /// Returns block reason via out parameter for logging.
    /// 
    /// Rules:
    /// - If any device has error (Alarm/Failed/Timeout) → cannot dispatch ANY command (must wait for recovery)
    /// - If any device is processing Transfer or CheckPallet → cannot dispatch ANY command (must wait)
    /// - If dispatching Transfer or CheckPallet → no other command can be processing (must wait)
    /// - If any device is processing Inbound → cannot dispatch Outbound (must wait)
    /// - If any device is processing Outbound → cannot dispatch Inbound (must wait)
    /// </summary>
    private bool CanDispatchCommandType(CommandType commandType, out string blockReason)
    {
        blockReason = string.Empty;

        // Rule: If any device is in error state (Alarm/Failed/Timeout) → block ALL new commands
        // This handles the scenario where 2 logical devices share 1 physical device:
        // When Device A has error, Device B's physical device is also blocked
        // Must wait for recovery before dispatching any command
        if (_tracker.HasDeviceErrors())
        {
            var errorDevices = _tracker.GetDeviceErrors();
            var errorSummary = string.Join(", ", errorDevices.Select(e => $"{e.Key}:Slot{e.Value.SlotId}"));
            blockReason = $"device(s) in error state [{errorSummary}]";
            return false;
        }

        // Get all currently processing commands
        var processingCommands = _tracker.GetProcessingCommands().ToList();
        
        if (processingCommands.Count == 0)
        {
            return true; // No conflicts possible
        }

        // Rule: If Transfer or CheckPallet is processing → block ALL commands
        var hasProcessingTransfer = processingCommands.Any(c => c.CommandType == CommandType.Transfer);
        var hasProcessingCheckPallet = processingCommands.Any(c => c.CommandType == CommandType.CheckPallet);
        if (hasProcessingTransfer || hasProcessingCheckPallet)
        {
            blockReason = "Transfer/CheckPallet is processing";
            return false;
        }

        // Rule: If dispatching Transfer or CheckPallet → no other command can be processing
        if (commandType == CommandType.Transfer || commandType == CommandType.CheckPallet)
        {
            blockReason = "other commands still processing";
            return false;
        }

        // Check for Inbound/Outbound conflict
        var hasProcessingInbound = processingCommands.Any(c => c.CommandType == CommandType.Inbound);
        var hasProcessingOutbound = processingCommands.Any(c => c.CommandType == CommandType.Outbound);

        var canDispatch = commandType switch
        {
            CommandType.Inbound => !hasProcessingOutbound,  // Cannot dispatch Inbound if Outbound is processing
            CommandType.Outbound => !hasProcessingInbound,  // Cannot dispatch Outbound if Inbound is processing
            _ => true
        };

        if (!canDispatch)
        {
            blockReason = $"Inbound/Outbound conflict";
        }

        return canDispatch;
    }

    /// <summary>
    /// Checks if a slot supports the specified command type based on its capabilities.
    /// Returns true if capabilities are not registered (backward compatibility).
    /// </summary>
    private bool SlotSupportsCommand(string deviceId, int slotId, CommandType commandType)
    {
        // If device capabilities are not registered, allow all commands (backward compatibility)
        if (!_slotCapabilities.TryGetValue(deviceId, out var deviceSlots))
        {
            return true;
        }

        // If slot capabilities are not registered, allow all commands
        if (!deviceSlots.TryGetValue(slotId, out var capabilities))
        {
            return true;
        }

        return capabilities.SupportsCommandType(commandType);
    }

    /// <summary>
    /// DEPRECATED: Use SlotSupportsCommand.
    /// Checks if a device supports the specified command type based on its capabilities.
    /// </summary>
    [Obsolete("Use SlotSupportsCommand for multi-slot architecture")]
    private bool DeviceSupportsCommand(string deviceId, CommandType commandType)
    {
        // For backward compatibility, check slot 0
        return SlotSupportsCommand(deviceId, 0, commandType);
    }

    /// <summary>
    /// Applies stagger delay between consecutive dispatches to avoid overwhelming devices.
    /// First command has no delay, subsequent commands always delay 2s.
    /// Rolls back on cancellation.
    /// </summary>
    private async Task ApplyDispatchStaggerDelayAsync(
        CommandEnvelope command,
        string deviceId,
        int slotId,
        int slotIndex,
        List<SlotInfo> availableSlots,
        CancellationToken cancellationToken)
    {
        // Skip delay for the first command ever
        if (_lastDispatchTime == DateTimeOffset.MinValue)
        {
            return;
        }

        // Always delay 2s for subsequent commands
        try
        {
            await Task.Delay(_dispatchStaggerDelay, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cancellation during delay: rollback
            _tracker.MarkAsPending(command.CommandId, command);
            availableSlots.Insert(slotIndex, new SlotInfo(deviceId, slotId));
            throw;
        }
    }

    /// <summary>
    /// Returns unused slots back to availability channel for future matching.
    /// </summary>
    private async Task ReturnUnusedSlotsToPoolAsync(
        List<SlotInfo> availableSlots,
        CancellationToken cancellationToken)
    {
        foreach (var slot in availableSlots)
        {
            await RequeueSlotAsync(slot.DeviceId, slot.SlotId, cancellationToken).ConfigureAwait(false);
        }
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Checks if command was removed from tracking before scheduling.
    /// </summary>
    private bool IsCommandRemoved(string commandId)
    {
        var state = _tracker.GetCommandState(commandId);
        return state == CommandState.Removed;
    }

    #endregion

    #region Slot Management

    /// <summary>
    /// Re-queues a slot ticket back to availability channel.
    /// </summary>
    private async Task RequeueSlotAsync(string deviceId, int slotId, CancellationToken cancellationToken)
    {
        var ticket = new ReadyTicket
        {
            PlcDeviceId = deviceId,
            SlotId = slotId,
            ReadyAt = DateTimeOffset.UtcNow,
            WorkerInstance = 0,
            CurrentQueueDepth = 0
        };

        try
        {
            await _channels.AvailabilityChannel.Writer.WriteAsync(ticket, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            // Channel closed, ignore
        }
    }

    /// <summary>
    /// DEPRECATED: Use RequeueSlotAsync.
    /// Re-queues a device ticket back to availability channel.
    /// </summary>
    [Obsolete("Use RequeueSlotAsync for multi-slot architecture")]
    private async Task RequeueDeviceAsync(string deviceId, CancellationToken cancellationToken)
    {
        await RequeueSlotAsync(deviceId, 0, cancellationToken);
    }

    /// <summary>
    /// Checks if command has device affinity (prefers specific device).
    /// </summary>
    private static bool HasDeviceAffinity(CommandEnvelope command)
    {
        return !string.IsNullOrEmpty(command.PlcDeviceId);
    }

    #endregion

    #region Command Dispatching

    /// <summary>
    /// Dispatches command to target slot's channel.
    /// Handles slot channel closure by re-queueing the command.
    /// </summary>
    private async Task DispatchCommandToSlotAsync(
        CommandEnvelope command,
        string targetDevice,
        int targetSlot,
        Queue<CommandEnvelope> pendingCommands,
        CancellationToken cancellationToken)
    {
        var slotChannel = _channels.GetOrCreateSlotChannel(targetDevice, targetSlot);

        // Update command with target device and slot
        command = command with 
        { 
            PlcDeviceId = targetDevice,
            SlotId = targetSlot
        };

        try
        {
            await slotChannel.Writer.WriteAsync(command, cancellationToken)
                .ConfigureAwait(false);
            
            _logger?.LogInformation($"[Matchmaker] Dispatched command {command.CommandId} ({command.CommandType}) to {targetDevice}/Slot{targetSlot}");
        }
        catch (ChannelClosedException)
        {
            _logger?.LogWarning($"[Matchmaker] Slot channel closed for {targetDevice}/Slot{targetSlot}, re-queueing command {command.CommandId}");
            HandleSlotChannelClosed(command, pendingCommands);
        }
    }

    /// <summary>
    /// DEPRECATED: Use DispatchCommandToSlotAsync.
    /// Dispatches command to target device's channel.
    /// </summary>
    [Obsolete("Use DispatchCommandToSlotAsync for multi-slot architecture")]
    private async Task DispatchCommandToDeviceAsync(
        CommandEnvelope command,
        string targetDevice,
        Queue<CommandEnvelope> pendingCommands,
        CancellationToken cancellationToken)
    {
        await DispatchCommandToSlotAsync(command, targetDevice, 0, pendingCommands, cancellationToken);
    }

    /// <summary>
    /// Handles slot channel closure by re-queueing command.
    /// </summary>
    private void HandleSlotChannelClosed(
        CommandEnvelope command,
        Queue<CommandEnvelope> pendingCommands)
    {
        pendingCommands.Enqueue(command);
        _tracker.MarkAsPending(command.CommandId, command);
    }

    #endregion
}

/// <summary>
/// Information about an available slot.
/// </summary>
internal readonly struct SlotInfo
{
    public string DeviceId { get; }
    public int SlotId { get; }

    public SlotInfo(string deviceId, int slotId)
    {
        DeviceId = deviceId;
        SlotId = slotId;
    }
}

/// <summary>
/// Result of slot matching operation.
/// </summary>
internal readonly struct SlotMatchResult
{
    public bool Found { get; }
    public string DeviceId { get; }
    public int SlotId { get; }
    public int SlotIndex { get; }

    public SlotMatchResult(bool found, string deviceId, int slotId, int slotIndex)
    {
        Found = found;
        DeviceId = deviceId;
        SlotId = slotId;
        SlotIndex = slotIndex;
    }

    public static SlotMatchResult NotFound => new(false, string.Empty, -1, -1);
}

/// <summary>
/// Result of device matching operation.
/// DEPRECATED: Use SlotMatchResult for multi-slot architecture.
/// </summary>
[Obsolete("Use SlotMatchResult for multi-slot architecture")]
internal readonly struct DeviceMatchResult
{
    public bool Found { get; }
    public string DeviceId { get; }
    public int DeviceIndex { get; }

    public DeviceMatchResult(bool found, string deviceId, int deviceIndex)
    {
        Found = found;
        DeviceId = deviceId;
        DeviceIndex = deviceIndex;
    }

    public static DeviceMatchResult NotFound => new(false, string.Empty, -1);
}
