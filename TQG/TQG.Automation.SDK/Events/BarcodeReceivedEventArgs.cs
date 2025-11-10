namespace TQG.Automation.SDK.Events;

/// <summary>
/// Event arguments for barcode validation requests.
/// Raised when a barcode is read from PLC during INBOUND operation and requires external validation.
/// User must respond with validation result including destination location and other parameters.
/// </summary>
public sealed class BarcodeReceivedEventArgs : EventArgs
{
    /// <summary>
    /// Command ID requiring barcode validation.
    /// </summary>
    public required string TaskId { get; init; }

    /// <summary>
    /// PLC device that read the barcode.
    /// </summary>
    public required string DeviceId { get; init; }

    /// <summary>
    /// Barcode value read from PLC (10 characters).
    /// </summary>
    public required string Barcode { get; init; }

    /// <summary>
    /// When the barcode was read and validation was requested.
    /// </summary>
    public DateTimeOffset RequestedAt { get; init; } = DateTimeOffset.UtcNow;
}
