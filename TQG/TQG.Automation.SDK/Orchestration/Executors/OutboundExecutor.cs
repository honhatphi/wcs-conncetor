using TQG.Automation.SDK.Core;
using TQG.Automation.SDK.Models;
using TQG.Automation.SDK.Orchestration.Models;

namespace TQG.Automation.SDK.Orchestration.Executors;

/// <summary>
/// Executes OUTBOUND commands: handles outgoing material flow from warehouse.
/// Simplified flow: Trigger → Write Source Location (location + gate + direction) → Wait for completion.
/// </summary>
internal sealed class OutboundExecutor(IPlcClient plcClient, SignalMap signalMap, bool stopOnAlarm)
{
    private readonly IPlcClient _plcClient = plcClient ?? throw new ArgumentNullException(nameof(plcClient));
    private readonly SignalMap _signalMap = signalMap ?? throw new ArgumentNullException(nameof(signalMap));
    private readonly bool _stopOnAlarm = stopOnAlarm;

    /// <summary>
    /// Executes an OUTBOUND command: send material from warehouse to external destination.
    /// Flow: Write Parameters → Trigger → Start Process → Wait for Result.
    /// </summary>
    public async Task<CommandExecutionResult> ExecuteAsync(
        CommandEnvelope command,
        CancellationToken cancellationToken)
    {
        if (command.CommandType != CommandType.Outbound)
            throw new ArgumentException($"Invalid command type: {command.CommandType}. Expected Outbound.", nameof(command));

        var steps = new List<string>();

        try
        {
            // Step 1: Write all command parameters
            await WriteCommandParametersAsync(command, steps, cancellationToken).ConfigureAwait(false);

            // Step 2: Trigger OUTBOUND command
            await TriggerCommandAsync(steps, cancellationToken).ConfigureAwait(false);

            // Step 3: Start process execution
            await StartProcessAsync(steps, cancellationToken).ConfigureAwait(false);

            // Step 4: Wait for result (CommandFailed or OutboundCompleted)
            var result = await WaitForCommandResultAsync(
                command,
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
                $"OUTBOUND execution failed: {ex.Message}",
                steps);
        }
    }

    /// <summary>
    /// Writes all command parameters to PLC (source location, gate, directions).
    /// </summary>
    private async Task WriteCommandParametersAsync(
        CommandEnvelope command,
        List<string> steps,
        CancellationToken cancellationToken)
    {
        // Write source location (Floor, Rail, Block)
        await _plcClient.WriteLocationAsync(
            command.SourceLocation!,
            _signalMap.SourceFloor,
            _signalMap.SourceRail,
            _signalMap.SourceBlock,
            steps,
            "source",
            cancellationToken).ConfigureAwait(false);

        // Write gate number (always write, default to value from command)
        await _plcClient.WriteAsync(
            _signalMap.GateNumber,
            (short)command.GateNumber,
            steps,
            $"Wrote gate number: {command.GateNumber}",
            cancellationToken).ConfigureAwait(false);

        // Write exit direction (always write, false if not specified)
        await _plcClient.WriteDirectionAsync(
            _signalMap.ExitDirection,
            command.ExitDirection,
            steps,
            "exit",
            cancellationToken).ConfigureAwait(false);

        // Write enter direction (always write, false if not specified)
        await _plcClient.WriteDirectionAsync(
            _signalMap.EnterDirection,
            command.EnterDirection,
            steps,
            "enter",
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Triggers the OUTBOUND command execution.
    /// </summary>
    private async Task TriggerCommandAsync(List<string> steps, CancellationToken cancellationToken)
    {
        await _plcClient.TriggerAsync(
            _signalMap.OutboundTrigger,
            steps,
            "OUTBOUND command",
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
    /// Waits for command result by polling ErrorAlarm, CommandFailed and OutboundCompleted flags.
    /// Alarm behavior depends on StopOnAlarm configuration.
    /// Reads and logs error code when alarm is detected.
    /// Polling continues until cancellationToken is cancelled (timeout handled by caller).
    /// </summary>
    private async Task<CommandExecutionResult> WaitForCommandResultAsync(
        CommandEnvelope command,
        List<string> steps,
        CancellationToken cancellationToken)
    {
        var errorAlarmAddress = PlcAddress.Parse(_signalMap.ErrorAlarm);
        var errorCodeAddress = PlcAddress.Parse(_signalMap.ErrorCode);
        var commandFailedAddress = PlcAddress.Parse(_signalMap.CommandFailed);
        var completionAddress = PlcAddress.Parse(_signalMap.OutboundCompleted);
        var pollInterval = TimeSpan.FromMilliseconds(500);
        var startTime = DateTimeOffset.UtcNow;
        var alarmDetected = false;
        PlcError? detectedError = null;

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

                detectedError = new PlcError(errorCode, errorMessage, command.CommandId);

                if (_stopOnAlarm)
                {
                    // Stop immediately on alarm
                    return CommandExecutionResult.Error(
                        $"Command stopped: {detectedError}",
                        steps,
                        detectedError);
                }
                // Otherwise continue to wait for CommandFailed or Completed
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

            // Check OutboundCompleted flag
            var isCompleted = await _plcClient.ReadAsync<bool>(completionAddress, cancellationToken)
                .ConfigureAwait(false);

            if (isCompleted)
            {
                var elapsed = (DateTimeOffset.UtcNow - startTime).TotalMilliseconds;
                steps.Add($"Command completed after {elapsed:F0}ms");

                var message = alarmDetected
                    ? $"OUTBOUND completed with alarm: {command.SourceLocation} → Gate {command.GateNumber}"
                    : $"OUTBOUND completed: {command.SourceLocation} → Gate {command.GateNumber}";

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
