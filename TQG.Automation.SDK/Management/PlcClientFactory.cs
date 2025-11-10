using TQG.Automation.SDK.Clients;
using TQG.Automation.SDK.Core;
using TQG.Automation.SDK.Shared;

namespace TQG.Automation.SDK.Management;

/// <summary>
/// Factory for creating PLC client instances based on connection mode.
/// Encapsulates the instantiation logic for different client types.
/// </summary>
internal sealed class PlcClientFactory
{
    /// <summary>
    /// Creates a PLC client instance based on the specified options.
    /// </summary>
    /// <param name="options">Connection configuration options.</param>
    /// <returns>An IPlcClient instance (S7PlcClient or TcpEmulatedPlcClient).</returns>
    /// <exception cref="ArgumentNullException">Thrown when options is null.</exception>
    /// <exception cref="ArgumentException">Thrown when PlcMode is not supported.</exception>
    public static IPlcClient Create(PlcConnectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        return options.Mode switch
        {
            PlcMode.Real => new S7PlcClient(options),
            PlcMode.Emulated => new TcpEmulatedPlcClient(options),
            _ => throw new ArgumentException($"Unsupported PLC mode: {options.Mode}", nameof(options))
        };
    }
}
