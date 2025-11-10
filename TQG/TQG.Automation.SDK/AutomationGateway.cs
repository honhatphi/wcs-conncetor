using System.Collections.Concurrent;
using System.Text.Json;
using TQG.Automation.SDK.Configuration;
using TQG.Automation.SDK.Core;
using TQG.Automation.SDK.Events;
using TQG.Automation.SDK.Exceptions;
using TQG.Automation.SDK.Logging;
using TQG.Automation.SDK.Management;
using TQG.Automation.SDK.Orchestration.Core;
using TQG.Automation.SDK.Orchestration.Models;
using TQG.Automation.SDK.Shared;

namespace TQG.Automation.SDK;

/// <summary>
/// Main entry point for PLC communication operations. Provides a unified interface for managing multiple PLC devices.
/// Singleton pattern ensures thread-safe single instance across the application.
/// </summary>
public sealed class AutomationGateway : IAsyncDisposable
{
    private static readonly Lazy<AutomationGateway> _instance = new(() => new AutomationGateway());

    private readonly PlcRegistry _registry;
    private readonly CommandOrchestrator _orchestrator;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<BarcodeValidationResponse>> _pendingBarcodeValidations = new();
    private readonly ILogger _logger;
    private CancellationTokenSource? _eventLoopCts;
    private Task? _eventLoopTask;
    private bool _isDisposed;
    private bool _isInitialized;
    private WarehouseLayout _warehouseLayout = WarehouseLayout.CreateDefault();

    /// <summary>
    /// Event raised when a command completes successfully.
    /// Subscribers receive CommandResultNotification with Status = Success.
    /// </summary>
    public event EventHandler<TaskSucceededEventArgs>? TaskSucceeded;

    /// <summary>
    /// Event raised when a command fails.
    /// Subscribers receive CommandResultNotification with Status = Failed.
    /// </summary>
    public event EventHandler<TaskFailedEventArgs>? TaskFailed;

    /// <summary>
    /// Event raised when an alarm is detected during command execution.
    /// This is an informational event - task may continue or fail depending on failOnAlarm configuration.
    /// Raised immediately when ErrorAlarm flag is detected on PLC.
    /// </summary>
    public event EventHandler<TaskAlarmEventArgs>? TaskAlarm;

    /// <summary>
    /// Event raised when a barcode is read from PLC during INBOUND operation and requires validation.
    /// Subscriber must call RespondToBarcodeValidation() within 2 minutes, or command will be rejected.
    /// </summary>
    public event EventHandler<BarcodeReceivedEventArgs>? BarcodeReceived;

    /// <summary>
    /// Gets the singleton instance of AutomationGateway.
    /// </summary>
    public static AutomationGateway Instance => _instance.Value;

    /// <summary>
    /// Private constructor to prevent direct instantiation.
    /// Use AutomationGateway.Instance to access the singleton.
    /// </summary>
    private AutomationGateway()
    {
        _registry = new PlcRegistry();
        _orchestrator = new CommandOrchestrator();
        _logger = new FileLogger("AutomationGateway", LoggerConfiguration.Default);
        _logger.LogInformation("=== AutomationGateway Instance Created ===");
    }

    /// <summary>
    /// Gets whether the gateway has been initialized.
    /// </summary>
    public bool IsInitialized => _isInitialized;

    /// <summary>
    /// Gets the number of registered devices.
    /// </summary>
    public int DeviceCount => _registry.Count;

    /// <summary>
    /// Gets all registered device IDs.
    /// </summary>
    public IEnumerable<string> DeviceIds => _registry.DeviceIds;

    /// <summary>
    /// Initializes and registers all PLC devices from configuration.
    /// Note: This only registers devices; use StartDeviceAsync() or StartAllDevicesAsync() to establish connections.
    /// </summary>
    /// <param name="configurations">Collection of PLC connection configurations.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <exception cref="ArgumentNullException">Thrown when configurations is null.</exception>
    /// <exception cref="ArgumentException">Thrown when configurations is empty or contains duplicates.</exception>
    /// <exception cref="PlcConnectionFailedException">Thrown when any device fails to initialize.</exception>
    public void Initialize(IEnumerable<PlcConnectionOptions> configurations)
    {
        _logger.LogInformation("Initialize(IEnumerable<PlcConnectionOptions>) called");
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ArgumentNullException.ThrowIfNull(configurations);

        var configList = configurations.ToList();
        _logger.LogInformation($"Initializing {configList.Count} devices from configuration");
        ValidateConfigurations(configList);

        InitializeCore(configList);
        _logger.LogInformation($"Successfully initialized {configList.Count} devices");
    }

    /// <summary>
    /// Initializes and registers all PLC devices from JSON configuration string.
    /// Note: This only registers devices; use ActivateDevice() or ActivateAllDevicesAsync() to establish connections.
    /// Uses PlcGatewayConfiguration for parsing and validation.
    /// </summary>
    /// <param name="configurations">JSON string containing PlcGatewayConfiguration with plcConnections array.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <exception cref="ArgumentNullException">Thrown when jsonConfiguration is null or empty.</exception>
    /// <exception cref="ArgumentException">Thrown when JSON is invalid or validation fails.</exception>
    /// <exception cref="PlcConnectionFailedException">Thrown when any device fails to initialize.</exception>
    public void Initialize(string configurations)
    {
        _logger.LogInformation("Initialize(string) called - Loading from JSON");
        var config = LoadAndValidateConfiguration(configurations);
        InitializeCore(config.PlcConnections);
    }

    /// <summary>
    /// Loads warehouse layout configuration from JSON string.
    /// This defines valid storage locations, block depth limits, and disabled positions.
    /// </summary>
    /// <param name="layoutJson">JSON string containing warehouse layout configuration.</param>
    /// <exception cref="ArgumentNullException">Thrown when layoutJson is null or empty.</exception>
    /// <exception cref="ArgumentException">Thrown when JSON is invalid or validation fails.</exception>
    public void LoadWarehouseLayout(string layoutJson)
    {
        _logger.LogInformation("LoadWarehouseLayout called");
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ArgumentNullException.ThrowIfNull(layoutJson);

        _warehouseLayout = WarehouseLayout.LoadFromJson(layoutJson);
        _logger.LogInformation("Warehouse layout loaded successfully");
    }

    /// <summary>
    /// Gets the current warehouse layout configuration.
    /// </summary>
    public WarehouseLayout GetWarehouseLayout()
    {
        _logger.LogDebug("GetWarehouseLayout called");
        return _warehouseLayout;
    }

    /// <summary>
    /// Switches the connection mode for a specific device (Real ↔ Emulated).
    /// </summary>
    /// <param name="deviceId">Unique device identifier.</param>
    /// <param name="newMode">The new connection mode.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <exception cref="ArgumentException">Thrown when deviceId is invalid.</exception>
    /// <exception cref="PlcConnectionFailedException">Thrown when device is not found or mode switch fails.</exception>
    public async Task SwitchModeAsync(
        string deviceId,
        PlcMode newMode,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation($"SwitchModeAsync called - DeviceId: {deviceId}, NewMode: {newMode}");
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ValidateDeviceId(deviceId);

        var oldManager = await _registry.GetManagerAsync(deviceId).ConfigureAwait(false);
        var oldOptions = oldManager.Options;

        // Check if mode is already set
        if (oldOptions.Mode == newMode)
        {
            _logger.LogInformation($"Device {deviceId} already in {newMode} mode - no action needed");
            return;
        }

        // Disconnect old connection
        await oldManager.DisconnectAsync().ConfigureAwait(false);

        // Create new options with updated mode
        var newOptions = oldOptions with { Mode = newMode };

        // Create new client and manager
        var newClient = PlcClientFactory.Create(newOptions);
        var newManager = new PlcConnectionManager(newClient, newOptions);

        // Connect new client
        await newManager.ConnectAsync(cancellationToken).ConfigureAwait(false);

        // Replace in registry
        var replacedManager = await _registry.ReplaceAsync(deviceId, newManager).ConfigureAwait(false);

        // Dispose old manager
        await replacedManager.DisposeAsync().ConfigureAwait(false);
        
        _logger.LogInformation($"Device {deviceId} successfully switched from {oldOptions.Mode} to {newMode}");
    }

    /// <summary>
    /// Gets the connection status for a specific device.
    /// </summary>
    /// <param name="deviceId">Unique device identifier.</param>
    /// <returns>True if connected; otherwise false.</returns>
    public bool IsConnected(string deviceId)
    {
        _logger.LogDebug($"IsConnected called - DeviceId: {deviceId}");
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (string.IsNullOrWhiteSpace(deviceId))
            return false;

        if (_registry.TryGetManager(deviceId, out var manager))
            return manager!.IsConnected;

        return false;
    }

    /// <summary>
    /// Gets the detailed status of a specific device.
    /// Status determination logic:
    /// - Error: Alarm detected (ErrorAlarm = true)
    /// - Busy: Device is executing a command
    /// - Idle: Device is ready and available
    /// </summary>
    /// <param name="deviceId">Unique device identifier.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Device status enum.</returns>
    /// <exception cref="ArgumentException">Thrown when deviceId is null or empty.</exception>
    /// <exception cref="PlcConnectionFailedException">Thrown when device is not found, offline, or link is not established.</exception>
    public Task<DeviceStatus> GetDeviceStatusAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug($"GetDeviceStatusAsync called - DeviceId: {deviceId}");
        return Task.Run(async () =>
        {
             ObjectDisposedException.ThrowIf(_isDisposed, this);

             if (string.IsNullOrWhiteSpace(deviceId))
                 throw new ArgumentException("Device ID cannot be null or empty.", nameof(deviceId));

             if (!_registry.TryGetManager(deviceId, out var manager))
                 throw new PlcConnectionFailedException($"Device '{deviceId}' not found. Please ensure the device is registered.");

             var client = manager!.Client;
             var config = manager.Options;

             var isConnected = manager.IsConnected;

             // If not connected, throw exception
             if (!isConnected)
                 throw new PlcConnectionFailedException($"Device '{deviceId}' is offline. Please connect the device using ActivateDevice() before checking status.");

             try
             {
                 // Check if link is established
                 var isLinkEstablished = await client.IsLinkEstablishedAsync(cancellationToken).ConfigureAwait(false);

                 if (!isLinkEstablished)
                     throw new PlcConnectionFailedException($"Device '{deviceId}' link is not established. Please ensure PLC program is running and has initialized the link.");

                 // Check for error alarm
                 var errorAlarmAddress = PlcAddress.Parse(config.SignalMap.ErrorAlarm);
                 var hasAlarm = await client.ReadAsync<bool>(errorAlarmAddress, cancellationToken).ConfigureAwait(false);

                 if (hasAlarm)
                 {
                     _logger.LogWarning($"Device {deviceId} status: Error (ErrorAlarm = true)");
                     return DeviceStatus.Error;
                 }

                 // Check if device is executing a command
                 var currentCommandId = _orchestrator.GetDeviceActiveCommand(deviceId)?.CommandId;
                 if (!string.IsNullOrEmpty(currentCommandId))
                 {
                     _logger.LogDebug($"Device {deviceId} status: Busy (CommandId: {currentCommandId})");
                     return DeviceStatus.Busy;
                 }

                 // Check if device is ready
                 var isReady = await client.IsDeviceReadyAsync(cancellationToken).ConfigureAwait(false);

                 var status = isReady ? DeviceStatus.Idle : DeviceStatus.Error;
                 _logger.LogDebug($"Device {deviceId} status: {status}");
                 return status;
             }
             catch (PlcConnectionFailedException)
             {
                 throw; // Re-throw connection exceptions
             }
             catch
             {
                 // If any PLC read fails, throw offline exception
                 throw new PlcConnectionFailedException($"Device '{deviceId}' is not responding. Please check the connection and try again.");
             }
         });

    }

    /// <summary>
    /// Gets the status of all registered devices.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Array of device statuses.</returns>
    /// <exception cref="PlcConnectionFailedException">Thrown when any device is offline or link is not established.</exception>
    public Task<DeviceStatus[]> GetAllDeviceStatusAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(async () =>
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            var deviceIds = _registry.DeviceIds.ToList();
            var statuses = new List<DeviceStatus>();

            foreach (var deviceId in deviceIds)
            {
                var status = await GetDeviceStatusAsync(deviceId, cancellationToken).ConfigureAwait(false);
                statuses.Add(status);
            }

            return statuses.ToArray();
        }, cancellationToken);
    }

    /// <summary>
    /// Starts (connects) a specific device and verifies PLC link is established.
    /// </summary>
    /// <param name="deviceId">Unique device identifier.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <exception cref="ArgumentException">Thrown when deviceId is invalid.</exception>
    /// <exception cref="PlcConnectionFailedException">Thrown when device is not found, connection fails, or link not established.</exception>
    public Task ActivateDevice(string deviceId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation($"ActivateDevice called - DeviceId: {deviceId}");
        return Task.Run(async () =>
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            ValidateDeviceId(deviceId);

            if (!_isInitialized)
                throw new InvalidOperationException("Gateway must be initialized before starting devices.");

            // Get manager and client
            var manager = await _registry.GetManagerAsync(deviceId).ConfigureAwait(false);
            var client = manager.Client;

            // Step 1: Physical connection
            await manager.ConnectAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation($"Device {deviceId} physically connected");

            // Step 2: Verify DLL is linked to PLC program
            var isLinked = await client.IsLinkEstablishedAsync(cancellationToken).ConfigureAwait(false);
            if (!isLinked)
            {
                // Disconnect on link failure
                await manager.DisconnectAsync().ConfigureAwait(false);
                
                _logger.LogError($"Device {deviceId} link not established with PLC");
                throw new PlcConnectionFailedException(
                    $"Link not established: PLC program has not set SoftwareConnected flag for {deviceId}. " +
                    $"Ensure PLC program is running and has initialized the link.");
            }
            
            _logger.LogInformation($"Device {deviceId} activated successfully");
        }, cancellationToken);
    }

    /// <summary>
    /// Starts (connects) all registered devices and verifies PLC links are established.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>True if all devices activated successfully; false if any device failed.</returns>
    public Task<bool> ActivateAllDevicesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("ActivateAllDevicesAsync called");
        return Task.Run(async () =>
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            if (!_isInitialized)
                throw new InvalidOperationException("Gateway must be initialized before starting devices.");

            var allDeviceIds = _registry.DeviceIds.ToList();
            _logger.LogInformation($"Activating {allDeviceIds.Count} devices");

            // Start all devices with link verification
            var successCount = 0;
            foreach (var deviceId in allDeviceIds)
            {
                try
                {
                    await ActivateDevice(deviceId, cancellationToken).ConfigureAwait(false);
                    successCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Failed to activate device {deviceId}: {ex.Message}", ex);
                    return false; // Any failure returns false
                }
            }

            _logger.LogInformation($"All {successCount} devices activated successfully");
            return true; // All succeeded
        }, cancellationToken);
    }

    /// <summary>
    /// Stops (disconnects) a specific device. Commands for this device will be rejected until restarted.
    /// </summary>
    /// <param name="deviceId">Unique device identifier.</param>
    /// <exception cref="ArgumentException">Thrown when deviceId is invalid.</exception>
    /// <exception cref="PlcConnectionFailedException">Thrown when device is not found.</exception>
    public Task DeactivateDevice(string deviceId)
    {
        _logger.LogInformation($"DeactivateDevice called - DeviceId: {deviceId}");
        return Task.Run(async () =>
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            ValidateDeviceId(deviceId);

            if (!_isInitialized)
                throw new InvalidOperationException("Gateway must be initialized before stopping devices.");

            await _registry.DisconnectDeviceAsync(deviceId).ConfigureAwait(false);
            _logger.LogInformation($"Device {deviceId} deactivated successfully");
        });
    }

    /// <summary>
    /// Stops (disconnects) all registered devices.
    /// </summary>
    public Task DeactivateAllDevicesAsync()
    {
        _logger.LogInformation("DeactivateAllDevicesAsync called");
        return Task.Run(async () =>
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            if (!_isInitialized)
            {
                _logger.LogWarning("DeactivateAllDevicesAsync called but gateway not initialized");
                return;
            }
            await _registry.DisconnectAllAsync().ConfigureAwait(false);
            _logger.LogInformation("All devices deactivated successfully");
        });
    }

    #region Command Queue Orchestration

    public Task SendCommand(TransportTask task)
    {
        _logger.LogInformation($"SendCommand called - TaskId: {task?.TaskId}, CommandType: {task?.CommandType}");
        ArgumentNullException.ThrowIfNull(task);

        return SendMultipleCommands([task]);
    }

    /// <summary>
    /// Submits one or more commands to the orchestration queue for scheduling and execution.
    /// Commands are matched to available devices and executed sequentially per device.
    /// </summary>
    /// <param name="requests">Collection of command requests to submit.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Submission result with accepted/rejected counts.</returns>
    /// <exception cref="ArgumentNullException">Thrown when requests is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when gateway is not initialized.</exception>
    public Task<SubmissionResult> SendMultipleCommands(
        IEnumerable<TransportTask> requests,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("SendMultipleCommands called");
        return Task.Run(async () =>
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            if (!_isInitialized)
                throw new InvalidOperationException("Gateway must be initialized before submitting commands.");

            ArgumentNullException.ThrowIfNull(requests);

            var requestList = requests.ToList();
            _logger.LogInformation($"Processing {requestList.Count} commands");
            
            if (requestList.Count == 0)
            {
                return new SubmissionResult
                {
                    Submitted = 0,
                    Rejected = 0,
                    RejectedCommands = []
                };
            }

            var submitted = 0;
            var rejected = new List<RejectCommand>();

            foreach (var request in requestList)
            {
                // Validate and map to internal envelope
                var validationError = ValidateCommandRequest(request);
                if (validationError != null)
                {
                    _logger.LogWarning($"Command validation failed - TaskId: {request.TaskId}, Reason: {validationError}");
                    rejected.Add(new RejectCommand(request, validationError));
                    continue;
                }

                var envelope = MapToEnvelope(request);

                // Submit to orchestrator
                var success = await _orchestrator.SubmitCommandAsync(envelope, cancellationToken)
                    .ConfigureAwait(false);

                if (success)
                {
                    _logger.LogInformation($"Command submitted to queue - TaskId: {request.TaskId}, CommandType: {request.CommandType}, DeviceId: {request.DeviceId ?? "Auto-assign"}");
                    submitted++;
                }
                else
                {
                    _logger.LogWarning($"Command submission failed - TaskId: {request.TaskId}");
                    rejected.Add(new RejectCommand(request, "Submission failed"));
                }
            }

            _logger.LogInformation($"Command submission complete - Submitted: {submitted}, Rejected: {rejected.Count}");
            
            return new SubmissionResult
            {
                Submitted = submitted,
                Rejected = rejected.Count,
                RejectedCommands = rejected
            };
        });
    }

    /// <summary>
    /// Pauses command scheduling. Pending commands remain in queue; in-flight commands continue executing.
    /// </summary>
    public void PauseQueue()
    {
        _logger.LogInformation("PauseQueue called");
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        _orchestrator.PauseScheduling();
        _logger.LogInformation("Command queue paused");
    }

    /// <summary>
    /// Resumes command scheduling after pause.
    /// </summary>
    public void ResumeQueue()
    {
        _logger.LogInformation("ResumeQueue called");
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        _orchestrator.ResumeScheduling();
        _logger.LogInformation("Command queue resumed");
    }

    /// <summary>
    /// Indicates whether command scheduling is currently paused.
    /// </summary>
    /// <returns></returns>
    public bool IsPauseQueue => _orchestrator.IsSchedulingPaused;

    /// <summary>
    /// Removes a pending command from the queue. If already executing, removal has no effect.
    /// </summary>
    /// <param name="commandId">Client command ID to remove (same ID used in CommandRequest).</param>
    /// <returns>True if command was pending and removed, false otherwise.</returns>
    public bool RemoveCommand(string commandId)
    {
        _logger.LogInformation($"RemoveCommand called - CommandId: {commandId}");
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        var removed = _orchestrator.RemoveCommand(commandId);
        if (removed)
        {
            _logger.LogInformation($"Command removed from queue - CommandId: {commandId}");
        }
        else
        {
            _logger.LogWarning($"Command not found or already executing - CommandId: {commandId}");
        }
        return removed;
    }

    /// <summary>
    /// Responds to a barcode validation request for an INBOUND command.
    /// Must be called within 2 minutes of BarcodeValidationRequested event, or command will auto-reject.
    /// If accepting (isValid = true), must provide destinationLocation and gateNumber.
    /// </summary>
    /// <param name="taskId">Command ID from BarcodeValidationRequestedEventArgs.</param>
    /// <param name="isValid">True to accept barcode and continue, false to reject.</param>
    /// <param name="destinationLocation">Destination location for inbound material (required if isValid = true).</param>
    /// <param name="gateNumber">Gate number for inbound operation (required if isValid = true).</param>
    /// <param name="direction">Optional enter direction for material flow.</param>
    /// <returns>True if response was accepted, false if validation already completed/timeout.</returns>
    public Task<bool> SendValidationResult(
        string taskId,
        bool isValid,
        Location? destinationLocation = null,
        Direction? direction = null,
        int? gateNumber = null)
    {
        _logger.LogInformation($"SendValidationResult called - TaskId: {taskId}, IsValid: {isValid}, Destination: {destinationLocation}, GateNumber: {gateNumber}");
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (string.IsNullOrWhiteSpace(taskId))
            throw new ArgumentException("Command ID cannot be null or empty.", nameof(taskId));

        // Validate required parameters when accepting
        if (isValid)
        {
            if (destinationLocation == null)
                throw new ArgumentNullException(nameof(destinationLocation), "Destination location is required when accepting barcode.");

            if (gateNumber == null || gateNumber <= 0)
                throw new ArgumentException("Valid gate number is required when accepting barcode.", nameof(gateNumber));
        }

        if (_pendingBarcodeValidations.TryRemove(taskId, out var tcs))
        {
            var response = new BarcodeValidationResponse
            {
                CommandId = taskId,
                IsValid = isValid,
                DestinationLocation = destinationLocation,
                GateNumber = gateNumber,
                EnterDirection = direction
            };

            tcs.TrySetResult(response);
            _logger.LogInformation($"Barcode validation response sent - TaskId: {taskId}, Result: {(isValid ? "Accepted" : "Rejected")}");
            return Task.FromResult(true);
        }

        _logger.LogWarning($"Barcode validation already completed or timeout - TaskId: {taskId}");
        return Task.FromResult(false); // Already completed or timeout
    }

    /// <summary>
    /// Triggers manual recovery for a specific device that is in error state.
    /// This is used when AutoRecoveryEnabled is false (manual mode).
    /// The device worker will check if the device is ready and resume operations if possible.
    /// </summary>
    /// <param name="deviceId">The device ID to recover.</param>
    /// <returns>True if the device exists and recovery trigger was sent; otherwise false.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when the gateway has been disposed.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the gateway is not initialized or device is currently executing a command.</exception>
    /// <exception cref="PlcConnectionFailedException">Thrown when device is not found, offline, or link is not established.</exception>
    public Task<bool> ResetDeviceStatusAsync(string deviceId)
    {
        _logger.LogInformation($"ResetDeviceStatusAsync called - DeviceId: {deviceId}");
        return Task.Run(async () =>
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            if (!_isInitialized)
                throw new InvalidOperationException("Gateway must be initialized before recovering devices.");

            ValidateDeviceId(deviceId);

            var deviceStatus = await GetDeviceStatusAsync(deviceId).ConfigureAwait(false);

            if (deviceStatus == DeviceStatus.Busy)
            {
                _logger.LogWarning($"Cannot reset device {deviceId} - device is busy");
                throw new InvalidOperationException($"Cannot reset device '{deviceId}' while it is executing a command. Wait for command completion or use manual intervention.");
            }

            var result = _orchestrator.TriggerDeviceRecovery(deviceId);
            if (result)
            {
                _logger.LogInformation($"Device {deviceId} recovery triggered successfully");
            }
            else
            {
                _logger.LogWarning($"Device {deviceId} recovery trigger failed");
            }
            return result;
        });
    }

    /// <summary>
    /// Gets the current location of a device by reading CurrentFloor, CurrentRail, CurrentBlock, CurrentDepth from PLC.
    /// Returns null if device is not found or PLC read fails.
    /// </summary>
    /// <param name="deviceId">Device identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Current location or null if not found.</returns>
    public Task<Location?> GetActualLocationAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug($"GetActualLocationAsync called - DeviceId: {deviceId}");
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        return Task.Run(async () =>
        {
            return await _orchestrator.GetDeviceCurrentLocationAsync(deviceId, cancellationToken).ConfigureAwait(false);
        });
    }

    /// <summary>
    /// Removes multiple pending commands from the queue.
    /// Commands already executing will complete normally.
    /// </summary>
    /// <param name="taskIds">Collection of command IDs to remove.</param>
    /// <returns>Count of successfully removed commands.</returns>
    /// <exception cref="ArgumentNullException">Thrown when taskIds is null.</exception>
    public int RemoveTransportTasks(IEnumerable<string> taskIds)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ArgumentNullException.ThrowIfNull(taskIds);
        return _orchestrator.RemoveCommands(taskIds);
    }

    /// <summary>
    /// Gets the currently executing command for a specific device.
    /// Returns null if device is idle or not found.
    /// </summary>
    /// <param name="deviceId">Device identifier.</param>
    /// <returns>Command tracking information of the active command, or null if device is idle.</returns>
    public string? GetCurrentTask(string deviceId)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        return _orchestrator.GetDeviceActiveCommand(deviceId)?.CommandId;
    }

    /// <summary>
    /// Gets all pending commands in the queue (awaiting device assignment).
    /// Commands are ordered by priority (High first) and submission time (FIFO within same priority).
    /// </summary>
    /// <returns>Read-only list of pending commands with full tracking information.</returns>
    public TransportTask[] GetPendingTask()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        var commands = _orchestrator.GetPendingCommands();
        return [.. commands.Select(c => new TransportTask
        {
            TaskId = c.CommandId,
            DeviceId = c.PlcDeviceId,
            CommandType = c.CommandType,
            SourceLocation = c.SourceLocation,
            TargetLocation = c.DestinationLocation,
            GateNumber = c.GateNumber
        })];
    }

    public TransportTask[] GetProccessingTask()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        var commands = _orchestrator.GetProcessingCommands();
        return [.. commands.Select(c => new TransportTask
        {
            TaskId = c.CommandId,
            DeviceId = c.PlcDeviceId,
            CommandType = c.CommandType,
            SourceLocation = c.SourceLocation,
            TargetLocation = c.DestinationLocation,
            GateNumber = c.GateNumber
        })];
    }

    /// <summary>
    /// Raises appropriate events based on command result:
    /// - TaskAlarm: When alarm is detected during execution (Error status - intermediate notification)
    /// - TaskSucceeded: When command completes successfully
    /// - TaskFailed: When command fails
    /// </summary>
    private void RaiseCommandResultEvent(CommandResultNotification notification)
    {
        try
        {
            // Check if this is an alarm notification (Error status)
            if (notification.Status == CommandStatus.Error)
            {
                // This is an alarm notification - raise TaskAlarm event
                if (notification.PlcError != null)
                {
                    var eventArgsAlarm = new TaskAlarmEventArgs(
                        notification.PlcDeviceId,
                        notification.CommandId,
                        notification.PlcError);
                    _logger.LogWarning($"EVENT: TaskAlarm raised - TaskId: {notification.CommandId}, DeviceId: {notification.PlcDeviceId}, ErrorCode: {notification.PlcError.ErrorCode}, ErrorMessage: {notification.PlcError.ErrorMessage}");
                    TaskAlarm?.Invoke(this, eventArgsAlarm);
                }
                return;
            }

            // Final result events
            if (notification.Status == CommandStatus.Success)
            {
                var eventArgsSuccess = new TaskSucceededEventArgs(notification.PlcDeviceId, notification.CommandId);
                _logger.LogInformation($"EVENT: TaskSucceeded raised - TaskId: {notification.CommandId}, DeviceId: {notification.PlcDeviceId}");
                TaskSucceeded?.Invoke(this, eventArgsSuccess);
            }
            else if (notification.Status == CommandStatus.Failed)
            {
                var error = notification.PlcError ?? new ErrorDetail(-1, notification.Message ?? "Exception");
                var eventArgsFailed = new TaskFailedEventArgs(notification.PlcDeviceId, notification.CommandId, error);
                _logger.LogError($"EVENT: TaskFailed raised - TaskId: {notification.CommandId}, DeviceId: {notification.PlcDeviceId}, ErrorCode: {error.ErrorCode}, ErrorMessage: {error.ErrorMessage}");
                TaskFailed?.Invoke(this, eventArgsFailed);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Exception in HandleResultNotification - TaskId: {notification.CommandId}", ex);
            // Suppress exceptions from event handlers to prevent disrupting the result stream
        }
    }

    /// <summary>
    /// Internal method for InboundExecutor to request barcode validation.
    /// Raises BarcodeValidationRequested event and waits for user response (max 2 minutes).
    /// </summary>
    private async Task<BarcodeValidationResponse> RequestBarcodeValidationAsync(
        BarcodeReceivedEventArgs eventArgs,
        CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<BarcodeValidationResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

        if (!_pendingBarcodeValidations.TryAdd(eventArgs.TaskId, tcs))
        {
            throw new InvalidOperationException($"Duplicate barcode validation request for command {eventArgs.TaskId}");
        }

        try
        {
            // Raise event to notify user
            _logger.LogInformation($"EVENT: BarcodeReceived raised - TaskId: {eventArgs.TaskId}, DeviceId: {eventArgs.DeviceId}, Barcode: {eventArgs.Barcode}");
            BarcodeReceived?.Invoke(this, eventArgs);

            // Wait for response with 5 minute timeout
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromMinutes(5));

            try
            {
                return await tcs.Task.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Timeout or cancellation - return rejection
                return new BarcodeValidationResponse
                {
                    CommandId = eventArgs.TaskId,
                    IsValid = false
                };
            }
        }
        finally
        {
            // Cleanup
            _pendingBarcodeValidations.TryRemove(eventArgs.TaskId, out _);
        }
    }

    /// <summary>
    /// Maps public CommandRequest DTO to internal CommandEnvelope.
    /// </summary>
    /// <param name="request">The command request from client.</param>
    private static CommandEnvelope MapToEnvelope(TransportTask request)
    {
        return new CommandEnvelope
        {
            CommandId = request.TaskId,
            PlcDeviceId = request.DeviceId,
            CommandType = request.CommandType,
            SourceLocation = request.SourceLocation,
            DestinationLocation = request.TargetLocation,
            GateNumber = request.GateNumber,
            EnterDirection = request.InDirBlock,
            ExitDirection = request.OutDirBlock,
            SubmittedAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Maps internal CommandResult to public CommandResultNotification.
    /// Mapping logic:
    /// - Success → Success (command completed successfully)
    /// - Failed → Failed (PLC signaled CommandFailed flag)
    /// - Warning → Success (command completed with alarm but still successful, message contains warning)
    /// - Error → Error (alarm detected during execution - intermediate notification)
    /// - Timeout → Failed (command timed out before completion)
    /// - Cancelled → Failed (command was cancelled)
    /// </summary>
    private static CommandResultNotification MapToNotification(CommandResult result)
    {
        // Map internal ExecutionStatus to public CommandStatus
        var publicStatus = result.Status switch
        {
            ExecutionStatus.Success => CommandStatus.Success,
            ExecutionStatus.Warning => CommandStatus.Success, // Warning is still success (completed with alarm)
            ExecutionStatus.Failed => CommandStatus.Failed,   // PLC signaled failure
            ExecutionStatus.Error => CommandStatus.Error,     // Alarm notification (intermediate)
            ExecutionStatus.Timeout => CommandStatus.Failed,  // Timeout is a failure
            ExecutionStatus.Cancelled => CommandStatus.Failed, // Cancellation is a failure
            _ => CommandStatus.Failed // Default to Failed for unknown statuses
        };

        return new CommandResultNotification
        {
            CommandId = result.CommandId,
            PlcDeviceId = result.PlcDeviceId,
            Status = publicStatus,
            Message = result.Message,
            CompletedAt = result.CompletedAt,
            Duration = result.Duration,
            PlcError = result.PlcError
        };
    }

    /// <summary>
    /// Validates a command request and returns error message if invalid.
    /// Uses warehouse layout configuration for location validation.
    /// - Inbound: No location validation required (DestinationLocation provided via barcode validation)
    /// - Outbound: Requires SourceLocation
    /// - Transfer: Requires both SourceLocation and DestinationLocation
    /// - CheckPallet: Requires SourceLocation with valid depth
    /// </summary>
    private string? ValidateCommandRequest(TransportTask request)
    {
        // Validate CommandType (enum is always valid unless default/undefined)
        if (!Enum.IsDefined(typeof(CommandType), request.CommandType))
            return $"Invalid CommandType: {request.CommandType}";

        // Validate locations based on command type
        switch (request.CommandType)
        {
            case CommandType.Outbound:
                // Outbound requires SourceLocation
                if (request.SourceLocation == null)
                    return "SourceLocation is required for OUTBOUND commands";

                var (isSourceValid, sourceError) = _warehouseLayout.ValidateLocation(request.SourceLocation, "SourceLocation");
                if (!isSourceValid)
                    return sourceError;
                break;

            case CommandType.CheckPallet:
                // CheckPallet requires SourceLocation
                if (request.SourceLocation == null)
                    return "SourceLocation is required for CHECKPALLET commands";

                var (isCheckSourceValid, checkSourceError) = _warehouseLayout.ValidateLocation(request.SourceLocation, "SourceLocation");
                if (!isCheckSourceValid)
                    return checkSourceError;
                break;

            case CommandType.Transfer:
                // Transfer requires both SourceLocation and DestinationLocation
                if (request.SourceLocation == null)
                    return "SourceLocation is required for TRANSFER commands";

                if (request.TargetLocation == null)
                    return "DestinationLocation is required for TRANSFER commands";

                var (isTransferSourceValid, transferSourceError) = _warehouseLayout.ValidateLocation(request.SourceLocation, "SourceLocation");
                if (!isTransferSourceValid)
                    return transferSourceError;

                var (isTransferDestValid, transferDestError) = _warehouseLayout.ValidateLocation(request.TargetLocation, "DestinationLocation");
                if (!isTransferDestValid)
                    return transferDestError;
                break;

            case CommandType.Inbound:
                // Inbound: DestinationLocation will be provided during barcode validation
                // No location validation needed here
                break;
        }

        // Validate directions if provided
        if (!Enum.IsDefined(typeof(Direction), request.InDirBlock))
            return $"Invalid EnterDirection: {request.InDirBlock}";
        if (!Enum.IsDefined(typeof(Direction), request.InDirBlock))
            return $"Invalid ExitDirection: {request.OutDirBlock}";

        // Validate gate number
        if (request.GateNumber < 1 || request.GateNumber > 10)
            return "GateNumber must be between 1 and 10";

        return null; // Valid
    }

    /// <summary>
    /// Validates device ID parameter.
    /// </summary>
    private static void ValidateDeviceId(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            throw new ArgumentException("Device ID cannot be null or empty.", nameof(deviceId));
    }

    #endregion

    #region Initialization Helpers

    /// <summary>
    /// Core initialization logic shared by all InitializeAsync overloads.
    /// </summary>
    private void InitializeCore(List<PlcConnectionOptions> configurations)
    {
        // Set barcode validation callback before registering devices
        _orchestrator.SetBarcodeValidationCallback(RequestBarcodeValidationAsync);

        var failures = new List<(string DeviceId, Exception Error)>();

        // Initialize all devices
        foreach (var config in configurations)
        {
            try
            {
                InitializeDevice(config);
            }
            catch (Exception ex)
            {
                failures.Add((config.DeviceId, ex));
            }
        }

        // Report failures if any
        if (failures.Count > 0)
        {
            var errorMessages = failures.Select(f => $"{f.DeviceId}: {f.Error.Message}");
            throw new PlcConnectionFailedException(
                $"Failed to initialize {failures.Count} device(s): {string.Join("; ", errorMessages)}");
        }

        // Start orchestration background tasks (idempotent - safe if already started)
        _orchestrator.Start();

        // Start background event loop to raise events
        StartEventLoop();

        _isInitialized = true;
    }

    /// <summary>
    /// Initializes a single device: creates client, manager, and registers (but does NOT connect).
    /// Use StartDeviceAsync() to establish physical connection.
    /// </summary>
    private void InitializeDevice(PlcConnectionOptions config)
    {
        config.Validate();

        var client = PlcClientFactory.Create(config);
        var manager = new PlcConnectionManager(client, config);

        // Register to registry and orchestrator (no connection yet)
        _registry.RegisterAsync(config.DeviceId, manager);
        _orchestrator.RegisterDevice(client, config);
    }

    /// <summary>
    /// Validates configuration list for emptiness and duplicates.
    /// </summary>
    private static void ValidateConfigurations(List<PlcConnectionOptions> configurations)
    {
        if (configurations.Count == 0)
            throw new ArgumentException("Configuration list cannot be empty.", nameof(configurations));

        // Check for duplicate device IDs
        var duplicates = configurations
            .GroupBy(c => c.DeviceId, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Skip(1).Any())
            .Select(g => g.Key)
            .ToList();

        if (duplicates.Count > 0)
            throw new ArgumentException($"Duplicate device IDs found: {string.Join(", ", duplicates)}");
    }

    /// <summary>
    /// Loads and validates configuration from JSON string.
    /// </summary>
    private static PlcGatewayConfiguration LoadAndValidateConfiguration(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        try
        {
            var config = PlcGatewayConfiguration.LoadFromJson(json);
            config.Validate(); // Validates all connections and checks for duplicates
            return config;
        }
        catch (JsonException ex)
        {
            throw new ArgumentException($"Invalid JSON configuration: {ex.Message}", nameof(json), ex);
        }
        catch (ArgumentException)
        {
            throw; // Re-throw validation errors as-is
        }
    }

    #endregion

    #region Event Loop Management

    /// <summary>
    /// Starts background event loop to observe command results and raise events.
    /// </summary>
    private void StartEventLoop()
    {
        if (_eventLoopCts != null)
            return; // Already started

        _eventLoopCts = new CancellationTokenSource();
        _eventLoopTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var result in _orchestrator.ObserveResultsAsync(_eventLoopCts.Token))
                {
                    var notification = MapToNotification(result);
                    RaiseCommandResultEvent(notification);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
            }
            catch
            {
                // Suppress exceptions to prevent crashing the event loop
            }
        }, CancellationToken.None);
    }

    /// <summary>
    /// Stops background event loop.
    /// </summary>
    private async Task StopEventLoopAsync()
    {
        if (_eventLoopCts == null || _eventLoopTask == null)
            return;

        _eventLoopCts.Cancel();

        try
        {
            await _eventLoopTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected
        }
        finally
        {
            _eventLoopCts.Dispose();
            _eventLoopCts = null;
            _eventLoopTask = null;
        }
    }

    #endregion

    /// <summary>
    /// Disposes all resources and connections permanently.
    /// After disposal, this gateway instance cannot be reused - create a new instance instead.
    /// </summary>
    /// <remarks>
    /// This performs a hard cleanup:
    /// - Stops and disposes orchestrator (cannot be restarted)
    /// - Disconnects and disposes all devices
    /// - Clears registry
    /// For a soft shutdown that allows reinitialization, use ShutdownAsync() instead.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;

        // Stop event loop first
        await StopEventLoopAsync().ConfigureAwait(false);

        try
        {
            // Hard stop: dispose orchestrator completely
            await _orchestrator.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // Suppress exceptions during orchestrator disposal
        }

        // Link cleanup is handled by PLC program - no need for explicit clear

        try
        {
            // Hard cleanup: dispose all managers and clear registry
            await _registry.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // Suppress exceptions during registry disposal
        }
    }
}
