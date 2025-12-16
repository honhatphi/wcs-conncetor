using System.Threading.Channels;
using TQG.Automation.SDK.Core;
using TQG.Automation.SDK.Events;
using TQG.Automation.SDK.Logging;
using TQG.Automation.SDK.Orchestration.Executors;
using TQG.Automation.SDK.Orchestration.Infrastructure;
using TQG.Automation.SDK.Orchestration.Models;
using TQG.Automation.SDK.Shared;

namespace TQG.Automation.SDK.Orchestration.Workers;

/// <summary>
/// Per-slot worker that reads commands from slot-specific channel,
/// executes them via shared IPlcClient, and writes results to ResultChannel.
/// Multiple SlotWorkers can share the same IPlcClient for parallel execution.
///
/// Safety Protocol:
/// Before executing any command, the worker verifies:
/// 1. Link established (SoftwareConnected flag = true)
/// 2. Device ready (DeviceReady flag = true)
///
/// If either check fails, the command is rejected with Error status.
/// 
/// Error Recovery Protocol:
/// When a command fails (Failed/Timeout status), the worker:
/// 1. Sets device error state via PendingCommandTracker (blocks ALL slots of this device)
/// 2. Waits for device recovery (auto or manual)
/// 3. Clears device error state when recovery is complete
/// </summary>
internal sealed class SlotWorker : IAsyncDisposable
{
    private readonly string _deviceId;
    private readonly int _slotId;
    private readonly string _compositeId;
    private readonly IPlcClient _plcClient;
    private readonly PlcConnectionOptions _deviceConfig;
    private readonly SlotConfiguration _slotConfig;
    private readonly SignalMap _signalMap;
    private readonly OrchestratorChannels _channels;
    private readonly PendingCommandTracker _tracker;
    private readonly Channel<CommandEnvelope> _slotChannel;
    private readonly CancellationTokenSource _workerCts;
    private readonly InboundExecutor _inboundExecutor;
    private readonly OutboundExecutor _outboundExecutor;
    private readonly TransferExecutor _transferExecutor;
    private readonly CheckExecutor _checkExecutor;
    private readonly AsyncManualResetEvent _manualRecoveryTrigger;
    private readonly ILogger _logger;
    private Task? _workerTask;

    /// <summary>
    /// Creates a new SlotWorker for a specific slot within a device.
    /// </summary>
    /// <param name="plcClient">Shared PLC client (same instance across all slots of this device).</param>
    /// <param name="deviceConfig">Device-level configuration including FailOnAlarm and SignalMapTemplate.</param>
    /// <param name="slotConfig">Slot-specific configuration including DbNumber and Capabilities.</param>
    /// <param name="channels">Orchestrator channels for communication.</param>
    /// <param name="tracker">Command tracker for managing device error state.</param>
    /// <param name="logger">Logger for diagnostic messages.</param>
    /// <param name="barcodeValidationCallback">Callback for barcode validation during inbound operations.</param>
    public SlotWorker(
        IPlcClient plcClient,
        PlcConnectionOptions deviceConfig,
        SlotConfiguration slotConfig,
        OrchestratorChannels channels,
        PendingCommandTracker tracker,
        ILogger logger,
        Func<BarcodeReceivedEventArgs, CancellationToken, Task<BarcodeValidationResponse>> barcodeValidationCallback)
    {
        _plcClient = plcClient ?? throw new ArgumentNullException(nameof(plcClient));
        _deviceConfig = deviceConfig ?? throw new ArgumentNullException(nameof(deviceConfig));
        _slotConfig = slotConfig ?? throw new ArgumentNullException(nameof(slotConfig));
        _channels = channels ?? throw new ArgumentNullException(nameof(channels));
        _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _deviceId = deviceConfig.DeviceId;
        _slotId = slotConfig.SlotId;
        _compositeId = slotConfig.GetCompositeId(_deviceId);

        // Generate SignalMap from template using slot's DB number
        _signalMap = deviceConfig.SignalMapTemplate.ToSignalMap(slotConfig.DbNumber);

        // Get or create slot-specific channel
        _slotChannel = channels.GetOrCreateSlotChannel(_deviceId, _slotId);
        _workerCts = new CancellationTokenSource();

        // Create executors with slot-specific SignalMap and device-level FailOnAlarm
        _inboundExecutor = new InboundExecutor(plcClient, _signalMap, deviceConfig.FailOnAlarm, barcodeValidationCallback);
        _outboundExecutor = new OutboundExecutor(plcClient, _signalMap, deviceConfig.FailOnAlarm);
        _transferExecutor = new TransferExecutor(plcClient, _signalMap, deviceConfig.FailOnAlarm);
        _checkExecutor = new CheckExecutor(plcClient, _signalMap);
        _manualRecoveryTrigger = new AsyncManualResetEvent();

        // Set logger for all executors
        _inboundExecutor.SetLogger(logger);
        _outboundExecutor.SetLogger(logger);
        _transferExecutor.SetLogger(logger);
        _checkExecutor.SetLogger(logger);
    }

    /// <summary>
    /// Gets the device identifier.
    /// </summary>
    public string DeviceId => _deviceId;

    /// <summary>
    /// Gets the slot identifier.
    /// </summary>
    public int SlotId => _slotId;

    /// <summary>
    /// Gets the composite identifier (deviceId:SlotN).
    /// </summary>
    public string CompositeId => _compositeId;

    /// <summary>
    /// Starts the worker background task.
    /// </summary>
    public void Start()
    {
        if (_workerTask != null)
            return; // Already started

        _workerTask = Task.Run(() => RunAsync(_workerCts.Token), CancellationToken.None);
    }

    /// <summary>
    /// Gets the operational capabilities of this slot.
    /// </summary>
    public DeviceCapabilities GetCapabilities()
    {
        return _slotConfig.Capabilities;
    }

    /// <summary>
    /// Stops the worker gracefully.
    /// </summary>
    public async Task StopAsync()
    {
        if (_workerTask == null)
            return; // Not started

        _workerCts.Cancel();

        try
        {
            await _workerTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
    }

    /// <summary>
    /// Triggers manual recovery for the slot.
    /// This signals the worker to check device status and resume if ready.
    /// Used when AutoRecoveryEnabled is false.
    /// </summary>
    public void TriggerManualRecovery()
    {
        _manualRecoveryTrigger.Set();
    }

    /// <summary>
    /// Reads the current location of the device from PLC.
    /// Reads CurrentFloor, CurrentRail, CurrentBlock, CurrentDepth signals.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Current location or null if read fails.</returns>
    public async Task<Location?> ReadCurrentLocationAsync(CancellationToken cancellationToken)
    {
        try
        {
            var floorAddress = PlcAddress.Parse(_signalMap.CurrentFloor);
            var railAddress = PlcAddress.Parse(_signalMap.CurrentRail);
            var blockAddress = PlcAddress.Parse(_signalMap.CurrentBlock);
            var depthAddress = PlcAddress.Parse(_signalMap.CurrentDepth);

            var floor = await _plcClient.ReadAsync<int>(floorAddress, cancellationToken);
            var rail = await _plcClient.ReadAsync<int>(railAddress, cancellationToken);
            var block = await _plcClient.ReadAsync<int>(blockAddress, cancellationToken);
            var depth = await _plcClient.ReadAsync<int>(depthAddress, cancellationToken);

            return new Location
            {
                Floor = floor,
                Rail = rail,
                Block = block,
                Depth = depth
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Main worker loop: reads commands from slot channel, executes, writes results.
    /// </summary>
    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await SignalAvailabilityAsync(cancellationToken);

            while (!cancellationToken.IsCancellationRequested)
            {
                await ProcessNextCommandAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
    }

    /// <summary>
    /// Processes a single command from the slot channel.
    /// </summary>
    private async Task ProcessNextCommandAsync(CancellationToken cancellationToken)
    {
        CommandEnvelope? command = null;

        try
        {
            command = await _slotChannel.Reader.ReadAsync(cancellationToken);

            var result = await ExecuteCommandAsync(command, cancellationToken);

            // IMPORTANT: Set device error BEFORE publishing to block Matchmaker immediately
            // This prevents race condition where Matchmaker dispatches to another slot
            // before ReplyHub has a chance to process the error
            if (IsErrorStatus(result.Status))
            {
                var errorMessage = result.Message ?? "Unknown error";
                var errorCode = result.PlcError?.ErrorCode;
                _tracker.SetDeviceError(_deviceId, _slotId, errorMessage, errorCode);
            }

            await PublishResultAsync(result, cancellationToken);

            await HandlePostExecutionAsync(result, cancellationToken);
        }
        catch (ChannelClosedException)
        {
            throw; // Propagate to exit worker loop
        }
        catch (OperationCanceledException) when (command != null)
        {
            // Set device error before publishing cancellation
            _tracker.SetDeviceError(_deviceId, _slotId, "Worker stopped during execution");
            await HandleCommandCancellationAsync(command);
            throw; // Propagate to exit worker loop
        }
        catch (Exception ex) when (command != null)
        {
            await HandleCommandErrorAsync(command, ex, cancellationToken);
        }
    }

    /// <summary>
    /// Checks if the execution status indicates an error that requires recovery.
    /// </summary>
    private static bool IsErrorStatus(ExecutionStatus status)
    {
        return status == ExecutionStatus.Failed || status == ExecutionStatus.Timeout;
    }

    /// <summary>
    /// Publishes command execution result to the result channel.
    /// </summary>
    private async Task PublishResultAsync(CommandResult result, CancellationToken cancellationToken)
    {
        await _channels.ResultChannel.Writer.WriteAsync(result, cancellationToken);
    }

    /// <summary>
    /// Handles post-execution logic: signal availability or wait for recovery.
    /// Device error state is already set in ProcessNextCommandAsync before publishing.
    /// </summary>
    private async Task HandlePostExecutionAsync(CommandResult result, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return;

        if (ShouldSignalAvailability(result))
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken); // Wait 5 seconds before signaling availability again

            await SignalAvailabilityAsync(cancellationToken);
        }
        else
        {
            // Device error already set before publish - just wait for recovery
            await WaitForDeviceRecoveryAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Handles command cancellation by creating and publishing a cancellation result.
    /// </summary>
    private async Task HandleCommandCancellationAsync(CommandEnvelope command)
    {
        var result = CreateErrorResult(
            command.CommandId,
            ExecutionStatus.Failed,
            "Worker stopped during execution");

        await TryPublishResultAsync(result);
    }

    /// <summary>
    /// Handles unexpected command execution errors.
    /// </summary>
    private async Task HandleCommandErrorAsync(
        CommandEnvelope command,
        Exception exception,
        CancellationToken cancellationToken)
    {
        _logger.LogError($"[{_compositeId}] Unexpected error during command {command.CommandId} execution: {exception.Message}", exception);

        // Set device error BEFORE publishing to block Matchmaker immediately
        _tracker.SetDeviceError(_deviceId, _slotId, $"Worker error: {exception.Message}");

        var result = CreateErrorResult(
            command.CommandId,
            ExecutionStatus.Failed,
            $"Worker error: {exception.Message}");

        await TryPublishResultAsync(result);

        if (!cancellationToken.IsCancellationRequested)
        {
            await WaitForDeviceRecoveryAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Creates a standardized error result for failed command executions.
    /// </summary>
    private CommandResult CreateErrorResult(string commandId, ExecutionStatus status, string message)
    {
        var timestamp = DateTimeOffset.UtcNow;
        return new CommandResult
        {
            CommandId = commandId,
            PlcDeviceId = _deviceId,
            SlotId = _slotId,
            Status = status,
            Message = message,
            StartedAt = timestamp,
            CompletedAt = timestamp
        };
    }

    /// <summary>
    /// Attempts to publish a result, suppressing exceptions if channel is closed.
    /// </summary>
    private async Task TryPublishResultAsync(CommandResult result)
    {
        try
        {
            await _channels.ResultChannel.Writer.WriteAsync(result, CancellationToken.None);
        }
        catch
        {
            // Suppress if result channel closed during shutdown
        }
    }

    /// <summary>
    /// Executes a command via PLC client with timeout enforcement.
    /// </summary>
    private async Task<CommandResult> ExecuteCommandAsync(
        CommandEnvelope command,
        CancellationToken cancellationToken)
    {
        var startTime = DateTimeOffset.UtcNow;

        try
        {
            // Step 1: Verify link is established
            var isLinked = await _plcClient.IsLinkEstablishedAsync(cancellationToken);

            if (!isLinked)
            {
                return new CommandResult
                {
                    CommandId = command.CommandId,
                    PlcDeviceId = _deviceId,
                    SlotId = _slotId,
                    Status = ExecutionStatus.Failed,
                    Message = "Link not established: PLC program has not set SoftwareConnected flag. Ensure PLC program is running.",
                    StartedAt = startTime,
                    CompletedAt = DateTimeOffset.UtcNow
                };
            }

            // Step 2: Verify device is ready (with retry logic)
            var isReady = await WaitForDeviceReadyAsync(cancellationToken);

            if (!isReady)
            {
                return new CommandResult
                {
                    CommandId = command.CommandId,
                    PlcDeviceId = _deviceId,
                    SlotId = _slotId,
                    Status = ExecutionStatus.Failed,
                    Message = "Device not ready: PLC has not set DeviceReady flag. Device may be busy or in error state.",
                    StartedAt = startTime,
                    CompletedAt = DateTimeOffset.UtcNow
                };
            }

            // Step 3: Create timeout token combining worker cancellation and command timeout
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_deviceConfig.CommandTimeout);

            // Step 4: Execute via appropriate executor based on command type
            var executionResult = await ExecuteCommandInternalAsync(command, timeoutCts.Token);

            var completedTime = DateTimeOffset.UtcNow;

            // Build detailed message from execution steps
            var detailedMessage = executionResult.Message;
            if (executionResult.ExecutionSteps.Count > 0)
            {
                detailedMessage += $"\nSteps: {string.Join(" → ", executionResult.ExecutionSteps)}";
            }

            return new CommandResult
            {
                CommandId = command.CommandId,
                PlcDeviceId = _deviceId,
                SlotId = _slotId,
                Status = executionResult.Status,
                Message = detailedMessage,
                StartedAt = startTime,
                CompletedAt = completedTime,
                PalletAvailable = executionResult.PalletAvailable,
                PalletUnavailable = executionResult.PalletUnavailable,
                PlcError = executionResult.PlcError,
                Data = executionResult.ExecutionSteps.Count > 0
                    ? new Dictionary<string, object> { ["Steps"] = executionResult.ExecutionSteps }
                    : null
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Worker cancelled
            throw;
        }
        catch (OperationCanceledException)
        {
            // Timeout
            return new CommandResult
            {
                CommandId = command.CommandId,
                PlcDeviceId = _deviceId,
                SlotId = _slotId,
                Status = ExecutionStatus.Timeout,
                Message = $"Command timed out after {_deviceConfig.CommandTimeout.TotalSeconds}s",
                StartedAt = startTime,
                CompletedAt = DateTimeOffset.UtcNow
            };
        }
        catch (Exception ex)
        {
            // Execution error
            return new CommandResult
            {
                CommandId = command.CommandId,
                PlcDeviceId = _deviceId,
                SlotId = _slotId,
                Status = ExecutionStatus.Failed,
                Message = $"Execution failed: {ex.Message}",
                StartedAt = startTime,
                CompletedAt = DateTimeOffset.UtcNow
            };
        }
    }

    /// <summary>
    /// Internal execution logic - routes to appropriate executor based on command type.
    /// </summary>
    private async Task<CommandExecutionResult> ExecuteCommandInternalAsync(
        CommandEnvelope command,
        CancellationToken cancellationToken)
    {
        return command.CommandType switch
        {
            CommandType.Inbound => await _inboundExecutor.ExecuteAsync(command, _channels.ResultChannel, cancellationToken),

            CommandType.Outbound => await _outboundExecutor.ExecuteAsync(command, _channels.ResultChannel, cancellationToken),

            CommandType.Transfer => await _transferExecutor.ExecuteAsync(command, _channels.ResultChannel, cancellationToken),

            CommandType.CheckPallet => await _checkExecutor.ExecuteAsync(command, cancellationToken),

            _ => throw new InvalidOperationException($"Unknown command type: {command.CommandType}")
        };
    }

    /// <summary>
    /// Determines if slot should signal availability based on command execution result.
    /// Slot should NOT signal availability if:
    /// - Status is Alarm (intermediate, command still executing)
    /// - Status is Failed or Timeout (device needs recovery)
    /// </summary>
    private bool ShouldSignalAvailability(CommandResult result)
    {
        return result.Status switch
        {
            ExecutionStatus.Success => true,
            ExecutionStatus.Alarm => false,   // Intermediate - command still executing
            ExecutionStatus.Failed => false,  // Device needs recovery
            ExecutionStatus.Timeout => false, // Device needs recovery
            _ => false
        };
    }

    /// <summary>
    /// Waits for device to recover from error state before accepting new commands.
    /// Behavior depends on AutoRecoveryEnabled:
    /// - Auto mode: Continuously polls DeviceReady flag until true
    /// - Manual mode: Waits for manual recovery trigger via TriggerManualRecovery()
    /// </summary>
    private async Task WaitForDeviceRecoveryAsync(CancellationToken cancellationToken)
    {
        if (_deviceConfig.AutoRecoveryEnabled)
        {
            await WaitForAutoRecoveryAsync(cancellationToken);
        }
        else
        {
            await WaitForManualRecoveryAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Waits for device to become ready by polling DeviceReady flag.
    /// Used before command execution to ensure device is available.
    /// Returns true if device becomes ready within timeout, false otherwise.
    /// </summary>
    private async Task<bool> WaitForDeviceReadyAsync(CancellationToken cancellationToken)
    {
        var maxWaitTime = _deviceConfig.CommandTimeout; // Use configured command timeout
        var pollInterval = TimeSpan.FromSeconds(1); // Check every second
        var deadline = DateTimeOffset.UtcNow + maxWaitTime;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                bool isReady = await _plcClient.IsDeviceReadyAsync(cancellationToken);

                if (isReady)
                {
                    return true;
                }

                // Check if we've exceeded the wait time
                if (DateTimeOffset.UtcNow >= deadline)
                {
                    return false; // Timeout waiting for device to become ready
                }

                await Task.Delay(pollInterval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // Error reading device status, continue polling
                await Task.Delay(pollInterval, cancellationToken);
            }
        }

        return false; // Cancelled
    }

    /// <summary>
    /// Waits for automatic device recovery by continuously polling DeviceReady flag
    /// AND verifying that error flags (CommandFailed, ErrorAlarm) have been cleared.
    /// When recovery is complete, clears device error state to allow new commands.
    /// </summary>
    private async Task WaitForAutoRecoveryAsync(CancellationToken cancellationToken)
    {
        _logger.LogWarning($"[{_compositeId}] Slot in error state. Auto-recovery enabled, polling for device ready...");

        var lastLogTime = DateTimeOffset.MinValue;
        var logInterval = TimeSpan.FromMinutes(1); // Log every minute to avoid spam

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var isReady = await _plcClient.IsDeviceReadyAsync(cancellationToken);

                if (isReady)
                {
                    // Additional check: Verify error flags have been cleared
                    var errorFlagsCleared = await AreErrorFlagsClearedAsync(cancellationToken);

                    if (errorFlagsCleared)
                    {
                        _logger.LogInformation($"[{_compositeId}] Auto-recovery successful. Device ready and error flags cleared.");
                        // Clear device error state - allow ALL slots to receive new commands
                        _tracker.ClearDeviceError(_deviceId);

                        await SignalAvailabilityAsync(cancellationToken);
                        return;
                    }

                    // DeviceReady=true but error flags not cleared - continue waiting
                    var now = DateTimeOffset.UtcNow;
                    if (now - lastLogTime > logInterval)
                    {
                        _logger.LogWarning($"[{_compositeId}] DeviceReady=true but error flags (CommandFailed/ErrorAlarm) not cleared. Waiting for device reset...");
                        lastLogTime = now;
                    }
                }
                else
                {
                    // Log periodically
                    var now = DateTimeOffset.UtcNow;
                    if (now - lastLogTime > logInterval)
                    {
                        _logger.LogDebug($"[{_compositeId}] Waiting for auto-recovery (DeviceReady=false)...");
                        lastLogTime = now;
                    }
                }

                await Task.Delay(_deviceConfig.RecoveryPollInterval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[{_compositeId}] Error during auto-recovery check: {ex.Message}", ex);
                // Error reading device status, continue polling
                await Task.Delay(_deviceConfig.RecoveryPollInterval, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Waits for manual recovery trigger via TriggerManualRecovery().
    /// Blocks until recovery is triggered, then verifies device is ready and error flags cleared.
    /// When recovery is complete, clears device error state to allow new commands.
    /// </summary>
    private async Task WaitForManualRecoveryAsync(CancellationToken cancellationToken)
    {
        _logger.LogWarning($"[{_compositeId}] Slot in error state. Waiting for manual recovery trigger. Please reset the device first, then call RecoverDeviceAsync().");

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Wait for manual recovery trigger
                await _manualRecoveryTrigger.WaitAsync(cancellationToken);
                _manualRecoveryTrigger.Reset(); // Reset for next error

                _logger.LogInformation($"[{_compositeId}] Manual recovery triggered. Checking device status...");

                // Verify device is actually ready
                var isReady = await _plcClient.IsDeviceReadyAsync(cancellationToken);

                if (isReady)
                {
                    // Additional check: Verify error flags have been cleared
                    var errorFlagsCleared = await AreErrorFlagsClearedAsync(cancellationToken);

                    if (errorFlagsCleared)
                    {
                        _logger.LogInformation($"[{_compositeId}] Manual recovery successful. Device ready and error flags cleared.");
                        // Clear device error state - allow ALL slots to receive new commands
                        _tracker.ClearDeviceError(_deviceId);

                        await SignalAvailabilityAsync(cancellationToken);
                        return;
                    }

                    _logger.LogWarning($"[{_compositeId}] Manual recovery FAILED: Device is ready but error flags (CommandFailed/ErrorAlarm) are still set. Please reset the device completely before retrying.");
                }
                else
                {
                    _logger.LogWarning($"[{_compositeId}] Manual recovery FAILED: DeviceReady flag is false. Please ensure the device has been reset and is operational before retrying.");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[{_compositeId}] Error during manual recovery check: {ex.Message}", ex);
                // Error during recovery check, continue waiting
                await Task.Delay(_deviceConfig.RecoveryPollInterval, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Checks if error flags (CommandFailed, ErrorAlarm) have been cleared.
    /// This ensures the device has been properly reset before accepting new commands.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if all error flags are cleared (false), false otherwise.</returns>
    private async Task<bool> AreErrorFlagsClearedAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Read CommandFailed flag
            var commandFailedAddress = PlcAddress.Parse(_signalMap.CommandFailed);
            var commandFailed = await _plcClient.ReadAsync<bool>(commandFailedAddress, cancellationToken);

            if (commandFailed)
            {
                return false; // CommandFailed flag still set
            }

            // Read ErrorAlarm flag
            var errorAlarmAddress = PlcAddress.Parse(_signalMap.ErrorAlarm);
            var errorAlarm = await _plcClient.ReadAsync<bool>(errorAlarmAddress, cancellationToken);

            if (errorAlarm)
            {
                return false; // ErrorAlarm flag still set
            }

            return true; // All error flags cleared
        }
        catch
        {
            // Error reading flags, assume not cleared
            return false;
        }
    }

    /// <summary>
    /// Signals slot availability to AvailabilityChannel.
    /// Includes both DeviceId and SlotId for proper routing.
    /// </summary>
    private async Task SignalAvailabilityAsync(CancellationToken cancellationToken)
    {
        var ticket = new ReadyTicket
        {
            PlcDeviceId = _deviceId,
            SlotId = _slotId,
            ReadyAt = DateTimeOffset.UtcNow,
            WorkerInstance = GetHashCode(),
            CurrentQueueDepth = 0 // Slot channel has capacity 1, always 0 after read
        };

        try
        {
            await _channels.AvailabilityChannel.Writer.WriteAsync(ticket, cancellationToken);
        }
        catch (ChannelClosedException)
        {
            // Availability channel closed during shutdown, ignore
        }
    }

    /// <summary>
    /// Disposes worker resources.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _workerCts.Dispose();
    }
}
