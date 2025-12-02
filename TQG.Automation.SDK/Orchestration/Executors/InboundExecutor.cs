using System.Threading.Channels;
using TQG.Automation.SDK.Clients;
using TQG.Automation.SDK.Core;
using TQG.Automation.SDK.Events;
using TQG.Automation.SDK.Orchestration.Models;
using TQG.Automation.SDK.Shared;

namespace TQG.Automation.SDK.Orchestration.Executors;

/// <summary>
/// Types of signals that can be detected during command execution.
/// </summary>
internal enum SignalType
{
    None,
    Alarm,
    CommandFailed,
    InboundCompleted
}

/// <summary>
/// Result from the signal monitor indicating what signal was detected.
/// </summary>
internal sealed record SignalMonitorResult
{
    public required SignalType Type { get; init; }
    public ErrorDetail? Error { get; init; }
    public DateTimeOffset DetectedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Executes INBOUND commands: handles incoming material flow to warehouse.
/// Flow: Trigger → Start Process → Read Barcode → Validate → Write Parameters → Wait for Result.
/// Signal monitoring runs continuously throughout the entire execution process.
/// </summary>
internal sealed class InboundExecutor(
    IPlcClient plcClient,
    SignalMap signalMap,
    bool failOnAlarm,
    Func<BarcodeReceivedEventArgs, CancellationToken, Task<BarcodeValidationResponse>> barcodeValidationCallback)
{
    #region Fields

    private readonly IPlcClient _plcClient = plcClient ?? throw new ArgumentNullException(nameof(plcClient));
    private readonly SignalMap _signalMap = signalMap ?? throw new ArgumentNullException(nameof(signalMap));
    private readonly bool _failOnAlarm = failOnAlarm;
    private readonly Func<BarcodeReceivedEventArgs, CancellationToken, Task<BarcodeValidationResponse>>
        _barcodeValidationCallback = barcodeValidationCallback ?? throw new ArgumentNullException(nameof(barcodeValidationCallback));

    /// <summary>
    /// Shared state for signal monitoring across execution.
    /// </summary>
    private SignalMonitorResult? _detectedSignal;
    private bool _alarmNotificationSent;

    #endregion

    #region Nested Types

    /// <summary>
    /// Context for signal monitoring.
    /// </summary>
    private sealed class SignalMonitorContext
    {
        public required CommandEnvelope Command { get; init; }
        public required Channel<CommandResult> ResultChannel { get; init; }
        public required List<string> Steps { get; init; }
        public required DateTimeOffset StartTime { get; init; }
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Executes an INBOUND command: receive material from external source.
    /// Flow: Trigger → Start Process → Read & Validate Barcode → Write Parameters → Wait for Result.
    /// Signal monitoring (Alarm, CommandFailed, InboundCompleted) runs continuously throughout execution.
    /// </summary>
    public async Task<CommandExecutionResult> ExecuteAsync(
        CommandEnvelope command,
        Channel<CommandResult> resultChannel,
        CancellationToken cancellationToken)
    {
        if (command.CommandType != CommandType.Inbound)
            throw new ArgumentException($"Invalid command type: {command.CommandType}. Expected Inbound.", nameof(command));

        var steps = new List<string>();
        var startTime = DateTimeOffset.UtcNow;

        // Reset shared state for this execution
        _detectedSignal = null;
        _alarmNotificationSent = false;

        // Create a linked cancellation token source that will be cancelled when a terminating signal is detected
        using var signalCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var linkedToken = signalCts.Token;

        // Context for signal monitoring
        var monitorContext = new SignalMonitorContext
        {
            Command = command,
            ResultChannel = resultChannel,
            Steps = steps,
            StartTime = startTime
        };

        // Start background signal monitoring task
        var monitorTask = MonitorSignalsAsync(monitorContext, signalCts, cancellationToken);

        try
        {
            // Execute all steps with the linked token (will be cancelled if terminating signal detected)
            var executionTask = ExecuteStepsAsync(command, steps, linkedToken);

            // Wait for either execution to complete or monitor to detect a terminating signal
            var completedTask = await Task.WhenAny(executionTask, monitorTask).ConfigureAwait(false);

            if (completedTask == monitorTask)
            {
                // Signal was detected - check what type
                var signalResult = await monitorTask.ConfigureAwait(false);

                if (signalResult.Type == SignalType.InboundCompleted)
                {
                    var elapsed = (signalResult.DetectedAt - startTime).TotalMilliseconds;
                    steps.Add($"Command completed after {elapsed:F0}ms (detected by monitor)");
                    return CommandExecutionResult.Success(
                        $"INBOUND completed (signal detected during execution)",
                        steps);
                }
                else if (signalResult.Type == SignalType.CommandFailed)
                {
                    var elapsed = (signalResult.DetectedAt - startTime).TotalMilliseconds;
                    steps.Add($"Command failed after {elapsed:F0}ms (detected by monitor)");
                    return CommandExecutionResult.Failed(
                        "PLC signaled command failure (CommandFailed flag set)",
                        steps,
                        signalResult.Error);
                }
                else if (signalResult.Type == SignalType.Alarm && _failOnAlarm)
                {
                    return CommandExecutionResult.Failed(
                        $"Command failed due to alarm (failOnAlarm=true): {signalResult.Error}",
                        steps,
                        signalResult.Error);
                }
            }

            // Execution completed normally - get the result
            return await executionTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Cancelled by signal monitor, not by external cancellation
            if (_detectedSignal != null)
            {
                return HandleDetectedSignal(_detectedSignal, steps, startTime);
            }
            throw;
        }
        catch (OperationCanceledException)
        {
            throw; // Let caller handle external cancellation
        }
        catch (Exception ex)
        {
            steps.Add($"Error: {ex.Message}");
            return CommandExecutionResult.Error(
                $"INBOUND execution failed: {ex.Message}",
                steps);
        }
        finally
        {
            // Ensure monitor task is cancelled and completed
            await signalCts.CancelAsync().ConfigureAwait(false);
            try
            {
                await monitorTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected when we cancel the monitor
            }
        }
    }

    #endregion

    #region Signal Monitoring

    /// <summary>
    /// Background task that continuously monitors PLC signals.
    /// Detects: ErrorAlarm, CommandFailed, InboundCompleted.
    /// </summary>
    private async Task<SignalMonitorResult> MonitorSignalsAsync(
        SignalMonitorContext context,
        CancellationTokenSource signalCts,
        CancellationToken externalCancellation)
    {
        var pollInterval = TimeSpan.FromMilliseconds(200);
        var commandFailedAddress = PlcAddress.Parse(_signalMap.CommandFailed);
        var completionAddress = PlcAddress.Parse(_signalMap.InboundCompleted);

        while (!externalCancellation.IsCancellationRequested)
        {
            try
            {
                // Check for alarm
                var errorDetail = await CheckForAlarmAsync(externalCancellation).ConfigureAwait(false);

                if (errorDetail != null && _detectedSignal?.Type != SignalType.Alarm)
                {
                    var elapsed = (DateTimeOffset.UtcNow - context.StartTime).TotalMilliseconds;
                    context.Steps.Add($"⚠️ Alarm detected after {elapsed:F0}ms");
                    context.Steps.Add($"Error Code: {errorDetail.ErrorCode} - {errorDetail.ErrorMessage}");

                    _detectedSignal = new SignalMonitorResult
                    {
                        Type = SignalType.Alarm,
                        Error = errorDetail
                    };

                    // Send alarm notification to result channel (only once)
                    await SendAlarmNotificationAsync(context, errorDetail, externalCancellation)
                        .ConfigureAwait(false);

                    // If failOnAlarm, cancel the execution
                    if (_failOnAlarm)
                    {
                        await signalCts.CancelAsync().ConfigureAwait(false);
                        return _detectedSignal;
                    }
                }

                // Check CommandFailed flag
                var hasFailed = await _plcClient.ReadAsync<bool>(commandFailedAddress, externalCancellation)
                    .ConfigureAwait(false);

                if (hasFailed)
                {
                    _detectedSignal = new SignalMonitorResult
                    {
                        Type = SignalType.CommandFailed,
                        Error = _detectedSignal?.Error // Include alarm error if any
                    };

                    await signalCts.CancelAsync().ConfigureAwait(false);
                    return _detectedSignal;
                }

                // Check InboundCompleted flag
                var isCompleted = await _plcClient.ReadAsync<bool>(completionAddress, externalCancellation)
                    .ConfigureAwait(false);

                if (isCompleted)
                {
                    _detectedSignal = new SignalMonitorResult
                    {
                        Type = SignalType.InboundCompleted,
                        Error = _detectedSignal?.Error // Include alarm error if any (for warning)
                    };

                    await signalCts.CancelAsync().ConfigureAwait(false);
                    return _detectedSignal;
                }

                await Task.Delay(pollInterval, externalCancellation).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        return new SignalMonitorResult { Type = SignalType.None };
    }

    /// <summary>
    /// Sends alarm notification to result channel (only once per execution).
    /// </summary>
    private async Task SendAlarmNotificationAsync(
        SignalMonitorContext context,
        ErrorDetail errorDetail,
        CancellationToken cancellationToken)
    {
        if (_alarmNotificationSent)
            return;

        _alarmNotificationSent = true;

        var alarmNotification = new CommandResult
        {
            CommandId = context.Command.CommandId,
            PlcDeviceId = context.Command.PlcDeviceId ?? "Unknown",
            Status = ExecutionStatus.Error,
            Message = $"⚠️ Alarm detected during execution: {errorDetail}",
            StartedAt = context.StartTime,
            CompletedAt = DateTimeOffset.UtcNow,
            PlcError = errorDetail
        };

        await context.ResultChannel.Writer.WriteAsync(alarmNotification, cancellationToken)
            .ConfigureAwait(false);

        context.Steps.Add("Sent alarm notification to result channel");
    }

    /// <summary>
    /// Handles detected signal and returns appropriate result.
    /// </summary>
    private static CommandExecutionResult HandleDetectedSignal(
        SignalMonitorResult signal,
        List<string> steps,
        DateTimeOffset startTime)
    {
        var elapsed = (signal.DetectedAt - startTime).TotalMilliseconds;

        return signal.Type switch
        {
            SignalType.InboundCompleted => signal.Error != null
                ? CommandExecutionResult.Warning($"INBOUND completed with alarm after {elapsed:F0}ms", steps)
                : CommandExecutionResult.Success($"INBOUND completed after {elapsed:F0}ms", steps),

            SignalType.CommandFailed => CommandExecutionResult.Failed(
                $"Command failed after {elapsed:F0}ms",
                steps,
                signal.Error),

            SignalType.Alarm => CommandExecutionResult.Failed(
                $"Command failed due to alarm: {signal.Error}",
                steps,
                signal.Error),

            _ => CommandExecutionResult.Error("Unknown signal detected", steps)
        };
    }

    /// <summary>
    /// Checks for alarm condition and returns error details if alarm is active.
    /// </summary>
    /// <returns>ErrorDetail if alarm is active, null otherwise</returns>
    private async Task<ErrorDetail?> CheckForAlarmAsync(CancellationToken cancellationToken)
    {
        //var errorAlarmAddress = PlcAddress.Parse(_signalMap.ErrorAlarm);
        //var hasAlarm = await _plcClient.ReadAsync<bool>(errorAlarmAddress, cancellationToken)
        //    .ConfigureAwait(false);

        //if (!hasAlarm)
        //    return null;

        var errorCodeAddress = PlcAddress.Parse(_signalMap.ErrorCode);
        var errorCode = await _plcClient.ReadAsync<int>(errorCodeAddress, cancellationToken)
            .ConfigureAwait(false);

        if(errorCode == 0) return null;

        var errorMessage = PlcErrorCodeMapper.GetMessage(errorCode);
        return new ErrorDetail(errorCode, errorMessage);
    }

    #endregion

    #region Execution Steps

    /// <summary>
    /// Executes all INBOUND steps sequentially.
    /// </summary>
    private async Task<CommandExecutionResult> ExecuteStepsAsync(
        CommandEnvelope command,
        List<string> steps,
        CancellationToken cancellationToken)
    {
        // Step 1: Trigger INBOUND command
        await TriggerCommandAsync(steps, cancellationToken).ConfigureAwait(false);

        // Step 2: Start process execution
        await StartProcessAsync(steps, cancellationToken).ConfigureAwait(false);

        // Step 3: Read barcode from PLC
        var barcode = await ReadBarcodeAsync(command, steps, cancellationToken).ConfigureAwait(false);

        // Step 4: Validate barcode with user
        var validationResponse = await ValidateBarcodeAsync(command, barcode, steps, cancellationToken)
            .ConfigureAwait(false);

        // Step 5: Write validation result flags to PLC
        if (!validationResponse.IsValid)
        {
            await WriteBarcodeValidationFlagsAsync(isValid: false, steps, cancellationToken)
                .ConfigureAwait(false);

            return CommandExecutionResult.Failed($"Barcode rejected", steps);
        }

        await WriteBarcodeValidationFlagsAsync(isValid: true, steps, cancellationToken)
            .ConfigureAwait(false);

        // Step 6: Write command parameters (from validation response)
        await WriteCommandParametersAsync(
            validationResponse.DestinationLocation!,
            validationResponse.GateNumber!.Value,
            validationResponse.EnterDirection,
            steps,
            cancellationToken).ConfigureAwait(false);

        // Step 7: Wait for completion signal (now handled by monitor, but keep polling as backup)
        return await WaitForCompletionAsync(
            command,
            validationResponse.GateNumber!.Value,
            validationResponse.DestinationLocation!,
            steps,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Triggers the INBOUND command execution.
    /// </summary>
    private async Task TriggerCommandAsync(List<string> steps, CancellationToken cancellationToken)
    {
        await _plcClient.TriggerAsync(
            _signalMap.InboundTrigger,
            steps,
            "INBOUND command",
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Starts the process execution on PLC.
    /// </summary>
    private async Task StartProcessAsync(List<string> steps, CancellationToken cancellationToken)
    {
        await _plcClient.TriggerAsync(
            _signalMap.StartProcess,
            steps,
            "process execution",
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads barcode from PLC by polling 10 character registers.
    /// Polls until barcode != "0000000000" or cancellation.
    /// Each character is a single digit (0-9).
    /// Note: Alarm monitoring is handled by the background monitor task.
    /// </summary>
    private async Task<string> ReadBarcodeAsync(
        CommandEnvelope command,
        List<string> steps,
        CancellationToken cancellationToken)
    {
        var pollInterval = TimeSpan.FromMilliseconds(500);
        var startTime = DateTimeOffset.UtcNow;
        var defaultBarcode = "0000000000";

        steps.Add("Waiting for barcode...");

        while (!cancellationToken.IsCancellationRequested)
        {
            var barcodeChars = new List<string>(10);

            // Read all 10 barcode characters
            var addresses = new[]
            {
                _signalMap.BarcodeChar1, _signalMap.BarcodeChar2, _signalMap.BarcodeChar3,
                _signalMap.BarcodeChar4, _signalMap.BarcodeChar5, _signalMap.BarcodeChar6,
                _signalMap.BarcodeChar7, _signalMap.BarcodeChar8, _signalMap.BarcodeChar9,
                _signalMap.BarcodeChar10
            };

            for (int i = 0; i < addresses.Length; i++)
            {
                var charAddress = PlcAddress.Parse(addresses[i]);
                var charValue = await _plcClient.ReadAsync<string>(charAddress, cancellationToken)
                    .ConfigureAwait(false);

                if (charValue.Length != 1)
                {
                    steps.Add($"Stopped at position {i + 1}: character has length {charValue.Length} ('{charValue}'). Using {i} characters collected.");
                    break;
                }

                barcodeChars.Add(charValue);
            }

            // Construct barcode from collected characters
            var barcode = string.Join("", barcodeChars);

            // Check if barcode is not default (has actual data)
            if (barcode != defaultBarcode && !string.IsNullOrEmpty(barcode))
            {
                var elapsed = (DateTimeOffset.UtcNow - startTime).TotalMilliseconds;
                steps.Add($"Barcode detected after {elapsed:F0}ms: '{barcode}'");
                return barcode;
            }

            await Task.Delay(pollInterval, cancellationToken).ConfigureAwait(false);
        }

        // Cancellation (either external or from signal monitor)
        throw new OperationCanceledException("Barcode reading was cancelled", cancellationToken);
    }

    /// <summary>
    /// Validates barcode with user via callback.
    /// Requests validation and waits for user response (max 5 minutes).
    /// Returns validation response with approval/rejection and parameters.
    /// </summary>
    private async Task<BarcodeValidationResponse> ValidateBarcodeAsync(
        CommandEnvelope command,
        string barcode,
        List<string> steps,
        CancellationToken cancellationToken)
    {
        // Check if barcode is valid (not default)
        if (barcode == "0000000000")
        {
            steps.Add("No valid barcode detected");
            return new BarcodeValidationResponse
            {
                CommandId = command.CommandId,
                IsValid = false
            };
        }

        steps.Add($"Barcode read: {barcode}");

        // Request barcode validation from user (5 minute timeout)
        var validationRequest = new BarcodeReceivedEventArgs
        {
            TaskId = command.CommandId,
            DeviceId = command.PlcDeviceId ?? "Unknown",
            Barcode = barcode
        };

        var validationResponse = await _barcodeValidationCallback(validationRequest, cancellationToken)
            .ConfigureAwait(false);

        if (!validationResponse.IsValid)
        {
            steps.Add($"Barcode validation rejected.");
            return validationResponse;
        }

        // Validate that response contains required parameters
        if (validationResponse.DestinationLocation == null)
        {
            steps.Add("Validation response missing DestinationLocation");
            return new BarcodeValidationResponse
            {
                CommandId = command.CommandId,
                IsValid = false
            };
        }

        if (!validationResponse.GateNumber.HasValue || validationResponse.GateNumber <= 0)
        {
            steps.Add("Validation response missing or invalid GateNumber");
            return new BarcodeValidationResponse
            {
                CommandId = command.CommandId,
                IsValid = false
            };
        }

        steps.Add($"Barcode validation approved - Destination: {validationResponse.DestinationLocation}, Gate: {validationResponse.GateNumber}");

        return validationResponse;
    }

    #endregion

    #region PLC Write Operations

    /// <summary>
    /// Writes barcode validation result flags to PLC.
    /// Sets BarcodeValid flag based on validation result.
    /// </summary>
    private async Task WriteBarcodeValidationFlagsAsync(
        bool isValid,
        List<string> steps,
        CancellationToken cancellationToken)
    {
        if (isValid)
        {
            await _plcClient.WriteAsync(
                _signalMap.BarcodeValid,
                true,
                steps,
                "Wrote BarcodeValid flag (true)",
                cancellationToken).ConfigureAwait(false);

            await _plcClient.WriteAsync(
               _signalMap.BarcodeInvalid,
               false,
               steps,
               "Wrote BarcodeInvalid flag (false)",
               cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await _plcClient.WriteAsync(
                _signalMap.BarcodeInvalid,
                true,
                steps,
                "Wrote BarcodeInvalid flag (true)",
                cancellationToken).ConfigureAwait(false);

            await _plcClient.WriteAsync(
                _signalMap.BarcodeValid,
                false,
                steps,
                "Wrote BarcodeValid flag (false)",
                cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Writes command parameters to PLC (destination location, gate, directions).
    /// Called after barcode validation is approved.
    /// Parameters come from validation response, not from original command.
    /// </summary>
    private async Task WriteCommandParametersAsync(
        Location destinationLocation,
        int gateNumber,
        Direction? enterDirection,
        List<string> steps,
        CancellationToken cancellationToken)
    {
        // Write destination location (Floor, Rail, Block)
        await _plcClient.WriteLocationAsync(
            destinationLocation,
            _signalMap.TargetFloor,
            _signalMap.TargetRail,
            _signalMap.TargetBlock,
            steps,
            "destination",
            cancellationToken).ConfigureAwait(false);

        // Write gate number
        await _plcClient.WriteAsync(
            _signalMap.GateNumber,
            (short)gateNumber,
            steps,
            $"Wrote gate number: {gateNumber}",
            cancellationToken).ConfigureAwait(false);

        // Write enter direction (always write, false if not specified)
        await _plcClient.WriteDirectionAsync(
            _signalMap.EnterDirection,
            enterDirection,
            steps,
            "enter",
            cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region Completion Waiting

    /// <summary>
    /// Waits for completion signal as a backup.
    /// Primary signal detection is handled by the background monitor task.
    /// This method will be cancelled by the monitor when a signal is detected.
    /// </summary>
    private async Task<CommandExecutionResult> WaitForCompletionAsync(
        CommandEnvelope command,
        int gateNumber,
        Location destinationLocation,
        List<string> steps,
        CancellationToken cancellationToken)
    {
        var completionAddress = PlcAddress.Parse(_signalMap.InboundCompleted);
        var commandFailedAddress = PlcAddress.Parse(_signalMap.CommandFailed);
        var pollInterval = TimeSpan.FromMilliseconds(500);
        var startTime = DateTimeOffset.UtcNow;

        steps.Add("Waiting for completion signal...");

        while (!cancellationToken.IsCancellationRequested)
        {
            // Check CommandFailed flag (backup check)
            var hasFailed = await _plcClient.ReadAsync<bool>(commandFailedAddress, cancellationToken)
                .ConfigureAwait(false);

            if (hasFailed)
            {
                var elapsed = (DateTimeOffset.UtcNow - startTime).TotalMilliseconds;
                steps.Add($"Command failed after {elapsed:F0}ms");
                return CommandExecutionResult.Failed(
                    "PLC signaled command failure (CommandFailed flag set)",
                    steps,
                    _detectedSignal?.Error);
            }

            // Check InboundCompleted flag (backup check)
            var isCompleted = await _plcClient.ReadAsync<bool>(completionAddress, cancellationToken)
                .ConfigureAwait(false);

            if (isCompleted)
            {
                var elapsed = (DateTimeOffset.UtcNow - startTime).TotalMilliseconds;
                steps.Add($"Command completed after {elapsed:F0}ms");

                var hasAlarm = _detectedSignal?.Type == SignalType.Alarm;
                var message = hasAlarm
                    ? $"INBOUND completed with alarm: Gate {gateNumber} → {destinationLocation}"
                    : $"INBOUND completed: Gate {gateNumber} → {destinationLocation}";

                return hasAlarm
                    ? CommandExecutionResult.Warning(message, steps)
                    : CommandExecutionResult.Success(message, steps);
            }

            await Task.Delay(pollInterval, cancellationToken).ConfigureAwait(false);
        }

        // Cancelled by signal monitor or external cancellation
        throw new OperationCanceledException("Command execution was cancelled", cancellationToken);
    }

    #endregion
}
