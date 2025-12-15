namespace TQG.Automation.SDK.Orchestration.Models;

/// <summary>
/// Ticket published by worker when device becomes available.
/// Consumed by Matchmaker for scheduling decisions.
/// </summary>
internal sealed record ReadyTicket
{
    /// <summary>
    /// Device that is ready for work.
    /// </summary>
    public required string PlcDeviceId { get; init; }

    /// <summary>
    /// Slot identifier that is signaling availability.
    /// </summary>
    public required int SlotId { get; init; }

    /// <summary>
    /// When device became ready.
    /// </summary>
    public DateTimeOffset ReadyAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Worker instance that published this ticket.
    /// </summary>
    public int WorkerInstance { get; init; }

    /// <summary>
    /// Optional: hint about device load or queue depth.
    /// </summary>
    public int CurrentQueueDepth { get; init; }
}
