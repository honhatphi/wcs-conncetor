using TQG.Automation.SDK.Clients;
using TQG.Automation.SDK.Core;
using TQG.Automation.SDK.Orchestration.Models;
using TQG.Automation.SDK.Shared;

namespace TQG.Automation.SDK.Orchestration.Executors;

/// <summary>
/// Executes CHECKPALLET commands: verifies pallet presence at a location.
/// Flow: Trigger → Write Location → Start Process → Wait for Result.
/// Always fails on alarm regardless of failOnAlarm setting.
/// </summary>
internal sealed class CheckExecutor(IPlcClient plcClient, SignalMap signalMap)
{
    private readonly IPlcClient _plcClient = plcClient ?? throw new ArgumentNullException(nameof(plcClient));
    private readonly SignalMap _signalMap = signalMap ?? throw new ArgumentNullException(nameof(signalMap));

    /// <summary>
    /// Executes a CHECK PALLET command: verify pallet existence at source location.
    /// Flow: Write Parameters → Trigger → Start Process → Wait for Result.
    /// </summary>
    public async Task<CommandExecutionResult> ExecuteAsync(
        CommandEnvelope command,
        CancellationToken cancellationToken)
    {
        if (command.CommandType != CommandType.CheckPallet)
            throw new ArgumentException($"Invalid command type: {command.CommandType}. Expected CheckPallet.", nameof(command));

        var steps = new List<string>();

        try
        {
            // Step 1: Write all command parameters (including depth)
            await WriteCommandParametersAsync(command, steps, cancellationToken).ConfigureAwait(false);

            // Step 2: Trigger CHECK PALLET command
            await TriggerCommandAsync(steps, cancellationToken).ConfigureAwait(false);

            // Step 3: Start process execution
            await StartProcessAsync(steps, cancellationToken).ConfigureAwait(false);

            // Step 4: Wait for result (with pallet availability check)
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
                $"CHECK PALLET execution failed: {ex.Message}",
                steps);
        }
    }

    /// <summary>
    /// Writes all command parameters to PLC (source location with depth).
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

        // Write source depth (required for CheckPallet)
        await _plcClient.WriteAsync(
            _signalMap.SourceDepth,
            (short)command.SourceLocation!.Depth,
            steps,
            $"Wrote source depth: {command.SourceLocation.Depth}",
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Triggers the CHECK PALLET command execution.
    /// </summary>
    private async Task TriggerCommandAsync(List<string> steps, CancellationToken cancellationToken)
    {
        await _plcClient.TriggerAsync(
            _signalMap.PalletCheckTrigger,
            steps,
            "CHECK PALLET command",
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
    /// Waits for command result by polling ErrorAlarm, PalletAvailable, PalletUnavailable flags.
    /// Always fails on alarm for CheckPallet operations (ignores failOnAlarm setting).
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
        var completionAddress = PlcAddress.Parse(_signalMap.PalletCheckCompleted);
        var availableAddress = PlcAddress.Parse(_signalMap.AvailablePallet);
        var unavailableAddress = PlcAddress.Parse(_signalMap.UnavailablePallet);
        var pollInterval = TimeSpan.FromMilliseconds(200);
        var startTime = DateTimeOffset.UtcNow;

        while (!cancellationToken.IsCancellationRequested)
        {
            // Check ErrorAlarm flag - ALWAYS stop on alarm for CheckPallet
            var hasAlarm = await _plcClient.ReadAsync<bool>(errorAlarmAddress, cancellationToken)
                .ConfigureAwait(false);

            if (hasAlarm)
            {
                var elapsed = (DateTimeOffset.UtcNow - startTime).TotalMilliseconds;
                steps.Add($"⚠️ Alarm detected after {elapsed:F0}ms");

                // Read error code from PLC
                var errorCode = await _plcClient.ReadAsync<int>(errorCodeAddress, cancellationToken)
                    .ConfigureAwait(false);

                var errorMessage = PlcErrorCodeMapper.GetMessage(errorCode);
                steps.Add($"Error Code: {errorCode} - {errorMessage}");

                var plcError = new ErrorDetail(errorCode, errorMessage);

                // Always stop on alarm for CheckPallet operations
                return CommandExecutionResult.Failed(
                    $"Check pallet stopped: {plcError}",
                    steps,
                    plcError);
            }

            // Check CommandFailed flag
            var hasFailed = await _plcClient.ReadAsync<bool>(commandFailedAddress, cancellationToken)
                .ConfigureAwait(false);

            if (hasFailed)
            {
                var elapsed = (DateTimeOffset.UtcNow - startTime).TotalMilliseconds;
                steps.Add($"Command failed after {elapsed:F0}ms");

                return CommandExecutionResult.Failed(
                    "PLC signaled check pallet failure (CommandFailed flag set)",
                    steps);
            }

            // Check PalletCheckCompleted flag
            var isCompleted = await _plcClient.ReadAsync<bool>(completionAddress, cancellationToken)
                .ConfigureAwait(false);

            if (isCompleted)
            {
                var elapsed = (DateTimeOffset.UtcNow - startTime).TotalMilliseconds;
                steps.Add($"Check completed after {elapsed:F0}ms");

                // Read pallet availability status
                var isAvailable = await _plcClient.ReadAsync<bool>(availableAddress, cancellationToken)
                    .ConfigureAwait(false);
                var isUnavailable = await _plcClient.ReadAsync<bool>(unavailableAddress, cancellationToken)
                    .ConfigureAwait(false);

                steps.Add($"Pallet status: Available={isAvailable}, Unavailable={isUnavailable}");

                string message;
                if (isAvailable)
                {
                    message = $"CHECK PALLET completed: Pallet found at {command.SourceLocation}";
                }
                else if (isUnavailable)
                {
                    message = $"CHECK PALLET completed: No pallet at {command.SourceLocation}";
                }
                else
                {
                    message = $"CHECK PALLET completed: Pallet status unclear at {command.SourceLocation}";
                }

                return new CommandExecutionResult
                {
                    Status = ExecutionStatus.Success,
                    Message = message,
                    PalletAvailable = isAvailable,
                    PalletUnavailable = isUnavailable,
                    ExecutionSteps = steps
                };
            }

            await Task.Delay(pollInterval, cancellationToken).ConfigureAwait(false);
        }

        // This should not be reached as Task.Delay will throw OperationCanceledException
        // when cancellationToken is cancelled, but include for completeness
        var totalElapsed = (DateTimeOffset.UtcNow - startTime).TotalSeconds;
        steps.Add($"Cancelled after {totalElapsed:F1}s");
        throw new OperationCanceledException("Check pallet execution was cancelled", cancellationToken);
    }
}
