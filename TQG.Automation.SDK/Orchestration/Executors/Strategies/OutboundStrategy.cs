using TQG.Automation.SDK.Clients;
using TQG.Automation.SDK.Core;
using TQG.Automation.SDK.Shared;
using Models = TQG.Automation.SDK.Orchestration.Models;

namespace TQG.Automation.SDK.Orchestration.Executors.Strategies;

/// <summary>
/// Strategy for OUTBOUND commands: handles outgoing material flow from warehouse.
/// Flow: Write Parameters (Source + Gate + Directions) → Trigger → Start → Wait.
/// </summary>
internal sealed class OutboundStrategy : BaseCommandStrategy
{
    public override CommandType SupportedCommandType => CommandType.Outbound;

    public override string GetTriggerAddress(SignalMap signalMap)
        => signalMap.OutboundTrigger;

    public override string GetCompletionAddress(SignalMap signalMap)
        => signalMap.OutboundCompleted;

    public override bool ValidateCommand(Models.CommandEnvelope command)
    {
        base.ValidateCommand(command);

        if (command.SourceLocation == null)
            throw new ArgumentException("SourceLocation is required for Outbound command.", nameof(command));

        return true;
    }

    public override async Task WriteParametersAsync(
        IPlcClient plcClient,
        SignalMap signalMap,
        Models.CommandEnvelope command,
        List<string> steps,
        CancellationToken cancellationToken)
    {
        // Write source location (Floor, Rail, Block)
        await plcClient.WriteLocationAsync(
            command.SourceLocation!,
            signalMap.SourceFloor,
            signalMap.SourceRail,
            signalMap.SourceBlock,
            steps,
            "source",
            cancellationToken).ConfigureAwait(false);

        // Write gate number
        await plcClient.WriteAsync(
            signalMap.GateNumber,
            (short)command.GateNumber,
            steps,
            $"Wrote gate number: {command.GateNumber}",
            cancellationToken).ConfigureAwait(false);

        // Write exit direction
        await plcClient.WriteDirectionAsync(
            signalMap.ExitDirection,
            command.ExitDirection,
            steps,
            "exit",
            cancellationToken).ConfigureAwait(false);

        // Write enter direction
        await plcClient.WriteDirectionAsync(
            signalMap.EnterDirection,
            command.EnterDirection,
            steps,
            "enter",
            cancellationToken).ConfigureAwait(false);
    }

    public override string BuildSuccessMessage(Models.CommandEnvelope command, bool hasWarning)
    {
        var baseMessage = $"OUTBOUND completed: {command.SourceLocation} → Gate {command.GateNumber}";
        return hasWarning ? $"{baseMessage} (with alarm)" : baseMessage;
    }

    public override string BuildFailureMessage(Models.CommandEnvelope command, ErrorDetail? error)
    {
        return error != null
            ? $"PLC signaled command failure (CommandFailed flag set, alarm: {error})"
            : "PLC signaled command failure (CommandFailed flag set)";
    }
}
