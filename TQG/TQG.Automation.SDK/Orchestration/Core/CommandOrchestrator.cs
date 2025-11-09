using System.Runtime.CompilerServices;
using System.Threading.Channels;
using TQG.Automation.SDK.Core;
using TQG.Automation.SDK.Models;
using TQG.Automation.SDK.Orchestration.Infrastructure;
using TQG.Automation.SDK.Orchestration.Models;
using TQG.Automation.SDK.Orchestration.Workers;

namespace TQG.Automation.SDK.Orchestration.Core;

/// <summary>
/// Central orchestration coordinator managing command queue, scheduling, and execution.
/// Owns all channels, pause gate, state tracker, and background workers (Matchmaker, DeviceWorkers, ReplyHub).
/// Thread-safe: all public methods can be called concurrently.
/// </summary>
internal sealed class CommandOrchestrator : IAsyncDisposable
{
    private readonly OrchestratorChannels _channels;
    private readonly AsyncManualResetEvent _pauseGate;
    private readonly PendingCommandTracker _tracker;
    private readonly CancellationTokenSource _shutdownCts;
    private readonly Dictionary<string, DeviceWorker> _deviceWorkers;

    private Matchmaker? _matchmaker;
    private ReplyHub? _replyHub;
    private Task? _matchmakerTask;
    private Task? _replyHubTask;

    private readonly object _lifecycleLock = new();
    private bool _isStarted;
    private bool _isDisposed;
    private Func<BarcodeValidationRequestedEventArgs, CancellationToken, Task<BarcodeValidationResponse>>? _barcodeValidationCallback;

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
        _deviceWorkers = new Dictionary<string, DeviceWorker>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Sets the barcode validation callback for INBOUND operations.
    /// Must be called before RegisterDevice().
    /// </summary>
    public void SetBarcodeValidationCallback(
        Func<BarcodeValidationRequestedEventArgs, CancellationToken, Task<BarcodeValidationResponse>> callback)
    {
        _barcodeValidationCallback = callback ?? throw new ArgumentNullException(nameof(callback));
    }

    /// <summary>
    /// Registers a device worker for orchestration.
    /// Must be called before StartAsync().
    /// </summary>
    public void RegisterDevice(IPlcClient plcClient, PlcConnectionOptions config)
    {
        ArgumentNullException.ThrowIfNull(config);

        lock (_lifecycleLock)
        {
            if (_isStarted)
                throw new InvalidOperationException("Cannot register devices after orchestration has started.");

            if (_deviceWorkers.ContainsKey(config.DeviceId))
                throw new ArgumentException($"Device '{config.DeviceId}' is already registered.", nameof(config.DeviceId));

            if (_barcodeValidationCallback == null)
                throw new InvalidOperationException("Barcode validation callback must be set before registering devices. Call SetBarcodeValidationCallback() first.");

            var worker = new DeviceWorker(plcClient, config, _channels, _barcodeValidationCallback);
            _deviceWorkers[config.DeviceId] = worker;
        }
    }

    /// <summary>
    /// Starts background orchestration tasks (Matchmaker, ReplyHub, DeviceWorkers).
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
            _matchmakerTask = Task.Run(() => _matchmaker.RunAsync(_shutdownCts.Token), CancellationToken.None);

            // Create and start ReplyHub
            _replyHub = new ReplyHub(_channels, _tracker);
            _replyHubTask = Task.Run(() => _replyHub.RunAsync(_shutdownCts.Token), CancellationToken.None);

            // Start all device workers
            foreach (var worker in _deviceWorkers.Values)
            {
                worker.Start();
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

        // Stop all device workers first
        var workerStopTasks = _deviceWorkers.Values.Select(w => w.StopAsync()).ToList();
        try
        {
            await Task.WhenAll(workerStopTasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }

        // Complete all channels (no more writes)
        await _channels.DisposeAsync().ConfigureAwait(false);

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
                await Task.WhenAll(tasksToWait).ConfigureAwait(false);
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
            await _channels.InputChannel.Writer.WriteAsync(envelope, cancellationToken)
                .ConfigureAwait(false);

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
    /// This signals the device worker to check device status and resume operations if ready.
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
            if (_deviceWorkers.TryGetValue(deviceId, out var worker))
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
    /// </summary>
    /// <param name="deviceId">Device identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Current location or null if not found.</returns>
    public async Task<Location?> GetDeviceCurrentLocationAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(deviceId))
            return null;

        // Find device worker
        DeviceWorker? worker;
        lock (_lifecycleLock)
        {
            if (!_deviceWorkers.TryGetValue(deviceId, out worker))
                return null;
        }

        // Read current location from PLC
        try
        {
            return await worker.ReadCurrentLocationAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // If read fails, return null
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
    /// Gets the currently executing command for a specific device.
    /// Returns null if device is idle or not found.
    /// </summary>
    /// <param name="deviceId">Device identifier.</param>
    /// <returns>Command tracking info of the executing command, or null if idle/not found.</returns>
    public CommandInfo? GetDeviceActiveCommand(string deviceId)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(deviceId))
            return null;

        // Find processing command for this device
        var activeCommand = _tracker.GetAllCommands()
            .FirstOrDefault(info =>
                info.PlcDeviceId?.Equals(deviceId, StringComparison.OrdinalIgnoreCase) == true &&
                info.State == CommandState.Processing);

        return activeCommand == null ? null : MapToCommandInfo(activeCommand);
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
        await foreach (var result in _channels.BroadcastChannel.Reader.ReadAllAsync(cancellationToken)
            .ConfigureAwait(false))
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

        // Dispose all device workers
        foreach (var worker in _deviceWorkers.Values)
        {
            await worker.DisposeAsync().ConfigureAwait(false);
        }
        _deviceWorkers.Clear();

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
