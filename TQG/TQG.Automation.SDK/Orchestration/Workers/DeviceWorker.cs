using System.Threading.Channels;
using TQG.Automation.SDK.Core;
using TQG.Automation.SDK.Models;
using TQG.Automation.SDK.Orchestration.Executors;
using TQG.Automation.SDK.Orchestration.Infrastructure;
using TQG.Automation.SDK.Orchestration.Models;

namespace TQG.Automation.SDK.Orchestration.Workers;

/// <summary>
/// Per-device worker that reads commands from device-specific channel,
/// executes them via IPlcClient, and writes results to ResultChannel.
/// Each device has exactly one worker for sequential execution.
///
/// Safety Protocol:
/// Before executing any command, the worker verifies:
/// 1. Link established (SoftwareConnected flag = true)
/// 2. Device ready (DeviceReady flag = true)
///
/// If either check fails, the command is rejected with Error status.
/// </summary>
internal sealed class DeviceWorker : IAsyncDisposable
{
    private readonly string _deviceId;
    private readonly IPlcClient _plcClient;
    private readonly PlcConnectionOptions _config;
    private readonly OrchestratorChannels _channels;
    private readonly Channel<CommandEnvelope> _deviceChannel;
    private readonly CancellationTokenSource _workerCts;
    private readonly InboundExecutor _inboundExecutor;
    private readonly OutboundExecutor _outboundExecutor;
    private readonly TransferExecutor _transferExecutor;
    private readonly CheckExecutor _checkExecutor;
    private readonly AsyncManualResetEvent _manualRecoveryTrigger;
    private Task? _workerTask;

    public DeviceWorker(
        IPlcClient plcClient,
        PlcConnectionOptions config,
        OrchestratorChannels channels,
        Func<BarcodeValidationRequestedEventArgs, CancellationToken, Task<BarcodeValidationResponse>> barcodeValidationCallback)
    {
        _plcClient = plcClient ?? throw new ArgumentNullException(nameof(plcClient));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _channels = channels ?? throw new ArgumentNullException(nameof(channels));
        _deviceId = config.DeviceId;
        _deviceChannel = channels.GetOrCreateDeviceChannel(config.DeviceId);
        _workerCts = new CancellationTokenSource();
        _inboundExecutor = new InboundExecutor(plcClient, config.SignalMap, config.StopOnAlarm, barcodeValidationCallback);
        _outboundExecutor = new OutboundExecutor(plcClient, config.SignalMap, config.StopOnAlarm);
        _transferExecutor = new TransferExecutor(plcClient, config.SignalMap, config.StopOnAlarm);
        _checkExecutor = new CheckExecutor(plcClient, config.SignalMap);
        _manualRecoveryTrigger = new AsyncManualResetEvent();
    }

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
    /// Triggers manual recovery for the device.
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
            var floorAddress = PlcAddress.Parse(_config.SignalMap.CurrentFloor);
            var railAddress = PlcAddress.Parse(_config.SignalMap.CurrentRail);
            var blockAddress = PlcAddress.Parse(_config.SignalMap.CurrentBlock);
            var depthAddress = PlcAddress.Parse(_config.SignalMap.CurrentDepth);

            var floor = await _plcClient.ReadAsync<int>(floorAddress, cancellationToken).ConfigureAwait(false);
            var rail = await _plcClient.ReadAsync<int>(railAddress, cancellationToken).ConfigureAwait(false);
            var block = await _plcClient.ReadAsync<int>(blockAddress, cancellationToken).ConfigureAwait(false);
            var depth = await _plcClient.ReadAsync<int>(depthAddress, cancellationToken).ConfigureAwait(false);

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
    /// Main worker loop: reads commands from device channel, executes, writes results.
    /// </summary>
    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await SignalAvailabilityAsync(cancellationToken).ConfigureAwait(false);

            while (!cancellationToken.IsCancellationRequested)
            {
                await ProcessNextCommandAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
    }

    /// <summary>
    /// Processes a single command from the device channel.
    /// </summary>
    private async Task ProcessNextCommandAsync(CancellationToken cancellationToken)
    {
        CommandEnvelope? command = null;

        try
        {
            command = await _deviceChannel.Reader.ReadAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = await ExecuteCommandAsync(command, cancellationToken)
                .ConfigureAwait(false);

            await PublishResultAsync(result, cancellationToken).ConfigureAwait(false);

            await HandlePostExecutionAsync(result, cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            throw; // Propagate to exit worker loop
        }
        catch (OperationCanceledException) when (command != null)
        {
            await HandleCommandCancellationAsync(command).ConfigureAwait(false);
            throw; // Propagate to exit worker loop
        }
        catch (Exception ex) when (command != null)
        {
            await HandleCommandErrorAsync(command, ex, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Publishes command execution result to the result channel.
    /// </summary>
    private async Task PublishResultAsync(CommandResult result, CancellationToken cancellationToken)
    {
        await _channels.ResultChannel.Writer.WriteAsync(result, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Handles post-execution logic: signal availability or wait for recovery.
    /// </summary>
    private async Task HandlePostExecutionAsync(CommandResult result, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return;

        if (ShouldSignalAvailability(result))
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken); // Wait 5 seconds before signaling availability again

            await SignalAvailabilityAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await WaitForDeviceRecoveryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Handles command cancellation by creating and publishing a cancellation result.
    /// </summary>
    private async Task HandleCommandCancellationAsync(CommandEnvelope command)
    {
        var result = CreateErrorResult(
            command.CommandId,
            ExecutionStatus.Cancelled,
            "Worker stopped during execution");

        await TryPublishResultAsync(result).ConfigureAwait(false);
    }

    /// <summary>
    /// Handles unexpected command execution errors.
    /// </summary>
    private async Task HandleCommandErrorAsync(
        CommandEnvelope command,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var result = CreateErrorResult(
            command.CommandId,
            ExecutionStatus.Error,
            $"Worker error: {exception.Message}");

        await TryPublishResultAsync(result).ConfigureAwait(false);

        if (!cancellationToken.IsCancellationRequested)
        {
            await WaitForDeviceRecoveryAsync(cancellationToken).ConfigureAwait(false);
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
            await _channels.ResultChannel.Writer.WriteAsync(result, CancellationToken.None)
                .ConfigureAwait(false);
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
            var isLinked = await _plcClient.IsLinkEstablishedAsync(cancellationToken)
                .ConfigureAwait(false);

            if (!isLinked)
            {
                return new CommandResult
                {
                    CommandId = command.CommandId,
                    PlcDeviceId = _deviceId,
                    Status = ExecutionStatus.Error,
                    Message = "Link not established: PLC program has not set SoftwareConnected flag. Ensure PLC program is running.",
                    StartedAt = startTime,
                    CompletedAt = DateTimeOffset.UtcNow
                };
            }

            // Step 2: Verify device is ready
            var isReady = await _plcClient.IsDeviceReadyAsync(cancellationToken)
                .ConfigureAwait(false);

            if (!isReady)
            {
                return new CommandResult
                {
                    CommandId = command.CommandId,
                    PlcDeviceId = _deviceId,
                    Status = ExecutionStatus.Error,
                    Message = "Device not ready: PLC has not set DeviceReady flag. Device may be busy or in error state.",
                    StartedAt = startTime,
                    CompletedAt = DateTimeOffset.UtcNow
                };
            }

            // Step 3: Create timeout token combining worker cancellation and command timeout
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_config.CommandTimeout);

            // Step 4: Execute via appropriate executor based on command type
            var executionResult = await ExecuteCommandInternalAsync(command, timeoutCts.Token)
                .ConfigureAwait(false);

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
                Status = ExecutionStatus.Timeout,
                Message = $"Command timed out after {_config.CommandTimeout.TotalSeconds}s",
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
                Status = ExecutionStatus.Error,
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
            CommandType.Inbound => await _inboundExecutor.ExecuteAsync(command, cancellationToken)
                .ConfigureAwait(false),

            CommandType.Outbound => await _outboundExecutor.ExecuteAsync(command, cancellationToken)
                .ConfigureAwait(false),

            CommandType.Transfer => await _transferExecutor.ExecuteAsync(command, cancellationToken)
                .ConfigureAwait(false),

            CommandType.CheckPallet => await _checkExecutor.ExecuteAsync(command, cancellationToken)
                .ConfigureAwait(false),

            _ => throw new InvalidOperationException($"Unknown command type: {command.CommandType}")
        };
    }

    /// <summary>
    /// Determines if device should signal availability based on command execution result.
    /// Device should NOT signal availability if:
    /// - Status is Error or Failed (device in error state, needs manual reset)
    /// - Status is Warning but StopOnAlarm=true (treated as error)
    /// </summary>
    private bool ShouldSignalAvailability(CommandResult result)
    {
        return result.Status switch
        {
            ExecutionStatus.Success => true,
            ExecutionStatus.Warning => !_config.StopOnAlarm, // Warning OK unless StopOnAlarm
            ExecutionStatus.Error => false,   // Device in error state
            ExecutionStatus.Failed => false,  // PLC reported failure
            ExecutionStatus.Timeout => false,  // Timeout is transient, allow retry
            ExecutionStatus.Cancelled => false, // Cancellation is external, device OK
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
        if (_config.AutoRecoveryEnabled)
        {
            await WaitForAutoRecoveryAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await WaitForManualRecoveryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Waits for automatic device recovery by continuously polling DeviceReady flag.
    /// </summary>
    private async Task WaitForAutoRecoveryAsync(CancellationToken cancellationToken)
    {
        var lastLogTime = DateTimeOffset.MinValue;
        var logInterval = TimeSpan.FromMinutes(1); // Log every minute to avoid spam

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var isReady = await _plcClient.IsDeviceReadyAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (isReady)
                {
                    await SignalAvailabilityAsync(cancellationToken).ConfigureAwait(false);
                    return;
                }

                // Log periodically
                var now = DateTimeOffset.UtcNow;
                if (now - lastLogTime > logInterval)
                {
                    // Note: In production, use proper logging framework
                    // Console.WriteLine($"[{_deviceId}] Waiting for auto-recovery (DeviceReady=false)...");
                    lastLogTime = now;
                }

                await Task.Delay(_config.RecoveryPollInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // Error reading device status, continue polling
                await Task.Delay(_config.RecoveryPollInterval, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Waits for manual recovery trigger via TriggerManualRecovery().
    /// Blocks until recovery is triggered, then verifies device is ready.
    /// </summary>
    private async Task WaitForManualRecoveryAsync(CancellationToken cancellationToken)
    {
        // Note: In production, use proper logging framework
        // Console.WriteLine($"[{_deviceId}] Device in error state. Waiting for manual recovery trigger...");

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Wait for manual recovery trigger
                await _manualRecoveryTrigger.WaitAsync(cancellationToken).ConfigureAwait(false);
                _manualRecoveryTrigger.Reset(); // Reset for next error

                // Verify device is actually ready
                var isReady = await _plcClient.IsDeviceReadyAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (isReady)
                {
                    // Console.WriteLine($"[{_deviceId}] Manual recovery successful. Device ready.");
                    await SignalAvailabilityAsync(cancellationToken).ConfigureAwait(false);
                    return;
                }
                else
                {
                    // Console.WriteLine($"[{_deviceId}] Manual recovery triggered but device not ready. Continue waiting...");
                    // Continue waiting for next trigger
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // Error during recovery check, continue waiting
                await Task.Delay(_config.RecoveryPollInterval, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Signals device availability to AvailabilityChannel.
    /// </summary>
    private async Task SignalAvailabilityAsync(CancellationToken cancellationToken)
    {
        var ticket = new ReadyTicket
        {
            PlcDeviceId = _deviceId,
            ReadyAt = DateTimeOffset.UtcNow,
            WorkerInstance = GetHashCode(),
            CurrentQueueDepth = 0 // Device channel has capacity 1, always 0 after read
        };

        try
        {
            await _channels.AvailabilityChannel.Writer.WriteAsync(ticket, cancellationToken)
                .ConfigureAwait(false);
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
