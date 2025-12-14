using System.Threading.Channels;
using TQG.Automation.SDK.Core;
using TQG.Automation.SDK.Shared;
using Models = TQG.Automation.SDK.Orchestration.Models;

namespace TQG.Automation.SDK.Orchestration.Executors.Strategies;

/// <summary>
/// Base class providing default implementations for ICommandStrategy.
/// Concrete strategies should inherit from this class and override abstract members.
/// </summary>
internal abstract class BaseCommandStrategy : ICommandStrategy
{
    /// <inheritdoc />
    public abstract CommandType SupportedCommandType { get; }

    /// <inheritdoc />
    public abstract string GetTriggerAddress(SignalMap signalMap);

    /// <inheritdoc />
    public abstract string GetCompletionAddress(SignalMap signalMap);

    /// <inheritdoc />
    public virtual bool ValidateCommand(Models.CommandEnvelope command)
    {
        if (command.CommandType != SupportedCommandType)
            throw new ArgumentException(
                $"Invalid command type: {command.CommandType}. Expected {SupportedCommandType}.",
                nameof(command));
        return true;
    }

    /// <inheritdoc />
    public abstract Task WriteParametersAsync(
        IPlcClient plcClient,
        SignalMap signalMap,
        Models.CommandEnvelope command,
        List<string> steps,
        CancellationToken cancellationToken);

    /// <inheritdoc />
    public virtual Task<Models.CommandExecutionResult?> ExecutePreTriggerAsync(
        IPlcClient plcClient,
        SignalMap signalMap,
        Models.CommandEnvelope command,
        Channel<Models.CommandResult> resultChannel,
        List<string> steps,
        CancellationToken cancellationToken)
    {
        return Task.FromResult<Models.CommandExecutionResult?>(null);
    }

    /// <inheritdoc />
    public virtual Task<Models.CommandExecutionResult?> ExecutePostTriggerAsync(
        IPlcClient plcClient,
        SignalMap signalMap,
        Models.CommandEnvelope command,
        Channel<Models.CommandResult> resultChannel,
        List<string> steps,
        CancellationToken cancellationToken)
    {
        return Task.FromResult<Models.CommandExecutionResult?>(null);
    }

    /// <inheritdoc />
    public abstract string BuildSuccessMessage(Models.CommandEnvelope command, bool hasWarning);

    /// <inheritdoc />
    public virtual string BuildFailureMessage(Models.CommandEnvelope command, ErrorDetail? error)
    {
        return error != null
            ? $"PLC signaled command failure (CommandFailed flag set, alarm: {error})"
            : "PLC signaled command failure (CommandFailed flag set)";
    }
}
