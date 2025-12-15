using TQG.Automation.SDK.Core;
using TQG.Automation.SDK.Events;
using TQG.Automation.SDK.Exceptions;
using TQG.Automation.SDK.Orchestration.Infrastructure;
using TQG.Automation.SDK.Orchestration.Workers;
using TQG.Automation.SDK.Shared;

namespace TQG.Automation.SDK.Management;

/// <summary>
/// Manages the lifecycle of a single PLC connection with multiple slots,
/// including health monitoring and automatic reconnection.
/// One IPlcClient is shared across all SlotWorkers for the device.
/// </summary>
internal sealed class PlcConnectionManager : IAsyncDisposable
{
    private readonly IPlcClient _client;
    private readonly PlcConnectionOptions _options;
    private readonly SemaphoreSlim _operationLock;
    private readonly CancellationTokenSource _disposalCts;
    private readonly Dictionary<int, SlotWorker> _slotWorkers;

    private PeriodicTimer? _healthCheckTimer;
    private Task? _healthCheckTask;
    private CancellationTokenSource? _healthCheckCts;
    private bool _isDisposed;
    private int _reconnectAttempts;

    /// <summary>
    /// Initializes a new instance of the PlcConnectionManager class.
    /// </summary>
    /// <param name="client">The PLC client to manage (shared across all slots).</param>
    /// <param name="options">Connection configuration options including slot configurations.</param>
    public PlcConnectionManager(IPlcClient client, PlcConnectionOptions options)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();

        _operationLock = new SemaphoreSlim(1, 1);
        _disposalCts = new CancellationTokenSource();
        _slotWorkers = new Dictionary<int, SlotWorker>();
    }

    /// <summary>
    /// Gets the PLC client managed by this manager.
    /// Shared across all slots of this device.
    /// </summary>
    public IPlcClient Client => _client;

    /// <summary>
    /// Gets the connection options for this manager.
    /// </summary>
    public PlcConnectionOptions Options => _options;

    /// <summary>
    /// Gets whether the client is currently connected.
    /// Single connection status for all slots.
    /// </summary>
    public bool IsConnected => _client.IsConnected;

    /// <summary>
    /// Gets the slot workers dictionary.
    /// Key: SlotId, Value: SlotWorker
    /// </summary>
    public IReadOnlyDictionary<int, SlotWorker> SlotWorkers => _slotWorkers;

    /// <summary>
    /// Gets a specific slot worker by slot ID.
    /// </summary>
    /// <param name="slotId">The slot identifier.</param>
    /// <returns>The SlotWorker if found; otherwise null.</returns>
    public SlotWorker? GetSlotWorker(int slotId)
    {
        return _slotWorkers.GetValueOrDefault(slotId);
    }

    /// <summary>
    /// Initializes slot workers for all configured slots.
    /// Must be called after construction and before connecting.
    /// </summary>
    /// <param name="channels">Orchestrator channels for communication.</param>
    /// <param name="barcodeValidationCallback">Callback for barcode validation during inbound operations.</param>
    public void InitializeSlotWorkers(
        OrchestratorChannels channels,
        Func<BarcodeReceivedEventArgs, CancellationToken, Task<BarcodeValidationResponse>> barcodeValidationCallback)
    {
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(barcodeValidationCallback);

        if (_slotWorkers.Count > 0)
            return; // Already initialized

        foreach (var slotConfig in _options.Slots)
        {
            var slotWorker = new SlotWorker(
                _client,
                _options,
                slotConfig,
                channels,
                barcodeValidationCallback);

            _slotWorkers[slotConfig.SlotId] = slotWorker;
        }
    }

    /// <summary>
    /// Connects to the PLC and starts health monitoring.
    /// All slots share this single connection.
    /// </summary>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_client.IsConnected)
                return;

            await _client.ConnectAsync(cancellationToken).ConfigureAwait(false);
            _reconnectAttempts = 0;

            // Start health check monitoring
            StartHealthMonitoring();
        }
        finally
        {
            _operationLock.Release();
        }
    }

    /// <summary>
    /// Starts all slot workers.
    /// Must be called after ConnectAsync and InitializeSlotWorkers.
    /// </summary>
    public void StartSlotWorkers()
    {
        foreach (var worker in _slotWorkers.Values)
        {
            worker.Start();
        }
    }

    /// <summary>
    /// Stops all slot workers.
    /// </summary>
    public async Task StopSlotWorkersAsync()
    {
        var stopTasks = _slotWorkers.Values.Select(w => w.StopAsync());
        await Task.WhenAll(stopTasks).ConfigureAwait(false);
    }

    /// <summary>
    /// Disconnects from the PLC and stops health monitoring.
    /// Stops all slot workers first before disconnecting.
    /// </summary>
    public async Task DisconnectAsync()
    {
        if (_isDisposed)
            return;

        await _operationLock.WaitAsync().ConfigureAwait(false);
        try
        {
            // Stop all slot workers first
            await StopSlotWorkersAsync().ConfigureAwait(false);

            // Stop health monitoring
            await StopHealthMonitoringAsync().ConfigureAwait(false);

            // Disconnect single client
            await _client.DisconnectAsync().ConfigureAwait(false);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    /// <summary>
    /// Reads a value from the PLC with automatic reconnection on connection loss.
    /// </summary>
    public async Task<T> ReadAsync<T>(PlcAddress address, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await _client.ReadAsync<T>(address, cancellationToken).ConfigureAwait(false);
        }
        catch (PlcConnectionFailedException)
        {
            // Trigger reconnect on next health check
            _reconnectAttempts = 0;
            throw;
        }
        finally
        {
            _operationLock.Release();
        }
    }

    /// <summary>
    /// Writes a value to the PLC with automatic reconnection on connection loss.
    /// </summary>
    public async Task WriteAsync<T>(PlcAddress address, T value, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _client.WriteAsync(address, value, cancellationToken).ConfigureAwait(false);
        }
        catch (PlcConnectionFailedException)
        {
            // Trigger reconnect on next health check
            _reconnectAttempts = 0;
            throw;
        }
        finally
        {
            _operationLock.Release();
        }
    }

    /// <summary>
    /// Starts background health monitoring with periodic connection checks.
    /// </summary>
    private void StartHealthMonitoring()
    {
        if (_healthCheckTimer != null)
            return;

        _healthCheckCts = new CancellationTokenSource();
        _healthCheckTimer = new PeriodicTimer(_options.HealthCheckInterval);
        _healthCheckTask = Task.Run(() => HealthCheckLoopAsync(_healthCheckCts.Token), CancellationToken.None);
    }

    /// <summary>
    /// Stops background health monitoring.
    /// </summary>
    private async Task StopHealthMonitoringAsync()
    {
        if (_healthCheckTimer == null || _healthCheckTask == null)
            return;

        // Cancel the health check loop
        _healthCheckCts?.Cancel();

        // Dispose timer to unblock WaitForNextTickAsync
        _healthCheckTimer.Dispose();

        // Wait for task to complete
        try
        {
            await _healthCheckTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is triggered
        }
        catch
        {
            // Ignore other exceptions during shutdown
        }

        _healthCheckCts?.Dispose();
        _healthCheckCts = null;
        _healthCheckTimer = null;
        _healthCheckTask = null;
    }

    /// <summary>
    /// Background loop for periodic health checks and automatic reconnection.
    /// </summary>
    private async Task HealthCheckLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && _healthCheckTimer != null)
            {
                try
                {
                    await _healthCheckTimer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false);

                    if (cancellationToken.IsCancellationRequested)
                        break;

                    // Check connection status
                    if (!_client.IsConnected)
                    {
                        await AttemptReconnectAsync().ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // Suppress exceptions in background task
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
    }

    /// <summary>
    /// Attempts to reconnect with exponential backoff.
    /// </summary>
    private async Task AttemptReconnectAsync()
    {
        if (_reconnectAttempts >= _options.MaxReconnectAttempts)
            return;

        try
        {
            // Calculate exponential backoff delay
            var delay = TimeSpan.FromMilliseconds(
                _options.ReconnectBaseDelay.TotalMilliseconds * Math.Pow(2, _reconnectAttempts)
            );

            await Task.Delay(delay, _disposalCts.Token).ConfigureAwait(false);

            await _operationLock.WaitAsync(_disposalCts.Token).ConfigureAwait(false);
            try
            {
                if (!_client.IsConnected)
                {
                    await _client.ConnectAsync(_disposalCts.Token).ConfigureAwait(false);
                    _reconnectAttempts = 0; // Reset on successful reconnect
                }
            }
            finally
            {
                _operationLock.Release();
            }
        }
        catch (OperationCanceledException)
        {
            // Ignore cancellation
        }
        catch
        {
            _reconnectAttempts++;
        }
    }

    /// <summary>
    /// Disposes the connection manager and its resources.
    /// Disposes all slot workers first.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;

        try
        {
            _disposalCts.Cancel();

            // Dispose all slot workers
            var disposeWorkerTasks = _slotWorkers.Values.Select(w => w.DisposeAsync().AsTask());
            await Task.WhenAll(disposeWorkerTasks).ConfigureAwait(false);
            _slotWorkers.Clear();

            // Stop health monitoring
            await StopHealthMonitoringAsync().ConfigureAwait(false);

            // Disconnect client
            await _client.DisconnectAsync().ConfigureAwait(false);
        }
        catch
        {
            // Suppress exceptions during disposal
        }

        await _client.DisposeAsync().ConfigureAwait(false);
        _operationLock.Dispose();
        _disposalCts.Dispose();
    }
}
