using TQG.Automation.SDK.Core;
using TQG.Automation.SDK.Exceptions;
using TQG.Automation.SDK.Shared;

namespace TQG.Automation.SDK.Management;

/// <summary>
/// Manages the lifecycle of a single PLC connection including health monitoring and automatic reconnection.
/// </summary>
internal sealed class PlcConnectionManager : IAsyncDisposable
{
    private readonly IPlcClient _client;
    private readonly PlcConnectionOptions _options;
    private readonly SemaphoreSlim _operationLock;
    private readonly CancellationTokenSource _disposalCts;

    private PeriodicTimer? _healthCheckTimer;
    private Task? _healthCheckTask;
    private CancellationTokenSource? _healthCheckCts;
    private bool _isDisposed;
    private int _reconnectAttempts;

    /// <summary>
    /// Initializes a new instance of the PlcConnectionManager class.
    /// </summary>
    /// <param name="client">The PLC client to manage.</param>
    /// <param name="options">Connection configuration options.</param>
    public PlcConnectionManager(IPlcClient client, PlcConnectionOptions options)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();

        _operationLock = new SemaphoreSlim(1, 1);
        _disposalCts = new CancellationTokenSource();
    }

    /// <summary>
    /// Gets the PLC client managed by this manager.
    /// </summary>
    public IPlcClient Client => _client;

    /// <summary>
    /// Gets the connection options for this manager.
    /// </summary>
    public PlcConnectionOptions Options => _options;

    /// <summary>
    /// Gets whether the client is currently connected.
    /// </summary>
    public bool IsConnected => _client.IsConnected;

    /// <summary>
    /// Connects to the PLC and starts health monitoring.
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
    /// Disconnects from the PLC and stops health monitoring.
    /// </summary>
    public async Task DisconnectAsync()
    {
        if (_isDisposed)
            return;

        await _operationLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopHealthMonitoringAsync().ConfigureAwait(false);
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
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;

        try
        {
            _disposalCts.Cancel();
            await StopHealthMonitoringAsync().ConfigureAwait(false);
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
