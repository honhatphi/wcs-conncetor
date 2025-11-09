using System.Collections.Concurrent;
using System.Threading.Channels;
using TQG.Automation.SDK.Orchestration.Models;

namespace TQG.Automation.SDK.Orchestration.Infrastructure;

/// <summary>
/// Central holder for all orchestration channels.
/// Provides thread-safe access to input, availability, result, and per-device channels.
/// </summary>
internal sealed class OrchestratorChannels : IAsyncDisposable
{
    /// <summary>
    /// Input channel: AutomationGateway writes CommandEnvelope here (bounded: 1000).
    /// Matchmaker reads from this channel to schedule commands.
    /// </summary>
    public Channel<CommandEnvelope> InputChannel { get; }

    /// <summary>
    /// Availability channel: DeviceWorkers write ReadyTickets here (unbounded).
    /// Matchmaker reads from this channel to match commands with available devices.
    /// </summary>
    public Channel<ReadyTicket> AvailabilityChannel { get; }

    /// <summary>
    /// Result channel: DeviceWorkers write CommandResults here (unbounded).
    /// ReplyHub reads from this channel to update tracker and broadcast.
    /// </summary>
    public Channel<CommandResult> ResultChannel { get; }

    /// <summary>
    /// Broadcast channel: ReplyHub writes CommandResults here (unbounded) for multiple observers.
    /// Observers read from this channel via ObserveResultsAsync() for true broadcast support.
    /// </summary>
    public Channel<CommandResult> BroadcastChannel { get; }

    /// <summary>
    /// Per-device channels: Matchmaker writes scheduled CommandEnvelope here (bounded: 1 per device).
    /// Each DeviceWorker reads from its own device channel for sequential execution.
    /// Thread-safe concurrent dictionary ensures safe concurrent access during worker lifecycle.
    /// </summary>
    private readonly ConcurrentDictionary<string, Channel<CommandEnvelope>> _deviceChannels;

    /// <summary>
    /// Initializes orchestration channels with configured capacities.
    /// </summary>
    public OrchestratorChannels()
    {
        InputChannel = ChannelConfiguration.CreateBoundedChannel<CommandEnvelope>(
            ChannelConfiguration.InputChannelCapacity);

        AvailabilityChannel = ChannelConfiguration.CreateUnboundedChannel<ReadyTicket>();
        ResultChannel = ChannelConfiguration.CreateUnboundedChannel<CommandResult>();
        BroadcastChannel = ChannelConfiguration.CreateUnboundedChannel<CommandResult>();

        _deviceChannels = new ConcurrentDictionary<string, Channel<CommandEnvelope>>(
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets or creates a per-device channel for the specified PLC device ID.
    /// Each device gets a bounded channel (capacity=1) for sequential command execution.
    /// Thread-safe: multiple workers can call concurrently during initialization.
    /// </summary>
    /// <param name="plcDeviceId">PLC device identifier (e.g., "PLC-01")</param>
    /// <returns>Bounded channel for device-specific commands</returns>
    public Channel<CommandEnvelope> GetOrCreateDeviceChannel(string plcDeviceId)
    {
        return _deviceChannels.GetOrAdd(
            plcDeviceId,
            _ => ChannelConfiguration.CreateBoundedChannel<CommandEnvelope>(
                ChannelConfiguration.DeviceChannelCapacity));
    }

    /// <summary>
    /// Attempts to retrieve an existing device channel without creating one.
    /// </summary>
    /// <param name="plcDeviceId">PLC device identifier</param>
    /// <param name="channel">Device channel if found</param>
    /// <returns>True if channel exists, false otherwise</returns>
    public bool TryGetDeviceChannel(string plcDeviceId, out Channel<CommandEnvelope> channel)
    {
        return _deviceChannels.TryGetValue(plcDeviceId, out channel!);
    }

    /// <summary>
    /// Gets all currently registered device IDs (for status queries).
    /// </summary>
    public IReadOnlyCollection<string> GetDeviceIds()
    {
        return _deviceChannels.Keys.ToList();
    }

    /// <summary>
    /// Completes all channels to signal shutdown to readers.
    /// Readers will drain remaining items before terminating.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        // Complete input channel (no more submissions)
        InputChannel.Writer.Complete();

        // Complete availability channel (no more ready tickets)
        AvailabilityChannel.Writer.Complete();

        // Complete result channel (no more results)
        ResultChannel.Writer.Complete();

        // Complete broadcast channel (no more broadcasts)
        BroadcastChannel.Writer.Complete();

        // Complete all device channels (no more scheduled commands)
        foreach (var deviceChannel in _deviceChannels.Values)
        {
            deviceChannel.Writer.Complete();
        }

        // Allow readers to drain before disposal
        await Task.Delay(100).ConfigureAwait(false);
    }
}
