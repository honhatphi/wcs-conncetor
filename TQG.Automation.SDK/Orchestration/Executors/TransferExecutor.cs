using System.Threading.Channels;
using TQG.Automation.SDK.Clients;
using TQG.Automation.SDK.Core;
using TQG.Automation.SDK.Orchestration.Executors.Strategies;
using TQG.Automation.SDK.Orchestration.Services;
using TQG.Automation.SDK.Shared;

namespace TQG.Automation.SDK.Orchestration.Executors;

/// <summary>
/// Executes TRANSFER commands: handles internal material movement within warehouse.
/// Moves material from one warehouse location to another via PLC.
/// Signal monitoring runs continuously throughout the entire execution process.
/// </summary>
internal sealed class TransferExecutor(IPlcClient plcClient, SignalMap signalMap, bool failOnAlarm)
{
    private readonly IPlcClient _plcClient = plcClient ?? throw new ArgumentNullException(nameof(plcClient));
    private readonly SignalMap _signalMap = signalMap ?? throw new ArgumentNullException(nameof(signalMap));
    private readonly bool _failOnAlarm = failOnAlarm;
    private readonly TransferStrategy _strategy = new();
    private readonly SignalMonitorService _signalMonitor = new(plcClient, signalMap);

    /// <summary>
    /// Executes a TRANSFER command with continuous signal monitoring.
    /// Signal monitoring detects Alarm, CommandFailed, and TransferCompleted in parallel with execution.
    /// </summary>
    public async Task<Models.CommandExecutionResult> ExecuteAsync(
        Models.CommandEnvelope command,
        Channel<Models.CommandResult> resultChannel,
        CancellationToken cancellationToken)
    {
        // Validate command
        _strategy.ValidateCommand(command);

        var steps = new List<string>();
        var startTime = DateTimeOffset.UtcNow;

        // Create linked cancellation token for signal monitoring
        using var signalCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var linkedToken = signalCts.Token;

        // Create signal monitor context
        var monitorContext = new Models.SignalMonitorContext
        {
            CommandId = command.CommandId,
            PlcDeviceId = command.PlcDeviceId ?? "Unknown",
            SlotId = command.SlotId ?? 0,
            CompletionSignalAddress = _strategy.GetCompletionAddress(_signalMap),
            ResultChannel = resultChannel,
            Steps = steps,
            StartTime = startTime,
            FailOnAlarm = _failOnAlarm
        };

        // Start background signal monitoring
        var monitorTask = _signalMonitor.MonitorSignalsAsync(monitorContext, signalCts, cancellationToken);

        try
        {
            // Execute all steps with linked token
            var executionTask = ExecuteStepsAsync(command, steps, linkedToken);

            // Wait for either execution to complete or signal detection
            var completedTask = await Task.WhenAny(executionTask, monitorTask).ConfigureAwait(false);

            if (completedTask == monitorTask)
            {
                var signalResult = await monitorTask.ConfigureAwait(false);
                return HandleSignalResult(signalResult, command, steps, startTime);
            }

            return await executionTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Cancelled by signal monitor - get the detected signal
            var signal = await GetDetectedSignalAsync(monitorTask).ConfigureAwait(false);
            if (signal != null && signal.Type != Models.SignalType.None)
            {
                return HandleSignalResult(signal, command, steps, startTime);
            }
            throw;
        }
        catch (OperationCanceledException)
        {
            throw; // External cancellation
        }
        catch (Exception ex)
        {
            steps.Add($"Error: {ex.Message}");
            return Models.CommandExecutionResult.Failed(
                $"TRANSFER execution failed: {ex.Message}",
                steps);
        }
        finally
        {
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

    /// <summary>
    /// Executes all TRANSFER steps sequentially.
    /// </summary>
    private async Task<Models.CommandExecutionResult> ExecuteStepsAsync(
        Models.CommandEnvelope command,
        List<string> steps,
        CancellationToken cancellationToken)
    {
        // Step 1: Write all command parameters (using strategy)
        await _strategy.WriteParametersAsync(
            _plcClient, _signalMap, command, steps, cancellationToken)
            .ConfigureAwait(false);

        // Step 2: Trigger TRANSFER command
        await _plcClient.TriggerAsync(
            _strategy.GetTriggerAddress(_signalMap),
            steps,
            "TRANSFER command",
            cancellationToken).ConfigureAwait(false);

        // Step 3: Start process execution
        await _plcClient.TriggerAsync(
            _signalMap.StartProcess,
            steps,
            "process execution",
            cancellationToken).ConfigureAwait(false);

        // Step 4: Wait for completion (handled by signal monitor, this is backup loop)
        return await WaitForCompletionBackupAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Backup wait loop - Signal monitor handles completion detection.
    /// </summary>
    private static async Task<Models.CommandExecutionResult> WaitForCompletionBackupAsync(
        CancellationToken cancellationToken)
    {
        var pollInterval = TimeSpan.FromMilliseconds(500);

        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(pollInterval, cancellationToken).ConfigureAwait(false);
        }

        // Will be cancelled by signal monitor or external cancellation
        throw new OperationCanceledException(cancellationToken);
    }

    /// <summary>
    /// Handles detected signal and returns appropriate result.
    /// </summary>
    private Models.CommandExecutionResult HandleSignalResult(
        Models.SignalMonitorResult signal,
        Models.CommandEnvelope command,
        List<string> steps,
        DateTimeOffset startTime)
    {
        return signal.Type switch
        {
            Models.SignalType.CommandCompleted => Models.CommandExecutionResult.Success(
                _strategy.BuildSuccessMessage(command, hasWarning: signal.Error != null), steps),

            Models.SignalType.CommandFailed => Models.CommandExecutionResult.Failed(
                _strategy.BuildFailureMessage(command, signal.Error),
                steps,
                signal.Error),

            Models.SignalType.Alarm => Models.CommandExecutionResult.Failed(
                $"Command failed due to alarm (failOnAlarm=true): {signal.Error}",
                steps,
                signal.Error),

            _ => Models.CommandExecutionResult.Failed("Unknown signal detected", steps)
        };
    }

    /// <summary>
    /// Safely gets the detected signal from monitor task.
    /// </summary>
    private static async Task<Models.SignalMonitorResult?> GetDetectedSignalAsync(
        Task<Models.SignalMonitorResult> monitorTask)
    {
        try
        {
            return monitorTask.IsCompleted ? await monitorTask.ConfigureAwait(false) : null;
        }
        catch
        {
            return null;
        }
    }
}
