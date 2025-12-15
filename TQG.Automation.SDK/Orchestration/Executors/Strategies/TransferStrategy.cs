using TQG.Automation.SDK.Clients;
using TQG.Automation.SDK.Core;
using TQG.Automation.SDK.Shared;

namespace TQG.Automation.SDK.Orchestration.Executors.Strategies;

/// <summary>
/// Strategy for TRANSFER commands: handles internal material movement within warehouse.
/// Flow: Write Parameters (Source + Target + Directions) → Trigger → Start → Wait.
/// </summary>
internal sealed class TransferStrategy : BaseCommandStrategy
{
    public override CommandType SupportedCommandType => CommandType.Transfer;

    public override string GetTriggerAddress(SignalMap signalMap)
        => signalMap.TransferTrigger;

    public override string GetCompletionAddress(SignalMap signalMap)
        => signalMap.TransferCompleted;

    public override bool ValidateCommand(Models.CommandEnvelope command)
    {
        base.ValidateCommand(command);

        if (command.SourceLocation == null)
            throw new ArgumentException("SourceLocation is required for Transfer command.", nameof(command));

        if (command.DestinationLocation == null)
            throw new ArgumentException("DestinationLocation is required for Transfer command.", nameof(command));

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

        // Write target location (Floor, Rail, Block)
        await plcClient.WriteLocationAsync(
            command.DestinationLocation!,
            signalMap.TargetFloor,
            signalMap.TargetRail,
            signalMap.TargetBlock,
            steps,
            "target",
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
        var baseMessage = $"TRANSFER completed: {command.SourceLocation} → {command.DestinationLocation}";
        return hasWarning ? $"{baseMessage} (with alarm)" : baseMessage;
    }
}
