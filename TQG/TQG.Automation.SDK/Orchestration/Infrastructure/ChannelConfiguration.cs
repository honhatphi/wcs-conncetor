using System.Threading.Channels;

namespace TQG.Automation.SDK.Orchestration.Infrastructure;

/// <summary>
/// Configuration constants and factory methods for orchestration channels.
/// All channels use FIFO ordering and drop behavior on overflow.
/// </summary>
internal static class ChannelConfiguration
{
    /// <summary>
    /// Maximum capacity for the input channel (command submissions from external API).
    /// Bounded to prevent unbounded memory growth under heavy load.
    /// </summary>
    public const int InputChannelCapacity = 20;

    /// <summary>
    /// Maximum capacity for each per-device channel (scheduled commands awaiting execution).
    /// Set to 1 to ensure strict sequential execution per device.
    /// </summary>
    public const int DeviceChannelCapacity = 1;

    /// <summary>
    /// Creates a bounded channel for command input with specified capacity.
    /// When full, new writes will wait until space is available.
    /// </summary>
    /// <typeparam name="T">Channel item type</typeparam>
    /// <param name="capacity">Maximum channel capacity</param>
    /// <returns>Bounded channel with SingleWriter=false, SingleReader=false</returns>
    public static Channel<T> CreateBoundedChannel<T>(int capacity)
    {
        var options = new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait, // Block on full (backpressure)
            SingleWriter = false, // Multiple writers allowed (e.g., AutomationGateway threads)
            SingleReader = false  // Multiple readers allowed (e.g., Matchmaker + cleanup tasks)
        };

        return Channel.CreateBounded<T>(options);
    }

    /// <summary>
    /// Creates an unbounded channel for low-volume or result streams.
    /// Use for AvailabilityChannel (ReadyTickets) and ResultChannel (CommandResults).
    /// </summary>
    /// <typeparam name="T">Channel item type</typeparam>
    /// <returns>Unbounded channel with SingleWriter=false, SingleReader=false</returns>
    public static Channel<T> CreateUnboundedChannel<T>()
    {
        var options = new UnboundedChannelOptions
        {
            SingleWriter = false, // Multiple workers/executors can write
            SingleReader = false  // ReplyHub + status queries can read
        };

        return Channel.CreateUnbounded<T>(options);
    }
}
