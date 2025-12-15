using System.Collections.Concurrent;
using System.Threading.Channels;
using TQG.Automation.SDK.Orchestration.Models;

namespace TQG.Automation.SDK.Orchestration.Infrastructure;

/// <summary>
/// Central holder for all orchestration channels.
/// Provides thread-safe access to input, availability, result, and per-slot channels.
/// </summary>
internal sealed class OrchestratorChannels : IAsyncDisposable
{
    /// <summary>
    /// Input channel: AutomationGateway writes CommandEnvelope here (bounded: 1000).
    /// Matchmaker reads from this channel to schedule commands.
    /// </summary>
    public Channel<CommandEnvelope> InputChannel { get; }

    /// <summary>
    /// Availability channel: SlotWorkers write ReadyTickets here (unbounded).
    /// Matchmaker reads from this channel to match commands with available slots.
    /// </summary>
    public Channel<ReadyTicket> AvailabilityChannel { get; }

    /// <summary>
    /// Result channel: SlotWorkers write CommandResults here (unbounded).
    /// ReplyHub reads from this channel to update tracker and broadcast.
    /// </summary>
    public Channel<CommandResult> ResultChannel { get; }

    /// <summary>
    /// Broadcast channel: ReplyHub writes CommandResults here (unbounded) for multiple observers.
    /// Observers read from this channel via ObserveResultsAsync() for true broadcast support.
    /// </summary>
    public Channel<CommandResult> BroadcastChannel { get; }

    /// <summary>
    /// Per-slot channels: Matchmaker writes scheduled CommandEnvelope here (bounded: 1 per slot).
    /// Each SlotWorker reads from its own slot channel for sequential execution.
    /// Thread-safe concurrent dictionary ensures safe concurrent access during worker lifecycle.
    /// Key format: "{deviceId}:Slot{slotId}"
    /// </summary>
    private readonly ConcurrentDictionary<string, Channel<CommandEnvelope>> _slotChannels;

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

        _slotChannels = new ConcurrentDictionary<string, Channel<CommandEnvelope>>(
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Generates the composite key for a slot channel.
    /// Format: "{deviceId}:Slot{slotId}"
    /// </summary>
    /// <param name="deviceId">Device identifier.</param>
    /// <param name="slotId">Slot identifier.</param>
    /// <returns>Composite channel key.</returns>
    private static string GetSlotChannelKey(string deviceId, int slotId) => $"{deviceId}:Slot{slotId}";

    /// <summary>
    /// Gets or creates a per-slot channel for the specified device and slot.
    /// Each slot gets a bounded channel (capacity=1) for sequential command execution.
    /// Thread-safe: multiple workers can call concurrently during initialization.
    /// </summary>
    /// <param name="deviceId">Device identifier (e.g., "Shuttle01").</param>
    /// <param name="slotId">Slot identifier (e.g., 1, 2).</param>
    /// <returns>Bounded channel for slot-specific commands.</returns>
    public Channel<CommandEnvelope> GetOrCreateSlotChannel(string deviceId, int slotId)
    {
        var key = GetSlotChannelKey(deviceId, slotId);
        return _slotChannels.GetOrAdd(
            key,
            _ => ChannelConfiguration.CreateBoundedChannel<CommandEnvelope>(
                ChannelConfiguration.DeviceChannelCapacity));
    }

    /// <summary>
    /// Attempts to retrieve an existing slot channel without creating one.
    /// </summary>
    /// <param name="deviceId">Device identifier.</param>
    /// <param name="slotId">Slot identifier.</param>
    /// <param name="channel">Slot channel if found.</param>
    /// <returns>True if channel exists, false otherwise.</returns>
    public bool TryGetSlotChannel(string deviceId, int slotId, out Channel<CommandEnvelope> channel)
    {
        var key = GetSlotChannelKey(deviceId, slotId);
        return _slotChannels.TryGetValue(key, out channel!);
    }

    /// <summary>
    /// Gets or creates a per-device channel for the specified PLC device ID.
    /// Each device gets a bounded channel (capacity=1) for sequential command execution.
    /// Thread-safe: multiple workers can call concurrently during initialization.
    /// </summary>
    /// <param name="plcDeviceId">PLC device identifier (e.g., "PLC-01")</param>
    /// <returns>Bounded channel for device-specific commands</returns>
    /// <remarks>
    /// DEPRECATED: Use GetOrCreateSlotChannel for multi-slot architecture.
    /// Kept for backward compatibility with DeviceWorker during migration.
    /// </remarks>
    [Obsolete("Use GetOrCreateSlotChannel for multi-slot architecture")]
    public Channel<CommandEnvelope> GetOrCreateDeviceChannel(string plcDeviceId)
    {
        // For backward compatibility, treat device as slot 0
        return GetOrCreateSlotChannel(plcDeviceId, 0);
    }

    /// <summary>
    /// Attempts to retrieve an existing device channel without creating one.
    /// </summary>
    /// <param name="plcDeviceId">PLC device identifier</param>
    /// <param name="channel">Device channel if found</param>
    /// <returns>True if channel exists, false otherwise</returns>
    /// <remarks>
    /// DEPRECATED: Use TryGetSlotChannel for multi-slot architecture.
    /// Kept for backward compatibility with DeviceWorker during migration.
    /// </remarks>
    [Obsolete("Use TryGetSlotChannel for multi-slot architecture")]
    public bool TryGetDeviceChannel(string plcDeviceId, out Channel<CommandEnvelope> channel)
    {
        // For backward compatibility, treat device as slot 0
        return TryGetSlotChannel(plcDeviceId, 0, out channel);
    }

    /// <summary>
    /// Gets all currently registered slot composite IDs (for status queries).
    /// Format: "{deviceId}:Slot{slotId}"
    /// </summary>
    public IReadOnlyCollection<string> GetSlotIds()
    {
        return _slotChannels.Keys.ToList();
    }

    /// <summary>
    /// Gets all unique device IDs from registered slots.
    /// </summary>
    public IReadOnlyCollection<string> GetDeviceIds()
    {
        return _slotChannels.Keys
            .Select(key => key.Split(":Slot")[0])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Gets all slot IDs for a specific device.
    /// </summary>
    /// <param name="deviceId">Device identifier.</param>
    /// <returns>List of slot IDs for the device.</returns>
    public IReadOnlyCollection<int> GetSlotIdsForDevice(string deviceId)
    {
        var prefix = $"{deviceId}:Slot";
        return _slotChannels.Keys
            .Where(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(key =>
            {
                var slotPart = key.Substring(prefix.Length);
                return int.TryParse(slotPart, out var slotId) ? slotId : -1;
            })
            .Where(id => id >= 0)
            .OrderBy(id => id)
            .ToList();
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

        // Complete all slot channels (no more scheduled commands)
        foreach (var slotChannel in _slotChannels.Values)
        {
            slotChannel.Writer.Complete();
        }

        // Allow readers to drain before disposal
        await Task.Delay(100).ConfigureAwait(false);
    }
}
