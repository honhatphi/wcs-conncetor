using TQG.Automation.SDK.Core;
using TQG.Automation.SDK.Exceptions;
using TQG.Automation.SDK.Models;

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
        catch (PlcConnectionException)
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
        catch (PlcConnectionException)
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

        _healthCheckTimer = new PeriodicTimer(_options.HealthCheckInterval);
        _healthCheckTask = Task.Run(HealthCheckLoopAsync, _disposalCts.Token);
    }

    /// <summary>
    /// Stops background health monitoring.
    /// </summary>
    private async Task StopHealthMonitoringAsync()
    {
        if (_healthCheckTimer == null)
            return;

        _healthCheckTimer.Dispose();
        _healthCheckTimer = null;

        if (_healthCheckTask != null)
        {
            try
            {
                await _healthCheckTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch
            {
                // Ignore timeout
            }

            _healthCheckTask = null;
        }
    }

    /// <summary>
    /// Background loop for periodic health checks and automatic reconnection.
    /// </summary>
    private async Task HealthCheckLoopAsync()
    {
        while (!_disposalCts.Token.IsCancellationRequested && _healthCheckTimer != null)
        {
            try
            {
                await _healthCheckTimer.WaitForNextTickAsync(_disposalCts.Token).ConfigureAwait(false);

                if (_disposalCts.Token.IsCancellationRequested)
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
