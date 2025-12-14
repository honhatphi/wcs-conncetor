using System.Threading.Channels;
using TQG.Automation.SDK.Clients;
using TQG.Automation.SDK.Core;
using TQG.Automation.SDK.Orchestration.Executors.Strategies;
using TQG.Automation.SDK.Orchestration.Services;
using TQG.Automation.SDK.Shared;
using Models = TQG.Automation.SDK.Orchestration.Models;

namespace TQG.Automation.SDK.Orchestration.Executors.Base;

/// <summary>
/// Base executor using Template Method pattern with Signal Monitor.
/// Provides common execution flow for all command types:
/// 1. Validate command
/// 2. Start signal monitoring (background)
/// 3. Execute pre-trigger logic (optional)
/// 4. Write parameters
/// 5. Trigger command
/// 6. Start process
/// 7. Execute post-trigger logic (optional)
/// 8. Wait for completion signal
/// </summary>
internal abstract class CommandExecutorBase
{
    protected readonly IPlcClient PlcClient;
    protected readonly SignalMap SignalMap;
    protected readonly bool FailOnAlarm;
    protected readonly SignalMonitorService SignalMonitor;

    protected CommandExecutorBase(
        IPlcClient plcClient,
        SignalMap signalMap,
        bool failOnAlarm)
    {
        PlcClient = plcClient ?? throw new ArgumentNullException(nameof(plcClient));
        SignalMap = signalMap ?? throw new ArgumentNullException(nameof(signalMap));
        FailOnAlarm = failOnAlarm;
        SignalMonitor = new SignalMonitorService(plcClient, signalMap);
    }

    /// <summary>
    /// Gets the strategy for this executor.
    /// </summary>
    protected abstract ICommandStrategy Strategy { get; }

    /// <summary>
    /// Template method for executing commands.
    /// Runs signal monitoring in parallel with main execution flow.
    /// </summary>
    public async Task<Models.CommandExecutionResult> ExecuteAsync(
        Models.CommandEnvelope command,
        Channel<Models.CommandResult> resultChannel,
        CancellationToken cancellationToken)
    {
        // Validate command
        Strategy.ValidateCommand(command);

        var steps = new List<string>();
        var startTime = DateTimeOffset.UtcNow;

        // Create linked cancellation for signal monitoring
        using var signalCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var linkedToken = signalCts.Token;

        // Create signal monitor context
        var monitorContext = new Models.SignalMonitorContext
        {
            CommandId = command.CommandId,
            PlcDeviceId = command.PlcDeviceId ?? "Unknown",
            CompletionSignalAddress = Strategy.GetCompletionAddress(SignalMap),
            ResultChannel = resultChannel,
            Steps = steps,
            StartTime = startTime,
            FailOnAlarm = FailOnAlarm
        };

        // Start background signal monitoring
        var monitorTask = SignalMonitor.MonitorSignalsAsync(monitorContext, signalCts, cancellationToken);

        try
        {
            // Execute main flow with linked token
            var executionTask = ExecuteMainFlowAsync(command, resultChannel, steps, linkedToken);

            // Wait for either completion or signal detection
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
            // Cancelled by signal monitor
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
            return Models.CommandExecutionResult.Error(
                $"{Strategy.SupportedCommandType} execution failed: {ex.Message}",
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
    /// Main execution flow - can be overridden for special cases.
    /// </summary>
    protected virtual async Task<Models.CommandExecutionResult> ExecuteMainFlowAsync(
        Models.CommandEnvelope command,
        Channel<Models.CommandResult> resultChannel,
        List<string> steps,
        CancellationToken cancellationToken)
    {
        // Pre-trigger logic (optional)
        var preResult = await Strategy.ExecutePreTriggerAsync(
            PlcClient, SignalMap, command, resultChannel, steps, cancellationToken)
            .ConfigureAwait(false);
        if (preResult != null) return preResult;

        // Write parameters
        await Strategy.WriteParametersAsync(
            PlcClient, SignalMap, command, steps, cancellationToken)
            .ConfigureAwait(false);

        // Trigger command
        await TriggerCommandAsync(steps, cancellationToken).ConfigureAwait(false);

        // Start process
        await StartProcessAsync(steps, cancellationToken).ConfigureAwait(false);

        // Post-trigger logic (optional, e.g., barcode reading for Inbound)
        var postResult = await Strategy.ExecutePostTriggerAsync(
            PlcClient, SignalMap, command, resultChannel, steps, cancellationToken)
            .ConfigureAwait(false);
        if (postResult != null) return postResult;

        // Wait for completion (handled by signal monitor, this is backup)
        return await WaitForCompletionAsync(command, steps, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Triggers the command on PLC.
    /// </summary>
    protected async Task TriggerCommandAsync(List<string> steps, CancellationToken cancellationToken)
    {
        await PlcClient.TriggerAsync(
            Strategy.GetTriggerAddress(SignalMap),
            steps,
            $"{Strategy.SupportedCommandType} command",
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Starts process execution on PLC.
    /// </summary>
    protected async Task StartProcessAsync(List<string> steps, CancellationToken cancellationToken)
    {
        await PlcClient.TriggerAsync(
            SignalMap.StartProcess,
            steps,
            "process execution",
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Backup wait loop - signal monitor typically handles completion detection.
    /// </summary>
    protected virtual async Task<Models.CommandExecutionResult> WaitForCompletionAsync(
        Models.CommandEnvelope command,
        List<string> steps,
        CancellationToken cancellationToken)
    {
        var pollInterval = TimeSpan.FromMilliseconds(500);

        // Keep waiting until signal monitor detects completion or cancellation
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
        var elapsed = (signal.DetectedAt - startTime).TotalMilliseconds;

        return signal.Type switch
        {
            Models.SignalType.CommandCompleted => signal.Error != null
                ? Models.CommandExecutionResult.Warning(
                    Strategy.BuildSuccessMessage(command, hasWarning: true), steps)
                : Models.CommandExecutionResult.Success(
                    Strategy.BuildSuccessMessage(command, hasWarning: false), steps),

            Models.SignalType.CommandFailed => Models.CommandExecutionResult.Failed(
                Strategy.BuildFailureMessage(command, signal.Error),
                steps,
                signal.Error),

            Models.SignalType.Alarm => Models.CommandExecutionResult.Failed(
                $"Command failed due to alarm (failOnAlarm=true): {signal.Error}",
                steps,
                signal.Error),

            _ => Models.CommandExecutionResult.Error("Unknown signal detected", steps)
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
