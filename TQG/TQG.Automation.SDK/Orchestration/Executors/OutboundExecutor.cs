using System.Threading.Channels;
using TQG.Automation.SDK.Clients;
using TQG.Automation.SDK.Core;
using TQG.Automation.SDK.Orchestration.Models;
using TQG.Automation.SDK.Shared;

namespace TQG.Automation.SDK.Orchestration.Executors;

/// <summary>
/// Executes OUTBOUND commands: handles outgoing material flow from warehouse.
/// Flow: Trigger → Write Parameters → Start Process → Wait for Result.
/// </summary>
internal sealed class OutboundExecutor(IPlcClient plcClient, SignalMap signalMap, bool failOnAlarm)
{
    private readonly IPlcClient _plcClient = plcClient ?? throw new ArgumentNullException(nameof(plcClient));
    private readonly SignalMap _signalMap = signalMap ?? throw new ArgumentNullException(nameof(signalMap));
    private readonly bool _failOnAlarm = failOnAlarm;

    /// <summary>
    /// Executes an OUTBOUND command: send material from warehouse to external destination.
    /// Flow: Write Parameters → Trigger → Start Process → Wait for Result.
    /// </summary>
    public async Task<CommandExecutionResult> ExecuteAsync(
        CommandEnvelope command,
        Channel<CommandResult> resultChannel,
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
    /// Alarm handling:
    /// 1. Always sends immediate Warning notification to resultChannel when alarm detected (only once to avoid duplicates)
    /// 2. If failOnAlarm=true: returns Failed immediately after sending notification
    /// 3. If failOnAlarm=false: continues waiting for CommandFailed or Completed
    /// This prevents duplicate error notifications since errorCode may not be reset immediately.
    /// </summary>
    private async Task<CommandExecutionResult> WaitForCommandResultAsync(
        CommandEnvelope command,
        Channel<CommandResult> resultChannel,
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
