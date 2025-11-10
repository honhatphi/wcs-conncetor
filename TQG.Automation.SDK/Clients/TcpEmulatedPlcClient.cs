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
    private readonly SemaphoreSlim _ioLock;
    private readonly ConcurrentDictionary<string, byte[]> _dataStore;
    private TcpClient? _tcpClient;
    private StreamWriter? _writer;
    private StreamReader? _reader;
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
        _ioLock = new SemaphoreSlim(1, 1);
        _dataStore = new ConcurrentDictionary<string, byte[]>();
    }

    /// <inheritdoc/>
    public bool IsConnected => _isConnected && !_isDisposed && (_tcpClient?.Connected ?? false);

    /// <inheritdoc/>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_isConnected && _tcpClient?.Connected == true)
                return; // Already connected

            await EnsureConnectionAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ioLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task DisconnectAsync()
    {
        if (_isDisposed)
            return;

        await _ioLock.WaitAsync().ConfigureAwait(false);
        try
        {
            _isConnected = false;
            CloseConnection();
            _dataStore.Clear();
        }
        finally
        {
            _ioLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<T> ReadAsync<T>(PlcAddress address, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var addressStr = FormatAddress(address);
            var response = await SendCommandAsync($"READ {_options.DeviceId} {addressStr}", cancellationToken).ConfigureAwait(false);

            if (!response.StartsWith("OK "))
                throw new PlcConnectionFailedException($"Read failed at {address}: {response}");

            var payload = response[3..].Trim();
            var value = ConvertTo<T>(payload, address.DataType);

            return value;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
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
        finally
        {
            _ioLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task WriteAsync<T>(PlcAddress address, T value, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var addressStr = FormatAddress(address);
            var valueStr = FormatValue(value);
            var response = await SendCommandAsync($"WRITE {_options.DeviceId} {addressStr} {valueStr}", cancellationToken).ConfigureAwait(false);

            if (!response.StartsWith("OK"))
                throw new PlcConnectionFailedException($"Write failed at {address}: {response}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
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
        finally
        {
            _ioLock.Release();
        }
    }

    /// <summary>
    /// Sends a command to the TCP PLC and waits for response with timeout handling.
    /// Thread-safe operation using I/O lock to prevent concurrent command execution.
    /// </summary>
    /// <param name="command">Command to send to the PLC.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Response received from the PLC.</returns>
    private async Task<string> SendCommandAsync(string command, CancellationToken cancellationToken)
    {
        await EnsureConnectionAsync(cancellationToken).ConfigureAwait(false);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_options.OperationTimeout);

        try
        {
            await _writer!.WriteLineAsync(command).ConfigureAwait(false);
            var response = await _reader!.ReadLineAsync(cts.Token).ConfigureAwait(false);
            
            if (response is null)
                return "ERR NoResponse";
            
            return response;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new Exceptions.TimeoutException($"Command '{command}' timed out after {_options.OperationTimeout.TotalSeconds}s.");
        }
    }

    /// <summary>
    /// Ensures TCP connection is established and ready for communication.
    /// Automatically reconnects if current connection is invalid.
    /// </summary>
    private async Task EnsureConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (_tcpClient?.Connected == true && _writer is not null && _reader is not null)
        {
            _isConnected = true;
            return;
        }

        CloseConnection();

        _tcpClient = new TcpClient
        {
            ReceiveTimeout = (int)_options.OperationTimeout.TotalMilliseconds,
            SendTimeout = (int)_options.OperationTimeout.TotalMilliseconds
        };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_options.ConnectTimeout);

        try
        {
            await _tcpClient.ConnectAsync(_options.IpAddress, _options.Port, cts.Token).ConfigureAwait(false);
            
            var stream = _tcpClient.GetStream();
            _writer = new StreamWriter(stream) { AutoFlush = true };
            _reader = new StreamReader(stream);
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
    }

    /// <summary>
    /// Formats PlcAddress to string for TCP protocol.
    /// Example: DB1.DBX0.0 or DB1.DBW10
    /// </summary>
    private static string FormatAddress(PlcAddress address)
    {
        return address.DataType == 'X'
            ? $"DB{address.DataBlock}.DB{address.DataType}{address.Offset}.{address.BitOffset}"
            : $"DB{address.DataBlock}.DB{address.DataType}{address.Offset}";
    }

    /// <summary>
    /// Formats value to string for TCP protocol.
    /// </summary>
    private static string FormatValue<T>(T value)
    {
        return value switch
        {
            bool b => b ? "1" : "0",
            _ => value?.ToString() ?? "0"
        };
    }

    /// <summary>
    /// Converts a string value from TCP PLC response to the specified target type.
    /// Handles common type conversions with performance optimization for basic types.
    /// </summary>
    /// <typeparam name="T">Target type to convert to.</typeparam>
    /// <param name="valueStr">String value received from PLC.</param>
    /// <param name="dataType">PLC data type character (X, B, W, D).</param>
    /// <returns>Converted value of type T.</returns>
    /// <exception cref="InvalidCastException">Thrown when conversion fails.</exception>
    private static T ConvertTo<T>(string valueStr, char dataType)
    {
        if (string.IsNullOrEmpty(valueStr))
            return default!;

        var targetType = typeof(T);

        // Handle common types directly for better performance
        if (targetType == typeof(bool) && bool.TryParse(valueStr, out var boolVal))
            return (T)(object)boolVal;

        if (targetType == typeof(bool) && dataType == 'X')
            return (T)(object)(valueStr == "1");

        if (targetType == typeof(int) && int.TryParse(valueStr, out var intVal))
            return (T)(object)intVal;

        if (targetType == typeof(ushort) && ushort.TryParse(valueStr, out var ushortVal))
            return (T)(object)ushortVal;

        if (targetType == typeof(uint) && uint.TryParse(valueStr, out var uintVal))
            return (T)(object)uintVal;

        if (targetType == typeof(byte) && byte.TryParse(valueStr, out var byteVal))
            return (T)(object)byteVal;

        if (targetType == typeof(string))
            return (T)(object)valueStr;

        // Fallback to general conversion
        try
        {
            return (T)Convert.ChangeType(valueStr, targetType)!;
        }
        catch (Exception ex)
        {
            throw new InvalidCastException($"Cannot convert value '{valueStr}' to type '{targetType.Name}'", ex);
        }
    }

    /// <summary>
    /// Closes the TCP connection and releases all related resources.
    /// </summary>
    private void CloseConnection()
    {
        try { _reader?.Dispose(); } catch { }
        try { _writer?.Dispose(); } catch { }
        try { _tcpClient?.Close(); } catch { }
        _reader = null;
        _writer = null;
        _tcpClient = null;
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

        _dataStore.Clear();
        _ioLock.Dispose();
    }
}
