using System.Threading.Channels;
using TQG.Automation.SDK.Clients;
using TQG.Automation.SDK.Core;
using TQG.Automation.SDK.Events;
using TQG.Automation.SDK.Orchestration.Models;
using TQG.Automation.SDK.Shared;

namespace TQG.Automation.SDK.Orchestration.Executors;

/// <summary>
/// Executes INBOUND commands: handles incoming material flow to warehouse.
/// Flow: Trigger → Start Process → Read Barcode → Validate → Write Parameters → Wait for Result.
/// </summary>
internal sealed class InboundExecutor(
    IPlcClient plcClient,
    SignalMap signalMap,
    bool failOnAlarm,
    Func<BarcodeReceivedEventArgs, CancellationToken, Task<BarcodeValidationResponse>> barcodeValidationCallback)
{
    private readonly IPlcClient _plcClient = plcClient ?? throw new ArgumentNullException(nameof(plcClient));
    private readonly SignalMap _signalMap = signalMap ?? throw new ArgumentNullException(nameof(signalMap));
    private readonly bool _failOnAlarm = failOnAlarm;
    private readonly Func<BarcodeReceivedEventArgs, CancellationToken, Task<BarcodeValidationResponse>>
        _barcodeValidationCallback = barcodeValidationCallback ?? throw new ArgumentNullException(nameof(barcodeValidationCallback));

    /// <summary>
    /// Executes an INBOUND command: receive material from external source.
    /// Flow: Trigger → Start Process → Read & Validate Barcode → Write Parameters → Wait for Result.
    /// </summary>
    public async Task<CommandExecutionResult> ExecuteAsync(
        CommandEnvelope command,
        Channel<CommandResult> resultChannel,
        CancellationToken cancellationToken)
    {
        if (command.CommandType != CommandType.Inbound)
            throw new ArgumentException($"Invalid command type: {command.CommandType}. Expected Inbound.", nameof(command));

        var steps = new List<string>();

        try
        {
            // Step 1: Trigger INBOUND command
            await TriggerCommandAsync(steps, cancellationToken).ConfigureAwait(false);

            // Step 2: Start process execution
            await StartProcessAsync(steps, cancellationToken).ConfigureAwait(false);

            // Step 3: Read barcode from PLC
            var barcode = await ReadBarcodeAsync(steps, cancellationToken).ConfigureAwait(false);

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

            // Step 7: Wait for result (CommandFailed or InboundCompleted)
            var result = await WaitForCommandResultAsync(
                command,
                validationResponse.GateNumber!.Value,
                validationResponse.DestinationLocation!,
                resultChannel,
                steps,
                cancellationToken).ConfigureAwait(false);

            return result;
        }
        catch (OperationCanceledException)
        {
            throw; // Let caller handle cancellation
        }
        catch (Exception ex)
        {
            steps.Add($"Error: {ex.Message}");
            return CommandExecutionResult.Error(
                $"INBOUND execution failed: {ex.Message}",
                steps);
        }
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
    /// Polls until barcode != "0000000000" or timeout.
    /// Each character is a single digit (0-9).
    /// </summary>
    private async Task<string> ReadBarcodeAsync(List<string> steps, CancellationToken cancellationToken)
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

        // Timeout or cancellation
        throw new OperationCanceledException("Barcode reading was cancelled", cancellationToken);
    }

    /// <summary>
    /// Validates barcode with user via callback.
    /// Requests validation and waits for user response (max 2 minutes).
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

        // Request barcode validation from user (2 minute timeout)
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
               "Wrote BarcodeInvalid flag (true)",
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
                "Wrote BarcodeValid flag (true)",
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

    /// <summary>
    /// Waits for command result by polling ErrorAlarm, CommandFailed and InboundCompleted flags.
    /// Alarm handling:
    /// 1. Always sends immediate Warning notification to resultChannel when alarm detected (only once to avoid duplicates)
    /// 2. If failOnAlarm=true: returns Failed immediately after sending notification
    /// 3. If failOnAlarm=false: continues waiting for CommandFailed or Completed
    /// This prevents duplicate error notifications since errorCode may not be reset immediately.
    /// </summary>
    private async Task<CommandExecutionResult> WaitForCommandResultAsync(
        CommandEnvelope command,
        int gateNumber,
        Location destinationLocation,
        Channel<CommandResult> resultChannel,
        List<string> steps,
        CancellationToken cancellationToken)
    {
        var errorAlarmAddress = PlcAddress.Parse(_signalMap.ErrorAlarm);
        var errorCodeAddress = PlcAddress.Parse(_signalMap.ErrorCode);
        var commandFailedAddress = PlcAddress.Parse(_signalMap.CommandFailed);
        var completionAddress = PlcAddress.Parse(_signalMap.InboundCompleted);
        var pollInterval = TimeSpan.FromMilliseconds(500);
        var startTime = DateTimeOffset.UtcNow;
        var alarmDetected = false;
        var alarmNotificationSent = false;
        ErrorDetail? detectedError = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            // Check ErrorAlarm flag
            var hasAlarm = await _plcClient.ReadAsync<bool>(errorAlarmAddress, cancellationToken)
                .ConfigureAwait(false);

            if (hasAlarm && !alarmDetected)
            {
                alarmDetected = true;
                var elapsed = (DateTimeOffset.UtcNow - startTime).TotalMilliseconds;
                steps.Add($"⚠️ Alarm detected after {elapsed:F0}ms");

                // Read error code from PLC
                var errorCode = await _plcClient.ReadAsync<int>(errorCodeAddress, cancellationToken)
                    .ConfigureAwait(false);

                var errorMessage = PlcErrorCodeMapper.GetMessage(errorCode);
                steps.Add($"Error Code: {errorCode} - {errorMessage}");

                detectedError = new ErrorDetail(errorCode, errorMessage);

                // Send immediate alarm notification to result channel (only once)
                if (!alarmNotificationSent)
                {
                    alarmNotificationSent = true;
                    var alarmNotification = new CommandResult
                    {
                        CommandId = command.CommandId,
                        PlcDeviceId = command.PlcDeviceId ?? "Unknown",
                        Status = ExecutionStatus.Error,
                        Message = $"⚠️ Alarm detected during execution: {detectedError}",
                        StartedAt = startTime,
                        CompletedAt = DateTimeOffset.UtcNow,
                        PlcError = detectedError
                    };

                    await resultChannel.Writer.WriteAsync(alarmNotification, cancellationToken)
                        .ConfigureAwait(false);
                    
                    steps.Add("Sent alarm notification to result channel");
                }

                // If failOnAlarm is enabled, return Failed immediately
                if (_failOnAlarm)
                {
                    return CommandExecutionResult.Failed(
                        $"Command failed due to alarm (failOnAlarm=true): {detectedError}",
                        steps,
                        detectedError);
                }

                // Otherwise continue waiting for Completed or Failed flag
            }

            // Check CommandFailed flag
            var hasFailed = await _plcClient.ReadAsync<bool>(commandFailedAddress, cancellationToken)
                .ConfigureAwait(false);

            if (hasFailed)
            {
                var elapsed = (DateTimeOffset.UtcNow - startTime).TotalMilliseconds;
                steps.Add($"Command failed after {elapsed:F0}ms");

                var message = alarmDetected
                    ? $"PLC signaled command failure (CommandFailed flag set, alarm: {detectedError})"
                    : "PLC signaled command failure (CommandFailed flag set)";

                return CommandExecutionResult.Failed(message, steps, detectedError);
            }

            // Check InboundCompleted flag
            var isCompleted = await _plcClient.ReadAsync<bool>(completionAddress, cancellationToken)
                .ConfigureAwait(false);

            if (isCompleted)
            {
                var elapsed = (DateTimeOffset.UtcNow - startTime).TotalMilliseconds;
                steps.Add($"Command completed after {elapsed:F0}ms");

                var message = alarmDetected
                    ? $"INBOUND completed with alarm: Gate {gateNumber} → {destinationLocation}"
                    : $"INBOUND completed: Gate {gateNumber} → {destinationLocation}";

                var status = alarmDetected
                    ? CommandExecutionResult.Warning(message, steps)
                    : CommandExecutionResult.Success(message, steps);

                return status;
            }

            await Task.Delay(pollInterval, cancellationToken).ConfigureAwait(false);
        }

        // This should not be reached as Task.Delay will throw OperationCanceledException
        // when cancellationToken is cancelled, but include for completeness
        var totalElapsed = (DateTimeOffset.UtcNow - startTime).TotalSeconds;
        steps.Add($"Cancelled after {totalElapsed:F1}s");
        throw new OperationCanceledException("Command execution was cancelled", cancellationToken);
    }
}
