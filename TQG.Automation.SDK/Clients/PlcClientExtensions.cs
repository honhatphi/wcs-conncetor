using TQG.Automation.SDK.Core;
using TQG.Automation.SDK.Shared;

namespace TQG.Automation.SDK.Clients;

/// <summary>
/// Extension methods for IPlcClient to simplify common write operations.
/// Reduces boilerplate code when writing values to PLC addresses.
/// </summary>
internal static class PlcClientExtensions
{
    /// <summary>
    /// Writes a value to the specified PLC address and logs the operation.
    /// </summary>
    /// <typeparam name="T">The type of value to write (bool, short, int, etc.)</typeparam>
    /// <param name="plcClient">The PLC client instance</param>
    /// <param name="address">The PLC address string (will be parsed automatically)</param>
    /// <param name="value">The value to write</param>
    /// <param name="steps">Optional list to log the operation step</param>
    /// <param name="logMessage">Optional custom log message (if null, a default message will be generated)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public static async Task WriteAsync<T>(
        this IPlcClient plcClient,
        string address,
        T value,
        List<string>? steps = null,
        string? logMessage = null,
        CancellationToken cancellationToken = default)
    {
        var plcAddress = PlcAddress.Parse(address);
        await plcClient.WriteAsync(plcAddress, value, cancellationToken).ConfigureAwait(false);

        if (steps != null)
        {
            var message = logMessage ?? $"Wrote {typeof(T).Name} value '{value}' to {address}";
            steps.Add(message);
        }
    }

    /// <summary>
    /// Writes a Location (Floor, Rail, Block) to the specified PLC addresses.
    /// </summary>
    /// <param name="plcClient">The PLC client instance</param>
    /// <param name="location">The location to write</param>
    /// <param name="floorAddress">Address for Floor component</param>
    /// <param name="railAddress">Address for Rail component</param>
    /// <param name="blockAddress">Address for Block component</param>
    /// <param name="steps">Optional list to log the operation</param>
    /// <param name="locationLabel">Label for the location (e.g., "source", "destination")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public static async Task WriteLocationAsync(
        this IPlcClient plcClient,
        Location location,
        string floorAddress,
        string railAddress,
        string blockAddress,
        List<string>? steps = null,
        string locationLabel = "location",
        CancellationToken cancellationToken = default)
    {
        var floorPlcAddr = PlcAddress.Parse(floorAddress);
        var railPlcAddr = PlcAddress.Parse(railAddress);
        var blockPlcAddr = PlcAddress.Parse(blockAddress);

        await plcClient.WriteAsync(floorPlcAddr, (short)location.Floor, cancellationToken).ConfigureAwait(false);
        await plcClient.WriteAsync(railPlcAddr, (short)location.Rail, cancellationToken).ConfigureAwait(false);
        await plcClient.WriteAsync(blockPlcAddr, (short)location.Block, cancellationToken).ConfigureAwait(false);

        steps?.Add($"Wrote {locationLabel} location: {location}");
    }

    /// <summary>
    /// Writes a Direction value as a boolean to the specified PLC address.
    /// </summary>
    /// <param name="plcClient">The PLC client instance</param>
    /// <param name="address">The PLC address string</param>
    /// <param name="direction">The direction value (null means Bottom/false)</param>
    /// <param name="steps">Optional list to log the operation</param>
    /// <param name="directionLabel">Label for the direction (e.g., "exit", "enter")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public static async Task WriteDirectionAsync(
        this IPlcClient plcClient,
        string address,
        Direction? direction,
        List<string>? steps = null,
        string directionLabel = "direction",
        CancellationToken cancellationToken = default)
    {
        var directionValue = direction?.Equals(Direction.Top) ?? false;
        var plcAddress = PlcAddress.Parse(address);
        await plcClient.WriteAsync(plcAddress, directionValue, cancellationToken).ConfigureAwait(false);

        steps?.Add($"Wrote {directionLabel} direction: {(directionValue ? "Top" : "Bottom")}");
    }

    /// <summary>
    /// Writes a trigger flag (true) to the specified PLC address.
    /// </summary>
    /// <param name="plcClient">The PLC client instance</param>
    /// <param name="address">The PLC address string</param>
    /// <param name="steps">Optional list to log the operation</param>
    /// <param name="triggerLabel">Label for the trigger action</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public static async Task TriggerAsync(
        this IPlcClient plcClient,
        string address,
        List<string>? steps = null,
        string triggerLabel = "trigger",
        CancellationToken cancellationToken = default)
    {
        var plcAddress = PlcAddress.Parse(address);
        await plcClient.WriteAsync(plcAddress, true, cancellationToken).ConfigureAwait(false);

        steps?.Add($"Triggered {triggerLabel}");
    }

    /// <summary>
    /// Writes multiple values as a batch operation with simplified syntax.
    /// Each item is a tuple of (address, value, logMessage).
    /// </summary>
    public static async Task WriteBatchAsync<T>(
        this IPlcClient plcClient,
        IEnumerable<(string address, T value, string? logMessage)> items,
        List<string>? steps = null,
        CancellationToken cancellationToken = default)
    {
        foreach (var (address, value, logMessage) in items)
        {
            await plcClient.WriteAsync(address, value, steps, logMessage, cancellationToken).ConfigureAwait(false);
        }
    }
}
