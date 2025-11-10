using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using TQG.Automation.SDK.Core;
using TQG.Automation.SDK.Exceptions;
using TQG.Automation.SDK.Shared;

namespace TQG.Automation.SDK.Clients;

/// <summary>
/// TCP PLC client that connects to a real TCP server.
/// Supports both real TCP connection and in-memory emulation mode.
/// Thread-safe operations using SemaphoreSlim for concurrent read/write protection.
/// </summary>
internal sealed class TcpEmulatedPlcClient : IPlcClient
{
    private readonly PlcConnectionOptions _options;
    private readonly SemaphoreSlim _operationLock;
    private readonly ConcurrentDictionary<string, byte[]> _dataStore;
    private TcpClient? _tcpClient;
    private NetworkStream? _networkStream;
    private bool _isConnected;
    private bool _isDisposed;

    // Default data block sizes (can be configured)
    private const int DefaultDataBlockSize = 65536; // 64KB per DB

    /// <summary>
    /// Initializes a new instance of the TcpEmulatedPlcClient class.
    /// </summary>
    /// <param name="options">Connection configuration options.</param>
    /// <exception cref="ArgumentNullException">Thrown when options is null.</exception>
    public TcpEmulatedPlcClient(PlcConnectionOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
        _operationLock = new SemaphoreSlim(1, 1);
        _dataStore = new ConcurrentDictionary<string, byte[]>();
    }

    /// <inheritdoc/>
    public bool IsConnected => _isConnected && !_isDisposed && (_tcpClient?.Connected ?? false);

    /// <inheritdoc/>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_isConnected && _tcpClient?.Connected == true)
                return; // Already connected

            // Close existing connection if any
            if (_tcpClient != null)
            {
                try
                {
                    _networkStream?.Close();
                    _tcpClient.Close();
                }
                catch { }
                finally
                {
                    _networkStream?.Dispose();
                    _tcpClient?.Dispose();
                    _networkStream = null;
                    _tcpClient = null;
                }
            }

            // Create new TCP client
            _tcpClient = new TcpClient
            {
                ReceiveTimeout = (int)_options.OperationTimeout.TotalMilliseconds,
                SendTimeout = (int)_options.OperationTimeout.TotalMilliseconds
            };

            // Connect with timeout
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_options.ConnectTimeout);

            try
            {
                await _tcpClient.ConnectAsync(_options.IpAddress, _options.Port, cts.Token).ConfigureAwait(false);
                _networkStream = _tcpClient.GetStream();
                _isConnected = true;
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new PlcConnectionFailedException($"Connection to {_options.DeviceId} ({_options.IpAddress}:{_options.Port}) timed out after {_options.ConnectTimeout.TotalSeconds}s.");
            }
            catch (SocketException ex)
            {
                throw new PlcConnectionFailedException($"Failed to connect to {_options.DeviceId} ({_options.IpAddress}:{_options.Port}): {ex.Message}", ex);
            }
            catch (Exception ex) when (ex is not PlcException)
            {
                throw new PlcConnectionFailedException($"Unexpected error connecting to {_options.DeviceId}: {ex.Message}", ex);
            }

            if (!_tcpClient.Connected)
                throw new PlcConnectionFailedException($"Failed to establish connection to {_options.DeviceId} ({_options.IpAddress}:{_options.Port}).");
        }
        finally
        {
            _operationLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task DisconnectAsync()
    {
        if (_isDisposed)
            return;

        await _operationLock.WaitAsync().ConfigureAwait(false);
        try
        {
            _isConnected = false;

            if (_networkStream != null)
            {
                try
                {
                    await _networkStream.FlushAsync().ConfigureAwait(false);
                    _networkStream.Close();
                }
                catch { }
                finally
                {
                    _networkStream.Dispose();
                    _networkStream = null;
                }
            }

            if (_tcpClient != null)
            {
                try
                {
                    _tcpClient.Close();
                }
                catch { }
                finally
                {
                    _tcpClient.Dispose();
                    _tcpClient = null;
                }
            }

            _dataStore.Clear();
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
                throw new PlcConnectionFailedException($"Not connected to TCP PLC {_options.DeviceId}. Call ConnectAsync first.");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_options.OperationTimeout);

            try
            {
                // Send read request to TCP server
                var request = BuildReadRequest(address);
                await SendRequestAsync(request, cts.Token).ConfigureAwait(false);

                // Receive response from TCP server
                var response = await ReceiveResponseAsync(cts.Token).ConfigureAwait(false);
                var value = ParseReadResponse(response, address);

                if (value is T typedValue)
                    return typedValue;

                // Attempt conversion
                try
                {
                    return (T)Convert.ChangeType(value, typeof(T))!;
                }
                catch (Exception ex)
                {
                    throw new PlcDataFormatException(
                        $"Cannot convert value of type {value?.GetType().Name ?? "null"} to {typeof(T).Name} for address {address}.",
                        ex);
                }
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new Exceptions.TimeoutException($"Read operation timed out for {_options.DeviceId} at {address} after {_options.OperationTimeout.TotalSeconds}s.");
            }
            catch (SocketException ex)
            {
                throw new PlcConnectionFailedException($"Connection lost to {_options.DeviceId}: {ex.Message}", ex);
            }
            catch (Exception ex) when (ex is not PlcException)
            {
                throw new PlcConnectionFailedException($"Error reading from {_options.DeviceId} at {address}: {ex.Message}", ex);
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
                throw new PlcConnectionFailedException($"Not connected to TCP PLC {_options.DeviceId}. Call ConnectAsync first.");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_options.OperationTimeout);

            try
            {
                // Send write request to TCP server
                var request = BuildWriteRequest(address, value);
                await SendRequestAsync(request, cts.Token).ConfigureAwait(false);

                // Receive response from TCP server
                var response = await ReceiveResponseAsync(cts.Token).ConfigureAwait(false);
                ParseWriteResponse(response, address);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new Exceptions.TimeoutException($"Write operation timed out for {_options.DeviceId} at {address} after {_options.OperationTimeout.TotalSeconds}s.");
            }
            catch (SocketException ex)
            {
                throw new PlcConnectionFailedException($"Connection lost to {_options.DeviceId}: {ex.Message}", ex);
            }
            catch (Exception ex) when (ex is not PlcException)
            {
                throw new PlcConnectionFailedException($"Error writing to {_options.DeviceId} at {address}: {ex.Message}", ex);
            }
        }
        finally
        {
            _operationLock.Release();
        }
    }

    /// <summary>
    /// Builds a TCP read request message.
    /// Format: READ|DB{dbNumber}.DB{dataType}{offset}.{bitOffset}
    /// Example: READ|DB1.DBX0.0
    /// </summary>
    private static string BuildReadRequest(PlcAddress address)
    {
        var addressStr = address.DataType == 'X'
            ? $"DB{address.DataBlock}.DB{address.DataType}{address.Offset}.{address.BitOffset}"
            : $"DB{address.DataBlock}.DB{address.DataType}{address.Offset}";
        
        return $"READ|{addressStr}\n";
    }

    /// <summary>
    /// Builds a TCP write request message.
    /// Format: WRITE|DB{dbNumber}.DB{dataType}{offset}.{bitOffset}|{value}
    /// Example: WRITE|DB1.DBX0.0|1
    /// </summary>
    private static string BuildWriteRequest<T>(PlcAddress address, T value)
    {
        var addressStr = address.DataType == 'X'
            ? $"DB{address.DataBlock}.DB{address.DataType}{address.Offset}.{address.BitOffset}"
            : $"DB{address.DataBlock}.DB{address.DataType}{address.Offset}";

        var valueStr = value switch
        {
            bool b => b ? "1" : "0",
            _ => value?.ToString() ?? "0"
        };

        return $"WRITE|{addressStr}|{valueStr}\n";
    }

    /// <summary>
    /// Sends a request to the TCP server.
    /// </summary>
    private async Task SendRequestAsync(string request, CancellationToken cancellationToken)
    {
        if (_networkStream == null)
            throw new PlcConnectionFailedException("Network stream is not available.");

        var buffer = Encoding.UTF8.GetBytes(request);
        await _networkStream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        await _networkStream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Receives a response from the TCP server.
    /// </summary>
    private async Task<string> ReceiveResponseAsync(CancellationToken cancellationToken)
    {
        if (_networkStream == null)
            throw new PlcConnectionFailedException("Network stream is not available.");

        var buffer = new byte[4096];
        var bytesRead = await _networkStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        
        if (bytesRead == 0)
            throw new PlcConnectionFailedException("Connection closed by remote host.");

        return Encoding.UTF8.GetString(buffer, 0, bytesRead).TrimEnd('\r', '\n');
    }

    /// <summary>
    /// Parses a read response from the TCP server.
    /// Expected format: OK|{value} or ERROR|{message}
    /// </summary>
    private static object ParseReadResponse(string response, PlcAddress address)
    {
        var parts = response.Split('|');
        
        if (parts.Length < 2)
            throw new PlcDataFormatException($"Invalid response format: {response}");

        if (parts[0] != "OK")
            throw new PlcConnectionFailedException($"Server error: {parts[1]}");

        var valueStr = parts[1];

        return address.DataType switch
        {
            'X' => valueStr == "1" || valueStr.Equals("true", StringComparison.OrdinalIgnoreCase),
            'B' => byte.Parse(valueStr),
            'W' => ushort.Parse(valueStr),
            'D' => uint.Parse(valueStr),
            _ => throw new PlcInvalidAddressException($"Unsupported data type: {address.DataType}")
        };
    }

    /// <summary>
    /// Parses a write response from the TCP server.
    /// Expected format: OK or ERROR|{message}
    /// </summary>
    private static void ParseWriteResponse(string response, PlcAddress address)
    {
        var parts = response.Split('|');
        
        if (parts[0] != "OK")
            throw new PlcConnectionFailedException($"Server write error at {address}: {(parts.Length > 1 ? parts[1] : "Unknown error")}");
    }

    /// <summary>
    /// Gets or creates a data block in the emulated memory (for fallback/caching).
    /// </summary>
    private byte[] GetOrCreateDataBlock(int dbNumber)
    {
        var key = $"DB{dbNumber}";
        return _dataStore.GetOrAdd(key, _ => new byte[DefaultDataBlockSize]);
    }

    /// <summary>
    /// Reads a value from the emulated data block.
    /// </summary>
    private static object ReadValueFromDataBlock(byte[] dataBlock, PlcAddress address)
    {
        ValidateAddress(dataBlock, address);

        return address.DataType switch
        {
            'X' => ReadBit(dataBlock, address.Offset, address.BitOffset),
            'B' => ReadByte(dataBlock, address.Offset),
            'W' => ReadWord(dataBlock, address.Offset),
            'D' => ReadDWord(dataBlock, address.Offset),
            _ => throw new PlcInvalidAddressException($"Unsupported data type: {address.DataType}")
        };
    }

    /// <summary>
    /// Writes a value to the emulated data block.
    /// </summary>
    private static void WriteValueToDataBlock<T>(byte[] dataBlock, PlcAddress address, T value)
    {
        ValidateAddress(dataBlock, address);

        switch (address.DataType)
        {
            case 'X':
                if (value is not bool boolValue)
                    throw new PlcDataFormatException($"Bit address requires boolean value, got {typeof(T).Name}.");
                WriteBit(dataBlock, address.Offset, address.BitOffset, boolValue);
                break;

            case 'B':
                if (value is not byte byteValue)
                    throw new PlcDataFormatException($"Byte address requires byte value, got {typeof(T).Name}.");
                WriteByte(dataBlock, address.Offset, byteValue);
                break;

            case 'W':
                if (value is not ushort and not short and not int)
                    throw new PlcDataFormatException($"Word address requires ushort/short/int value, got {typeof(T).Name}.");
                WriteWord(dataBlock, address.Offset, Convert.ToUInt16(value));
                break;

            case 'D':
                if (value is not uint and not int and not float)
                    throw new PlcDataFormatException($"DWord address requires uint/int/float value, got {typeof(T).Name}.");
                WriteDWord(dataBlock, address.Offset, Convert.ToUInt32(value));
                break;

            default:
                throw new PlcInvalidAddressException($"Unsupported data type: {address.DataType}");
        }
    }

    /// <summary>
    /// Validates that the address is within the data block bounds.
    /// </summary>
    private static void ValidateAddress(byte[] dataBlock, PlcAddress address)
    {
        var requiredSize = address.DataType switch
        {
            'X' => address.Offset + 1,
            'B' => address.Offset + 1,
            'W' => address.Offset + 2,
            'D' => address.Offset + 4,
            _ => throw new PlcInvalidAddressException($"Unsupported data type: {address.DataType}")
        };

        if (requiredSize > dataBlock.Length)
            throw new PlcInvalidAddressException($"Address {address} exceeds data block size ({dataBlock.Length} bytes).");
    }

    // Bit operations
    private static bool ReadBit(byte[] data, int offset, int bitOffset)
        => (data[offset] & 1 << bitOffset) != 0;

    private static void WriteBit(byte[] data, int offset, int bitOffset, bool value)
    {
        if (value)
            data[offset] |= (byte)(1 << bitOffset);
        else
            data[offset] &= (byte)~(1 << bitOffset);
    }

    // Byte operations
    private static byte ReadByte(byte[] data, int offset)
        => data[offset];

    private static void WriteByte(byte[] data, int offset, byte value)
        => data[offset] = value;

    // Word operations (big-endian, S7 convention)
    private static ushort ReadWord(byte[] data, int offset)
        => (ushort)(data[offset] << 8 | data[offset + 1]);

    private static void WriteWord(byte[] data, int offset, ushort value)
    {
        data[offset] = (byte)(value >> 8);
        data[offset + 1] = (byte)(value & 0xFF);
    }

    // DWord operations (big-endian, S7 convention)
    private static uint ReadDWord(byte[] data, int offset)
        => (uint)(data[offset] << 24 | data[offset + 1] << 16 |
                  data[offset + 2] << 8 | data[offset + 3]);

    private static void WriteDWord(byte[] data, int offset, uint value)
    {
        data[offset] = (byte)(value >> 24);
        data[offset + 1] = (byte)(value >> 16 & 0xFF);
        data[offset + 2] = (byte)(value >> 8 & 0xFF);
        data[offset + 3] = (byte)(value & 0xFF);
    }

    /// <summary>
    /// Resets all emulated data blocks (useful for testing).
    /// </summary>
    public void ResetDataStore()
    {
        _dataStore.Clear();
    }

    /// <summary>
    /// Gets the current data store for inspection (testing purposes).
    /// </summary>
    public IReadOnlyDictionary<string, byte[]> DataStore => _dataStore;

    /// <inheritdoc/>
    public async Task<bool> IsLinkEstablishedAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (!IsConnected)
            throw new PlcConnectionFailedException($"Cannot check link status: not connected to {_options.DeviceId}.");

        try
        {
            // Emulated mode: Auto-set SoftwareConnected flag to simulate PLC program behavior
            var softwareConnectedAddress = PlcAddress.Parse(_options.SignalMap.SoftwareConnected);
            await WriteAsync(softwareConnectedAddress, true, cancellationToken).ConfigureAwait(false);

            // Read back to return status
            return await ReadAsync<bool>(softwareConnectedAddress, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not PlcException)
        {
            throw new PlcConnectionFailedException($"Failed to check link status for {_options.DeviceId}: {ex.Message}", ex);
        }
    }

    /// <inheritdoc/>
    public async Task<bool> IsDeviceReadyAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (!IsConnected)
            throw new PlcConnectionFailedException($"Cannot check device ready status: not connected to {_options.DeviceId}.");

        try
        {
            // Emulated mode: Auto-set DeviceReady flag to simulate PLC program behavior
            var deviceReadyAddress = PlcAddress.Parse(_options.SignalMap.DeviceReady);
            await WriteAsync(deviceReadyAddress, true, cancellationToken).ConfigureAwait(false);

            // Read back to return status
            return await ReadAsync<bool>(deviceReadyAddress, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not PlcException)
        {
            throw new PlcConnectionFailedException($"Failed to check device ready status for {_options.DeviceId}: {ex.Message}", ex);
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

        _networkStream?.Dispose();
        _tcpClient?.Dispose();
        _dataStore.Clear();
        _operationLock.Dispose();
    }
}
