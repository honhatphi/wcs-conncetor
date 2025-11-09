using System.Threading.Channels;
using TQG.Automation.SDK.Orchestration.Infrastructure;
using TQG.Automation.SDK.Orchestration.Models;

namespace TQG.Automation.SDK.Orchestration.Workers;

/// <summary>
/// Scheduling engine that matches pending commands from InputChannel with available devices
/// from AvailabilityChannel, then writes scheduled commands to device-specific channels.
/// Implements priority-based scheduling (High before Normal) with FIFO within same priority.
/// </summary>
internal sealed class Matchmaker
{
    private readonly OrchestratorChannels _channels;
    private readonly AsyncManualResetEvent _pauseGate;
    private readonly PendingCommandTracker _tracker;

    /// <summary>
    /// Delay between dispatching commands to consecutive devices to stagger workload.
    /// Set to 0 to disable staggering (dispatch all devices immediately).
    /// </summary>
    private readonly TimeSpan _dispatchStaggerDelay = TimeSpan.FromSeconds(2);

    public Matchmaker(
        OrchestratorChannels channels,
        AsyncManualResetEvent pauseGate,
        PendingCommandTracker tracker)
    {
        _channels = channels ?? throw new ArgumentNullException(nameof(channels));
        _pauseGate = pauseGate ?? throw new ArgumentNullException(nameof(pauseGate));
        _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
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
                await WaitIfPausedAsync(cancellationToken).ConfigureAwait(false);

                CollectPendingCommands(pendingCommands);

                if (ShouldAutoPause(pendingCommands))
                {
                    AutoPauseAndWait();
                    await WaitForNewDataAsync(cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (HasAvailableDevices())
                {
                    await MatchCommandsToDevicesAsync(pendingCommands, cancellationToken)
                        .ConfigureAwait(false);
                }

                if (ShouldWaitForNewData(pendingCommands))
                {
                    await WaitForNewDataAsync(cancellationToken).ConfigureAwait(false);
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
        await _pauseGate.WaitAsync(cancellationToken).ConfigureAwait(false);
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
    /// Checks if there are available devices in the channel.
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
                Task.Delay(100, cancellationToken)
            ).ConfigureAwait(false);
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
    /// Matches pending commands with available devices and dispatches them.
    /// Strict FIFO: Commands are processed in order, never skipped.
    /// </summary>
    private async Task MatchCommandsToDevicesAsync(
        Queue<CommandEnvelope> pendingCommands,
        CancellationToken cancellationToken)
    {
        var availableDevices = CollectAvailableDevices();

        await ProcessCommandQueueAsync(pendingCommands, availableDevices, cancellationToken)
            .ConfigureAwait(false);

        await ReturnUnusedDevicesToPoolAsync(availableDevices, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Collects all currently available devices from availability channel.
    /// </summary>
    private List<string> CollectAvailableDevices()
    {
        var devices = new List<string>();
        while (_channels.AvailabilityChannel.Reader.TryRead(out var ticket))
        {
            devices.Add(ticket.PlcDeviceId);
        }
        return devices;
    }

    /// <summary>
    /// Processes command queue in strict FIFO order, matching with available devices.
    /// Stops when next command cannot be matched (waits for device).
    /// </summary>
    private async Task ProcessCommandQueueAsync(
        Queue<CommandEnvelope> pendingCommands,
        List<string> availableDevices,
        CancellationToken cancellationToken)
    {
        bool hasDispatchedInThisIteration = false;

        while (pendingCommands.Count > 0 && availableDevices.Count > 0)
        {
            var command = pendingCommands.Peek();

            if (IsCommandRemoved(command.CommandId))
            {
                pendingCommands.Dequeue();
                continue;
            }

            var matchResult = TryFindMatchingDevice(command, availableDevices);

            if (!matchResult.Found)
            {
                // Required device not available, STOP and wait
                break;
            }

            // Match found: dequeue command and remove device from pool
            pendingCommands.Dequeue();
            availableDevices.RemoveAt(matchResult.DeviceIndex);

            // Apply stagger delay (except for first dispatch)
            if (hasDispatchedInThisIteration)
            {
                await ApplyDispatchStaggerDelayAsync(
                    command,
                    matchResult.DeviceId,
                    matchResult.DeviceIndex,
                    availableDevices,
                    cancellationToken).ConfigureAwait(false);
            }

            // Mark and dispatch
            _tracker.MarkAsProcessing(command.CommandId, matchResult.DeviceId);
            await DispatchCommandToDeviceAsync(
                command,
                matchResult.DeviceId,
                pendingCommands,
                cancellationToken).ConfigureAwait(false);

            hasDispatchedInThisIteration = true;
        }
    }

    /// <summary>
    /// Finds matching device for a command.
    /// Returns device ID and index if found, otherwise indicates no match.
    /// </summary>
    private static DeviceMatchResult TryFindMatchingDevice(
        CommandEnvelope command,
        List<string> availableDevices)
    {
        if (HasDeviceAffinity(command))
        {
            // Device-specific command: must use specified device
            int index = availableDevices.FindIndex(d =>
                d.Equals(command.PlcDeviceId, StringComparison.OrdinalIgnoreCase));

            if (index >= 0)
            {
                return new DeviceMatchResult(true, availableDevices[index], index);
            }

            // Required device not available
            return DeviceMatchResult.NotFound;
        }
        else
        {
            // Any-device command: use first available
            if (availableDevices.Count > 0)
            {
                return new DeviceMatchResult(true, availableDevices[0], 0);
            }

            // No device available
            return DeviceMatchResult.NotFound;
        }
    }

    /// <summary>
    /// Applies stagger delay between device dispatches to avoid overwhelming devices.
    /// Rolls back on cancellation.
    /// </summary>
    private async Task ApplyDispatchStaggerDelayAsync(
        CommandEnvelope command,
        string deviceId,
        int deviceIndex,
        List<string> availableDevices,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(_dispatchStaggerDelay, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cancellation during delay: rollback
            _tracker.MarkAsPending(command.CommandId, command);
            availableDevices.Insert(deviceIndex, deviceId);
            throw;
        }
    }

    /// <summary>
    /// Returns unused devices back to availability channel for future matching.
    /// </summary>
    private async Task ReturnUnusedDevicesToPoolAsync(
        List<string> availableDevices,
        CancellationToken cancellationToken)
    {
        foreach (var deviceId in availableDevices)
        {
            await RequeueDeviceAsync(deviceId, cancellationToken).ConfigureAwait(false);
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

    #region Device Management

    /// <summary>
    /// Re-queues a device ticket back to availability channel.
    /// </summary>
    private async Task RequeueDeviceAsync(string deviceId, CancellationToken cancellationToken)
    {
        var ticket = new ReadyTicket
        {
            PlcDeviceId = deviceId,
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
    /// Checks if command has device affinity (prefers specific device).
    /// </summary>
    private static bool HasDeviceAffinity(CommandEnvelope command)
    {
        return !string.IsNullOrEmpty(command.PlcDeviceId);
    }

    #endregion

    #region Command Dispatching

    /// <summary>
    /// Dispatches command to target device's channel.
    /// Handles device channel closure by re-queueing the command.
    /// </summary>
    private async Task DispatchCommandToDeviceAsync(
        CommandEnvelope command,
        string targetDevice,
        Queue<CommandEnvelope> pendingCommands,
        CancellationToken cancellationToken)
    {
        var deviceChannel = _channels.GetOrCreateDeviceChannel(targetDevice);

        try
        {
            await deviceChannel.Writer.WriteAsync(command, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            HandleDeviceChannelClosed(command, pendingCommands);
        }
    }

    /// <summary>
    /// Handles device channel closure by re-queueing command.
    /// </summary>
    private void HandleDeviceChannelClosed(
        CommandEnvelope command,
        Queue<CommandEnvelope> pendingCommands)
    {
        pendingCommands.Enqueue(command);
        _tracker.MarkAsPending(command.CommandId, command);
    }

    #endregion
}

/// <summary>
/// Result of device matching operation.
/// </summary>
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
