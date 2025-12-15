using System.Threading.Channels;

namespace TQG.Automation.SDK.Orchestration.Models;

/// <summary>
/// Context information for signal monitoring during command execution.
/// </summary>
internal sealed class SignalMonitorContext
{
    /// <summary>
    /// The unique identifier of the command being executed.
    /// </summary>
    public required string CommandId { get; init; }

    /// <summary>
    /// The PLC device identifier executing the command.
    /// </summary>
    public required string PlcDeviceId { get; init; }

    /// <summary>
    /// The slot identifier where command is being executed.
    /// </summary>
    public required int SlotId { get; init; }

    /// <summary>
    /// The PLC address to monitor for command completion signal.
    /// </summary>
    public required string CompletionSignalAddress { get; init; }

    /// <summary>
    /// Channel to send alarm notifications to.
    /// </summary>
    public required Channel<CommandResult> ResultChannel { get; init; }

    /// <summary>
    /// List to record execution steps for logging/debugging.
    /// </summary>
    public required List<string> Steps { get; init; }

    /// <summary>
    /// When command execution started.
    /// </summary>
    public required DateTimeOffset StartTime { get; init; }

    /// <summary>
    /// Whether to fail immediately when an alarm is detected.
    /// </summary>
    public required bool FailOnAlarm { get; init; }
}
