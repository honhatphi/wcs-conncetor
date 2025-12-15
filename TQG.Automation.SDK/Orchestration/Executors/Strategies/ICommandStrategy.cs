using System.Threading.Channels;
using TQG.Automation.SDK.Core;
using TQG.Automation.SDK.Shared;

namespace TQG.Automation.SDK.Orchestration.Executors.Strategies;

/// <summary>
/// Strategy interface for different command types.
/// Each command type (Inbound, Outbound, Transfer) implements this interface
/// to define its specific behavior while sharing common execution flow.
/// </summary>
internal interface ICommandStrategy
{
    /// <summary>
    /// The command type this strategy handles.
    /// </summary>
    CommandType SupportedCommandType { get; }

    /// <summary>
    /// Gets the PLC address for the command trigger signal.
    /// </summary>
    string GetTriggerAddress(SignalMap signalMap);

    /// <summary>
    /// Gets the PLC address for the command completion signal.
    /// </summary>
    string GetCompletionAddress(SignalMap signalMap);

    /// <summary>
    /// Validates the command envelope before execution.
    /// </summary>
    /// <returns>True if valid, throws ArgumentException if invalid</returns>
    bool ValidateCommand(Models.CommandEnvelope command);

    /// <summary>
    /// Writes command-specific parameters to PLC.
    /// </summary>
    Task WriteParametersAsync(
        IPlcClient plcClient,
        SignalMap signalMap,
        Models.CommandEnvelope command,
        List<string> steps,
        CancellationToken cancellationToken);

    /// <summary>
    /// Executes any pre-trigger logic (e.g., for special setup before triggering).
    /// Default implementation does nothing.
    /// </summary>
    Task<Models.CommandExecutionResult?> ExecutePreTriggerAsync(
        IPlcClient plcClient,
        SignalMap signalMap,
        Models.CommandEnvelope command,
        Channel<Models.CommandResult> resultChannel,
        List<string> steps,
        CancellationToken cancellationToken)
    {
        return Task.FromResult<Models.CommandExecutionResult?>(null);
    }

    /// <summary>
    /// Executes any post-trigger logic before waiting for completion (e.g., barcode reading for Inbound).
    /// Default implementation does nothing.
    /// </summary>
    Task<Models.CommandExecutionResult?> ExecutePostTriggerAsync(
        IPlcClient plcClient,
        SignalMap signalMap,
        Models.CommandEnvelope command,
        Channel<Models.CommandResult> resultChannel,
        List<string> steps,
        CancellationToken cancellationToken)
    {
        return Task.FromResult<Models.CommandExecutionResult?>(null);
    }

    /// <summary>
    /// Builds the success message for command completion.
    /// </summary>
    string BuildSuccessMessage(Models.CommandEnvelope command, bool hasWarning);

    /// <summary>
    /// Builds the failure message for command failure.
    /// </summary>
    string BuildFailureMessage(Models.CommandEnvelope command, ErrorDetail? error);
}
