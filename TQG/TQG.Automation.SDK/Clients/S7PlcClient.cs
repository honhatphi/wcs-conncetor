using S7.Net;
using System.Net.Sockets;
using PlcException = TQG.Automation.SDK.Exceptions.PlcException;
using PlcConnectionException = TQG.Automation.SDK.Exceptions.PlcConnectionException;
using PlcTimeoutException = TQG.Automation.SDK.Exceptions.PlcTimeoutException;
using PlcInvalidAddressException = TQG.Automation.SDK.Exceptions.PlcInvalidAddressException;
using PlcDataFormatException = TQG.Automation.SDK.Exceptions.PlcDataFormatException;
using TQG.Automation.SDK.Core;
using TQG.Automation.SDK.Models;

namespace TQG.Automation.SDK.Clients;

/// <summary>
/// PLC client implementation for real hardware using S7.NET Plus library.
/// Thread-safe operations using SemaphoreSlim for concurrent read/write protection.
/// </summary>
internal sealed class S7PlcClient : IPlcClient
{
    private readonly PlcConnectionOptions _options;
    private readonly SemaphoreSlim _operationLock;
    private Plc? _plc;
    private bool _isDisposed;

    /// <summary>
    /// Initializes a new instance of the S7PlcClient class.
    /// </summary>
    /// <param name="options">Connection configuration options.</param>
    /// <exception cref="ArgumentNullException">Thrown when options is null.</exception>
    public S7PlcClient(PlcConnectionOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
        _operationLock = new SemaphoreSlim(1, 1);
    }

    /// <inheritdoc/>
    public bool IsConnected => _plc?.IsConnected ?? false;

    /// <inheritdoc/>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_plc?.IsConnected == true)
                return; // Already connected

            // Close existing instance if any
            if (_plc != null)
            {
                try { _plc.Close(); } catch { }
                _plc = null;
            }

            // Map PLC type from options (default to S7-1200 if not specified)
            var cpuType = CpuType.S71200;

            _plc = new Plc(
                cpuType,
                _options.IpAddress,
                (short)_options.Rack,
                (short)_options.Slot
            )
            {
                ReadTimeout = (int)_options.OperationTimeout.TotalMilliseconds,
                WriteTimeout = (int)_options.OperationTimeout.TotalMilliseconds
            };

            // Connect with timeout
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_options.ConnectTimeout);

            try
            {
                await Task.Run(() => _plc.Open(), cts.Token);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new PlcTimeoutException($"Connection to {_options.DeviceId} ({_options.IpAddress}) timed out after {_options.ConnectTimeout.TotalSeconds}s.");
            }
            catch (SocketException ex)
            {
                throw new PlcConnectionException($"Failed to connect to {_options.DeviceId} ({_options.IpAddress}): {ex.Message}", ex);
            }
            catch (Exception ex) when (ex is not PlcException)
            {
                throw new PlcConnectionException($"Unexpected error connecting to {_options.DeviceId}: {ex.Message}", ex);
            }

            if (!_plc.IsConnected)
                throw new PlcConnectionException($"Failed to establish connection to {_options.DeviceId} ({_options.IpAddress}).");
        }
        finally
        {
            _operationLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task DisconnectAsync()
    {
        if (_isDisposed || _plc == null)
            return;

        await _operationLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_plc.IsConnected)
            {
                _plc.Close();
            }
        }
        catch
        {
            // Suppress exceptions during disconnect
        }
        finally
        {
            _operationLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<T> ReadAsync<T>(PlcAddress address, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!IsConnected)
                throw new PlcConnectionException($"Not connected to {_options.DeviceId}. Call ConnectAsync first.");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_options.OperationTimeout);

            try
            {
                object? result = await Task.Run(() => ReadValueFromPlc(address), cts.Token);

                if (result is T typedResult)
                    return typedResult;

                // Attempt conversion
                try
                {
                    return (T)Convert.ChangeType(result, typeof(T))!;
                }
                catch (Exception ex)
                {
                    throw new PlcDataFormatException(
                        $"Cannot convert value of type {result?.GetType().Name ?? "null"} to {typeof(T).Name} for address {address}.",
                        ex);
                }
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new PlcTimeoutException($"Read operation timed out for {_options.DeviceId} at {address} after {_options.OperationTimeout.TotalSeconds}s.");
            }
            catch (SocketException ex)
            {
                throw new PlcConnectionException($"Connection lost to {_options.DeviceId}: {ex.Message}", ex);
            }
            catch (Exception ex) when (ex is not PlcException)
            {
                throw new PlcConnectionException($"Error reading from {_options.DeviceId} at {address}: {ex.Message}", ex);
            }
        }
        finally
        {
            _operationLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task WriteAsync<T>(PlcAddress address, T value, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!IsConnected)
                throw new PlcConnectionException($"Not connected to {_options.DeviceId}. Call ConnectAsync first.");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_options.OperationTimeout);

            try
            {
                await Task.Run(() => WriteValueToPlc(address, value), cts.Token);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new PlcTimeoutException($"Write operation timed out for {_options.DeviceId} at {address} after {_options.OperationTimeout.TotalSeconds}s.");
            }
            catch (SocketException ex)
            {
                throw new PlcConnectionException($"Connection lost to {_options.DeviceId}: {ex.Message}", ex);
            }
            catch (Exception ex) when (ex is not PlcException)
            {
                throw new PlcConnectionException($"Error writing to {_options.DeviceId} at {address}: {ex.Message}", ex);
            }
        }
        finally
        {
            _operationLock.Release();
        }
    }

    /// <summary>
    /// Reads a value from the PLC using S7.NET Plus library.
    /// </summary>
    private object ReadValueFromPlc(PlcAddress address)
    {
        var dataType = address.DataType switch
        {
            'X' => VarType.Bit,
            'B' => VarType.Byte,
            'W' => VarType.Word,
            'D' => VarType.DWord,
            _ => throw new PlcInvalidAddressException($"Unsupported data type: {address.DataType}")
        };

        // For bit access, use byte offset + bit offset
        if (dataType == VarType.Bit)
        {
            var result = _plc!.Read($"DB{address.DataBlock}.DBB{address.Offset}");
            var byteValue = result != null ? (byte)result : (byte)0;
            return (byteValue & 1 << address.BitOffset) != 0;
        }

        // For other types, read directly
        var dbAddress = $"DB{address.DataBlock}.DB{address.DataType}{address.Offset}";
        var value = _plc!.Read(dbAddress);
        return value ?? throw new PlcDataFormatException($"Read returned null for address {address}");
    }

    /// <summary>
    /// Writes a value to the PLC using S7.NET Plus library.
    /// </summary>
    private void WriteValueToPlc<T>(PlcAddress address, T value)
    {
        var dataType = address.DataType switch
        {
            'X' => VarType.Bit,
            'B' => VarType.Byte,
            'W' => VarType.Word,
            'D' => VarType.DWord,
            _ => throw new PlcInvalidAddressException($"Unsupported data type: {address.DataType}")
        };

        // For bit access, read-modify-write
        if (dataType == VarType.Bit)
        {
            if (value is not bool boolValue)
                throw new PlcDataFormatException($"Bit address requires boolean value, got {typeof(T).Name}.");

            var dbAddress = $"DB{address.DataBlock}.DBB{address.Offset}";
            var result = _plc!.Read(dbAddress);
            var byteValue = result != null ? (byte)result : (byte)0;

            if (boolValue)
                byteValue |= (byte)(1 << address.BitOffset);
            else
                byteValue &= (byte)~(1 << address.BitOffset);

            _plc.Write(dbAddress, byteValue);
            return;
        }

        // For other types, write directly
        var writeAddress = $"DB{address.DataBlock}.DB{address.DataType}{address.Offset}";
        _plc!.Write(writeAddress, value!);
    }

    /// <inheritdoc/>
    public async Task<bool> IsLinkEstablishedAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (!IsConnected)
            throw new PlcConnectionException($"Cannot check link status: not connected to {_options.DeviceId}.");

        try
        {
            var softwareConnectedAddress = PlcAddress.Parse(_options.SignalMap.SoftwareConnected);
            return await ReadAsync<bool>(softwareConnectedAddress, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not PlcException)
        {
            throw new PlcConnectionException($"Failed to check link status for {_options.DeviceId}: {ex.Message}", ex);
        }
    }

    /// <inheritdoc/>
    public async Task<bool> IsDeviceReadyAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (!IsConnected)
            throw new PlcConnectionException($"Cannot check device ready status: not connected to {_options.DeviceId}.");

        try
        {
            var deviceReadyAddress = PlcAddress.Parse(_options.SignalMap.DeviceReady);
            return await ReadAsync<bool>(deviceReadyAddress, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not PlcException)
        {
            throw new PlcConnectionException($"Failed to check device ready status for {_options.DeviceId}: {ex.Message}", ex);
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;

        try
        {
            await DisconnectAsync().ConfigureAwait(false);
        }
        catch
        {
            // Suppress exceptions during disposal
        }

        if (_plc != null)
        {
            try { _plc.Close(); } catch { }
            _plc = null;
        }

        _operationLock.Dispose();
    }
}
