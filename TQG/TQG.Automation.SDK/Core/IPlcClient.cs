namespace TQG.Automation.SDK.Core;

/// <summary>
/// Defines asynchronous operations for PLC communication.
/// Abstracts both real (S7) and emulated (TCP) PLC connections.
/// </summary>
internal interface IPlcClient : IAsyncDisposable
{
    /// <summary>
    /// Establishes asynchronous connection to the PLC device.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the connection attempt.</returns>
    /// <exception cref="Exceptions.PlcConnectionException">Thrown when connection fails.</exception>
    /// <exception cref="Exceptions.PlcTimeoutException">Thrown when connection times out.</exception>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnects from the PLC device gracefully.
    /// </summary>
    /// <returns>A task representing the disconnection operation.</returns>
    Task DisconnectAsync();

    /// <summary>
    /// Reads a value of type T from the specified PLC address.
    /// </summary>
    /// <typeparam name="T">The expected data type to read.</typeparam>
    /// <param name="address">The PLC memory address to read from.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The value read from the PLC.</returns>
    /// <exception cref="Exceptions.PlcConnectionException">Thrown when not connected.</exception>
    /// <exception cref="Exceptions.PlcInvalidAddressException">Thrown when address is invalid.</exception>
    /// <exception cref="Exceptions.PlcTimeoutException">Thrown when operation times out.</exception>
    /// <exception cref="Exceptions.PlcDataFormatException">Thrown when data type mismatch occurs.</exception>
    Task<T> ReadAsync<T>(PlcAddress address, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a value of type T to the specified PLC address.
    /// </summary>
    /// <typeparam name="T">The data type to write.</typeparam>
    /// <param name="address">The PLC memory address to write to.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the write operation.</returns>
    /// <exception cref="Exceptions.PlcConnectionException">Thrown when not connected.</exception>
    /// <exception cref="Exceptions.PlcInvalidAddressException">Thrown when address is invalid.</exception>
    /// <exception cref="Exceptions.PlcTimeoutException">Thrown when operation times out.</exception>
    /// <exception cref="Exceptions.PlcDataFormatException">Thrown when data type mismatch occurs.</exception>
    Task WriteAsync<T>(PlcAddress address, T value, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether the DLL is properly linked to the PLC program by reading
    /// the SoftwareConnected flag set by the PLC.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>True if SoftwareConnected flag is set; otherwise false.</returns>
    /// <exception cref="Exceptions.PlcConnectionException">Thrown when not connected or read operation fails.</exception>
    Task<bool> IsLinkEstablishedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether the PLC device is ready to accept commands by reading
    /// the DeviceReady flag.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>True if DeviceReady flag is set; otherwise false.</returns>
    /// <exception cref="Exceptions.PlcConnectionException">Thrown when not connected or read operation fails.</exception>
    Task<bool> IsDeviceReadyAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current connection status.
    /// </summary>
    bool IsConnected { get; }
}
