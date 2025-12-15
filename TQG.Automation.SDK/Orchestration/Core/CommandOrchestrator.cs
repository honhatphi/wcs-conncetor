using System.Runtime.CompilerServices;
using System.Threading.Channels;
using TQG.Automation.SDK.Core;
using TQG.Automation.SDK.Events;
using TQG.Automation.SDK.Orchestration.Infrastructure;
using TQG.Automation.SDK.Orchestration.Models;
using TQG.Automation.SDK.Orchestration.Workers;
using TQG.Automation.SDK.Shared;

namespace TQG.Automation.SDK.Orchestration.Core;

/// <summary>
/// Central orchestration coordinator managing command queue, scheduling, and execution.
/// Owns all channels, pause gate, state tracker, and background workers (Matchmaker, SlotWorkers, ReplyHub).
/// Thread-safe: all public methods can be called concurrently.
/// </summary>
internal sealed class CommandOrchestrator : IAsyncDisposable
{
    private readonly OrchestratorChannels _channels;
    private readonly AsyncManualResetEvent _pauseGate;
    private readonly PendingCommandTracker _tracker;
    private readonly CancellationTokenSource _shutdownCts;

    /// <summary>
    /// Slot workers organized by device and slot.
    /// Key: deviceId, Value: Dictionary&lt;slotId, SlotWorker&gt;
    /// </summary>
    private readonly Dictionary<string, Dictionary<int, SlotWorker>> _slotWorkers;

    private Matchmaker? _matchmaker;
    private ReplyHub? _replyHub;
    private Task? _matchmakerTask;
    private Task? _replyHubTask;

    private readonly object _lifecycleLock = new();
    private bool _isStarted;
    private bool _isDisposed;
    private Func<BarcodeReceivedEventArgs, CancellationToken, Task<BarcodeValidationResponse>>? _barcodeValidationCallback;

    /// <summary>
    /// Initializes the orchestrator with fresh channels and state.
    /// Does NOT start background tasks - call StartAsync() explicitly.
    /// </summary>
    public CommandOrchestrator()
    {
        _channels = new OrchestratorChannels();
        _pauseGate = new AsyncManualResetEvent(); // Start paused
        _tracker = new PendingCommandTracker();
        _shutdownCts = new CancellationTokenSource();
        _slotWorkers = new Dictionary<string, Dictionary<int, SlotWorker>>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Sets the barcode validation callback for INBOUND operations.
    /// Must be called before RegisterDevice().
    /// </summary>
    public void SetBarcodeValidationCallback(
        Func<BarcodeReceivedEventArgs, CancellationToken, Task<BarcodeValidationResponse>> callback)
    {
        _barcodeValidationCallback = callback ?? throw new ArgumentNullException(nameof(callback));
    }

    /// <summary>
    /// Registers a device with multiple slots for orchestration.
    /// Creates SlotWorker for each configured slot sharing the same IPlcClient.
    /// Must be called before Start().
    /// </summary>
    public void RegisterDevice(IPlcClient plcClient, PlcConnectionOptions config)
    {
        ArgumentNullException.ThrowIfNull(plcClient);
        ArgumentNullException.ThrowIfNull(config);

        lock (_lifecycleLock)
        {
            if (_isStarted)
                throw new InvalidOperationException("Cannot register devices after orchestration has started.");

            if (_slotWorkers.ContainsKey(config.DeviceId))
                throw new ArgumentException($"Device '{config.DeviceId}' is already registered.", nameof(config.DeviceId));

            if (_barcodeValidationCallback == null)
                throw new InvalidOperationException("Barcode validation callback must be set before registering devices. Call SetBarcodeValidationCallback() first.");

            // Create slot workers dictionary for this device
            var deviceSlots = new Dictionary<int, SlotWorker>();

            foreach (var slotConfig in config.Slots)
            {
                var slotWorker = new SlotWorker(
                    plcClient,                    // Shared client across all slots
                    config,                       // Device options (FailOnAlarm, timeouts, etc.)
                    slotConfig,                   // Slot-specific config (DbNumber, Capabilities)
                    _channels,
                    _barcodeValidationCallback
                );

                deviceSlots[slotConfig.SlotId] = slotWorker;
            }

            _slotWorkers[config.DeviceId] = deviceSlots;
        }
    }

    /// <summary>
    /// Starts background orchestration tasks (Matchmaker, ReplyHub, SlotWorkers).
    /// Idempotent: safe to call multiple times (subsequent calls are no-op).
    /// </summary>
    public void Start()
    {
        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, nameof(CommandOrchestrator));

            if (_isStarted)
                return; // Already started

            // Create and start Matchmaker
            _matchmaker = new Matchmaker(_channels, _pauseGate, _tracker);

            // Register all slot capabilities with Matchmaker
            foreach (var (deviceId, slots) in _slotWorkers)
            {
                foreach (var (slotId, worker) in slots)
                {
                    _matchmaker.RegisterSlotCapabilities(deviceId, slotId, worker.GetCapabilities());
                }
            }

            _matchmakerTask = Task.Run(() => _matchmaker.RunAsync(_shutdownCts.Token), CancellationToken.None);

            // Create and start ReplyHub
            _replyHub = new ReplyHub(_channels, _tracker);
            _replyHubTask = Task.Run(() => _replyHub.RunAsync(_shutdownCts.Token), CancellationToken.None);

            // Start all slot workers
            foreach (var slots in _slotWorkers.Values)
            {
                foreach (var worker in slots.Values)
                {
                    worker.Start();
                }
            }

            // Resume scheduling (open the pause gate)
            //_pauseGate.Set();

            _isStarted = true;
        }
    }

    /// <summary>
    /// Stops orchestration gracefully: completes channels, signals shutdown, waits for workers to finish.
    /// Idempotent: safe to call multiple times.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for shutdown wait</param>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        lock (_lifecycleLock)
        {
            if (!_isStarted || _isDisposed)
                return; // Nothing to stop
        }

        // Signal shutdown to all background tasks
        _shutdownCts.Cancel();

        // Stop all slot workers first
        var workerStopTasks = _slotWorkers.Values
            .SelectMany(slots => slots.Values)
            .Select(w => w.StopAsync())
            .ToList();

        try
        {
            await Task.WhenAll(workerStopTasks);
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }

        // Complete all channels (no more writes)
        await _channels.DisposeAsync();

        // Wait for Matchmaker and ReplyHub to drain and finish
        var tasksToWait = new List<Task>();
        if (_matchmakerTask != null)
            tasksToWait.Add(_matchmakerTask);
        if (_replyHubTask != null)
            tasksToWait.Add(_replyHubTask);

        if (tasksToWait.Count > 0)
        {
            try
            {
                await Task.WhenAll(tasksToWait);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
            }
        }

        lock (_lifecycleLock)
        {
            _isStarted = false;

            // Reset pause gate for next Start()
            _pauseGate.Reset();
        }
    }

    /// <summary>
    /// Submits a command to the input queue for scheduling.
    /// Blocks if input channel is full (backpressure).
    /// </summary>
    /// <param name="envelope">Command envelope to submit</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if submitted, false if orchestrator is shutting down</returns>
    public async Task<bool> SubmitCommandAsync(
        CommandEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        // Mark as pending before writing to channel
        _tracker.MarkAsPending(envelope.CommandId, envelope);

        try
        {
            // Write to input channel (bounded: may block on full)
            await _channels.InputChannel.Writer.WriteAsync(envelope, cancellationToken);

            // Auto-resume orchestration when new command arrives (only if paused)
            if (!_pauseGate.IsSet)
            {
                _pauseGate.Set();
            }

            return true;
        }
        catch (ChannelClosedException)
        {
            // Channel completed during shutdown
            _tracker.MarkAsRemoved(envelope.CommandId);
            return false;
        }
        catch (OperationCanceledException)
        {
            _tracker.MarkAsRemoved(envelope.CommandId);
            throw;
        }
    }

    /// <summary>
    /// Pauses command scheduling (Matchmaker stops matching commands to devices).
    /// In-flight commands continue executing.
    /// </summary>
    public void PauseScheduling()
    {
        ThrowIfDisposed();
        _pauseGate.Reset(); // Close gate
    }

    /// <summary>
    /// Resumes command scheduling (Matchmaker resumes matching).
    /// </summary>
    public void ResumeScheduling()
    {
        ThrowIfDisposed();
        _pauseGate.Set(); // Open gate
    }

    /// <summary>
    /// Indicates whether scheduling is currently paused.
    /// </summary>
    public bool IsSchedulingPaused
    {
        get
        {
            ThrowIfDisposed();
            return !_pauseGate.IsSet;
        }
    }

    /// <summary>
    /// Removes a pending command from the queue (soft delete).
    /// Command is marked as removed; if already executing, it completes normally.
    /// </summary>
    /// <param name="commandId">Client command ID to remove</param>
    /// <returns>True if command was pending and marked for removal, false if not found or already in-flight</returns>
    public bool RemoveCommand(string commandId)
    {
        ThrowIfDisposed();

        var state = _tracker.GetCommandState(commandId);
        if (state == CommandState.Pending)
        {
            return _tracker.MarkAsRemoved(commandId);
        }

        return false;
    }

    /// <summary>
    /// Triggers manual recovery for a specific device.
    /// This signals all slot workers of the device to check device status and resume operations if ready.
    /// Only effective when the device's AutoRecoveryEnabled is false.
    /// </summary>
    /// <param name="deviceId">The device ID to trigger recovery for.</param>
    /// <returns>True if device exists and recovery was triggered; otherwise false.</returns>
    public bool TriggerDeviceRecovery(string deviceId)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(deviceId))
            return false;

        lock (_lifecycleLock)
        {
            if (_slotWorkers.TryGetValue(deviceId, out var slots))
            {
                // Trigger recovery on all slots of this device
                foreach (var worker in slots.Values)
                {
                    worker.TriggerManualRecovery();
                }
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Triggers manual recovery for a specific slot.
    /// </summary>
    /// <param name="deviceId">The device ID.</param>
    /// <param name="slotId">The slot ID to trigger recovery for.</param>
    /// <returns>True if slot exists and recovery was triggered; otherwise false.</returns>
    public bool TriggerSlotRecovery(string deviceId, int slotId)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(deviceId))
            return false;

        lock (_lifecycleLock)
        {
            if (_slotWorkers.TryGetValue(deviceId, out var slots) &&
                slots.TryGetValue(slotId, out var worker))
            {
                worker.TriggerManualRecovery();
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Gets current orchestrator status snapshot (queue depth, processing count, device stats).
    /// </summary>
    public (int Queued, int Processing, int Completed, bool IsPaused, DeviceStatistics[] DeviceStats) GetStatus()
    {
        ThrowIfDisposed();

        var queued = _tracker.GetPendingCount();
        var processing = _tracker.GetProcessingCount();
        var completed = (int)_tracker.GetTotalCompleted();
        var isPaused = !_pauseGate.IsSet;
        var deviceStats = _tracker.GetDeviceStatistics().ToArray();

        return (queued, processing, completed, isPaused, deviceStats);
    }

    /// <summary>
    /// Gets the current location of a device by reading CurrentFloor, CurrentRail, CurrentBlock, CurrentDepth from PLC.
    /// Returns null if device is not found or PLC read fails.
    /// Uses the first slot worker for location reading (all slots share the same client).
    /// </summary>
    /// <param name="deviceId">Device identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Current location or null if not found.</returns>
    public async Task<Location?> GetDeviceCurrentLocationAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(deviceId))
            return null;

        // Find first slot worker for this device
        SlotWorker? worker;
        lock (_lifecycleLock)
        {
            if (!_slotWorkers.TryGetValue(deviceId, out var slots) || slots.Count == 0)
                return null;

            worker = slots.Values.First();
        }

        // Read current location from PLC
        try
        {
            return await worker.ReadCurrentLocationAsync(cancellationToken);
        }
        catch
        {
            // If read fails, return null
            return null;
        }
    }

    /// <summary>
    /// Gets the current location for a specific slot.
    /// </summary>
    /// <param name="deviceId">Device identifier.</param>
    /// <param name="slotId">Slot identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Current location or null if not found.</returns>
    public async Task<Location?> GetSlotCurrentLocationAsync(string deviceId, int slotId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(deviceId))
            return null;

        SlotWorker? worker;
        lock (_lifecycleLock)
        {
            if (!_slotWorkers.TryGetValue(deviceId, out var slots) ||
                !slots.TryGetValue(slotId, out worker))
                return null;
        }

        try
        {
            return await worker.ReadCurrentLocationAsync(cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Removes multiple pending commands from the queue.
    /// Commands already executing will complete normally.
    /// </summary>
    /// <param name="commandIds">Collection of command IDs to remove.</param>
    /// <returns>Count of successfully removed commands.</returns>
    public int RemoveCommands(IEnumerable<string> commandIds)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(commandIds);

        var removedCount = 0;
        foreach (var commandId in commandIds)
        {
            if (!string.IsNullOrWhiteSpace(commandId) && RemoveCommand(commandId))
            {
                removedCount++;
            }
        }

        return removedCount;
    }

    /// <summary>
    /// Gets all currently executing commands for a specific device (one per slot).
    /// Returns empty list if device is idle or not found.
    /// </summary>
    /// <param name="deviceId">Device identifier.</param>
    /// <returns>List of command tracking info for all executing commands on this device.</returns>
    public IReadOnlyList<CommandInfo> GetDeviceActiveCommands(string deviceId)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(deviceId))
            return [];

        // Find all processing commands for this device (could be multiple - one per slot)
        var activeCommands = _tracker.GetAllCommands()
            .Where(info =>
                info.PlcDeviceId?.Equals(deviceId, StringComparison.OrdinalIgnoreCase) == true &&
                info.State == CommandState.Processing)
            .Select(MapToCommandInfo)
            .ToList();

        return activeCommands;
    }

    /// <summary>
    /// Gets all pending commands in the queue (awaiting device assignment).
    /// </summary>
    /// <returns>List of pending commands ordered by submission time.</returns>
    public IReadOnlyList<CommandInfo> GetPendingCommands()
    {
        ThrowIfDisposed();

        return _tracker.GetPendingCommands()
            .Select(MapToCommandInfo)
            .ToList();
    }

    /// <summary>
    /// Gets all commands currently being processed (executing on devices).
    /// </summary>
    /// <returns>List of processing commands ordered by start time.</returns>
    public IReadOnlyList<CommandInfo> GetProcessingCommands()
    {
        ThrowIfDisposed();

        return _tracker.GetProcessingCommands()
            .Select(MapToCommandInfo)
            .ToList();
    }

    /// <summary>
    /// Maps internal CommandTrackingInfo to public CommandInfo.
    /// </summary>
    private static CommandInfo MapToCommandInfo(CommandTrackingInfo trackingInfo)
    {
        return new CommandInfo
        {
            CommandId = trackingInfo.CommandId,
            PlcDeviceId = trackingInfo.PlcDeviceId,
            CommandType = trackingInfo.CommandType,
            State = trackingInfo.State.ToString(),
            Status = trackingInfo.Status?.ToString() ?? "Unknown",
            SourceLocation = trackingInfo.SourceLocation,
            DestinationLocation = trackingInfo.DestinationLocation,
            GateNumber = trackingInfo.GateNumber,
            SubmittedAt = trackingInfo.SubmittedAt,
            StartedAt = trackingInfo.StartedAt,
            CompletedAt = trackingInfo.CompletedAt,
            PalletAvailable = trackingInfo.PalletAvailable,
            PalletUnavailable = trackingInfo.PalletUnavailable,
            PlcError = trackingInfo.PlcError
        };
    }

    /// <summary>
    /// Observes command results as they complete (streams from BroadcastChannel).
    /// Supports multiple concurrent observers - each observer receives all results.
    /// Caller should consume with await foreach to receive results in real-time.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to stop observing</param>
    /// <returns>Async enumerable of command results</returns>
    public async IAsyncEnumerable<CommandResult> ObserveResultsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        // Read from broadcast channel (multiple observers supported)
        // ReplyHub broadcasts each result here after updating tracker
        await foreach (var result in _channels.BroadcastChannel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return result;
        }
    }

    /// <summary>
    /// Gracefully shuts down orchestrator and releases resources.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
            return;

        // Stop orchestration (completes channels, waits for workers)
        await StopAsync().ConfigureAwait(false);

        // Dispose all slot workers
        foreach (var slots in _slotWorkers.Values)
        {
            foreach (var worker in slots.Values)
            {
                await worker.DisposeAsync().ConfigureAwait(false);
            }
            slots.Clear();
        }
        _slotWorkers.Clear();

        // Dispose shutdown token
        _shutdownCts.Dispose();

        lock (_lifecycleLock)
        {
            _isDisposed = true;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, nameof(CommandOrchestrator));
    }
}
