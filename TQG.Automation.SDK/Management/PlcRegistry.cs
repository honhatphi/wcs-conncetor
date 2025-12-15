using System.Collections.Concurrent;
using TQG.Automation.SDK.Exceptions;
using TQG.Automation.SDK.Orchestration.Workers;
using TQG.Automation.SDK.Shared;

namespace TQG.Automation.SDK.Management;

/// <summary>
/// Thread-safe registry for managing multiple PLC connections.
/// Uses ConcurrentDictionary for lock-free concurrent operations.
/// Each device can have multiple slots sharing a single connection.
/// </summary>
internal sealed class PlcRegistry : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, PlcConnectionManager> _managers;
    private bool _isDisposed;

    /// <summary>
    /// Initializes a new instance of the PlcRegistry class.
    /// </summary>
    public PlcRegistry()
    {
        _managers = new ConcurrentDictionary<string, PlcConnectionManager>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets the number of registered devices.
    /// </summary>
    public int Count => _managers.Count;

    /// <summary>
    /// Gets all registered device IDs.
    /// </summary>
    public IEnumerable<string> DeviceIds => _managers.Keys;

    /// <summary>
    /// Gets all device-slot combinations registered in the registry.
    /// </summary>
    /// <returns>Enumerable of (DeviceId, SlotId) tuples.</returns>
    public IEnumerable<(string DeviceId, int SlotId)> GetAllSlots()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        foreach (var (deviceId, manager) in _managers)
        {
            foreach (var slotId in manager.SlotWorkers.Keys)
            {
                yield return (deviceId, slotId);
            }
        }
    }

    /// <summary>
    /// Gets all slot workers across all devices.
    /// </summary>
    /// <returns>Enumerable of (DeviceId, SlotId, SlotWorker) tuples.</returns>
    public IEnumerable<(string DeviceId, int SlotId, SlotWorker Worker)> GetAllSlotWorkers()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        foreach (var (deviceId, manager) in _managers)
        {
            foreach (var (slotId, worker) in manager.SlotWorkers)
            {
                yield return (deviceId, slotId, worker);
            }
        }
    }

    /// <summary>
    /// Gets a specific slot worker by device ID and slot ID.
    /// </summary>
    /// <param name="deviceId">Device identifier.</param>
    /// <param name="slotId">Slot identifier.</param>
    /// <returns>The SlotWorker if found; otherwise null.</returns>
    public SlotWorker? GetSlotWorker(string deviceId, int slotId)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (string.IsNullOrWhiteSpace(deviceId))
            return null;

        if (_managers.TryGetValue(deviceId, out var manager))
        {
            return manager.GetSlotWorker(slotId);
        }

        return null;
    }

    /// <summary>
    /// Registers a new PLC connection manager.
    /// </summary>
    /// <param name="deviceId">Unique device identifier.</param>
    /// <param name="manager">Connection manager to register.</param>
    /// <returns>True if registered successfully; false if device ID already exists.</returns>
    /// <exception cref="ArgumentNullException">Thrown when deviceId or manager is null.</exception>
    public bool RegisterAsync(string deviceId, PlcConnectionManager manager)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ArgumentNullException.ThrowIfNull(deviceId);
        ArgumentNullException.ThrowIfNull(manager);

        return _managers.TryAdd(deviceId, manager);
    }

    /// <summary>
    /// Unregisters a PLC connection manager and disposes it.
    /// </summary>
    /// <param name="deviceId">Device identifier to unregister.</param>
    /// <returns>True if unregistered successfully; false if device ID not found.</returns>
    public async Task<bool> UnregisterAsync(string deviceId)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ArgumentNullException.ThrowIfNull(deviceId);

        if (_managers.TryRemove(deviceId, out var manager))
        {
            await manager.DisposeAsync().ConfigureAwait(false);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Gets a connection manager by device ID.
    /// </summary>
    /// <param name="deviceId">Device identifier.</param>
    /// <returns>The connection manager for the specified device.</returns>
    /// <exception cref="ArgumentNullException">Thrown when deviceId is null.</exception>
    /// <exception cref="PlcConnectionFailedException">Thrown when device ID is not found.</exception>
    public async Task<PlcConnectionManager> GetManagerAsync(string deviceId)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ArgumentNullException.ThrowIfNull(deviceId);

        return await Task.Run(() =>
        {
            if (_managers.TryGetValue(deviceId, out var manager))
                return manager;

            throw new PlcConnectionFailedException($"Device '{deviceId}' not found in registry. Available devices: {string.Join(", ", _managers.Keys)}");
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Tries to get a connection manager by device ID.
    /// </summary>
    /// <param name="deviceId">Device identifier.</param>
    /// <param name="manager">The connection manager if found.</param>
    /// <returns>True if manager was found; otherwise false.</returns>
    public bool TryGetManager(string deviceId, out PlcConnectionManager? manager)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (string.IsNullOrWhiteSpace(deviceId))
        {
            manager = null;
            return false;
        }

        return _managers.TryGetValue(deviceId, out manager);
    }

    /// <summary>
    /// Checks if a device is registered.
    /// </summary>
    /// <param name="deviceId">Device identifier.</param>
    /// <returns>True if device is registered; otherwise false.</returns>
    public bool ContainsDevice(string deviceId)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (string.IsNullOrWhiteSpace(deviceId))
            return false;

        return _managers.ContainsKey(deviceId);
    }

    /// <summary>
    /// Replaces an existing connection manager with a new one (used for mode switching).
    /// </summary>
    /// <param name="deviceId">Device identifier.</param>
    /// <param name="newManager">New connection manager to replace with.</param>
    /// <returns>The old manager that was replaced.</returns>
    /// <exception cref="PlcConnectionFailedException">Thrown when device ID is not found.</exception>
    public async Task<PlcConnectionManager> ReplaceAsync(string deviceId, PlcConnectionManager newManager)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ArgumentNullException.ThrowIfNull(deviceId);
        ArgumentNullException.ThrowIfNull(newManager);

        return await Task.Run(() =>
        {
            if (_managers.TryGetValue(deviceId, out var oldManager))
            {
                if (_managers.TryUpdate(deviceId, newManager, oldManager))
                    return oldManager;
            }

            throw new PlcConnectionFailedException($"Failed to replace manager for device '{deviceId}'. Device may not be registered.");
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Connects a specific device and begins health monitoring.
    /// </summary>
    /// <param name="deviceId">Device identifier.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <exception cref="PlcConnectionFailedException">Thrown when device is not found or connection fails.</exception>
    public async Task ConnectDeviceAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ArgumentNullException.ThrowIfNull(deviceId);

        var manager = await GetManagerAsync(deviceId).ConfigureAwait(false);
        await manager.ConnectAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Disconnects a specific device and halts health monitoring.
    /// </summary>
    /// <param name="deviceId">Device identifier.</param>
    /// <exception cref="PlcConnectionFailedException">Thrown when device is not found.</exception>
    public async Task DisconnectDeviceAsync(string deviceId)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ArgumentNullException.ThrowIfNull(deviceId);

        var manager = await GetManagerAsync(deviceId).ConfigureAwait(false);
        await manager.DisconnectAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Connects all registered devices in parallel.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>List of devices that failed to connect with their error messages.</returns>
    public async Task<List<(string DeviceId, string Error)>> ConnectAllAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        var failures = new List<(string DeviceId, string Error)>();
        var connectTasks = _managers.Select(kvp =>
            Task.Run(async () =>
            {
                try
                {
                    await kvp.Value.ConnectAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    lock (failures)
                    {
                        failures.Add((kvp.Key, ex.Message));
                    }
                }
            }, cancellationToken)
        );

        await Task.WhenAll(connectTasks).ConfigureAwait(false);
        return failures;
    }

    /// <summary>
    /// Disconnects all registered devices in parallel.
    /// </summary>
    public async Task DisconnectAllAsync()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        var disconnectTasks = _managers.Values.Select(manager =>
            Task.Run(async () =>
            {
                try
                {
                    await manager.DisconnectAsync().ConfigureAwait(false);
                }
                catch
                {
                    // Suppress individual disconnect failures
                }
            })
        );

        await Task.WhenAll(disconnectTasks).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets connection status for all devices.
    /// </summary>
    /// <returns>Dictionary mapping device IDs to connection status.</returns>
    public Dictionary<string, bool> GetConnectionStatus()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        return _managers.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.IsConnected
        );
    }

    /// <summary>
    /// Gets configuration options for all devices.
    /// </summary>
    /// <returns>Dictionary mapping device IDs to their options.</returns>
    public Dictionary<string, PlcConnectionOptions> GetAllOptions()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        return _managers.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.Options
        );
    }

    /// <summary>
    /// Disposes all registered connection managers and clears the registry.
    /// </summary>
    public async Task DisposeAllAsync()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;

        var disposeTasks = _managers.Values.Select(manager =>
            Task.Run(async () =>
            {
                try
                {
                    await manager.DisposeAsync().ConfigureAwait(false);
                }
                catch
                {
                    // Suppress individual disposal failures
                }
            })
        );

        await Task.WhenAll(disposeTasks).ConfigureAwait(false);
        _managers.Clear();
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await DisposeAllAsync().ConfigureAwait(false);
    }
}
