namespace TQG.Automation.SDK.Shared;

/// <summary>
/// Response model for barcode validation.
/// User must call RespondToBarcodeValidation() within timeout period (5 minutes).
/// If IsValid = true, must provide DestinationLocation and GateNumber.
/// </summary>
public sealed class BarcodeValidationResponse
{
    /// <summary>
    /// Command ID being validated.
    /// </summary>
    public required string CommandId { get; init; }

    /// <summary>
    /// Whether the barcode is valid and command should proceed.
    /// </summary>
    public required bool IsValid { get; init; }

    /// <summary>
    /// Destination location for the inbound material (required if IsValid = true).
    /// </summary>
    public Location? DestinationLocation { get; init; }

    /// <summary>
    /// Gate number for the inbound operation (required if IsValid = true).
    /// </summary>
    public int? GateNumber { get; init; }

    /// <summary>
    /// Enter direction for material flow (optional).
    /// </summary>
    public Direction? EnterDirection { get; init; }

    /// <summary>
    /// When the validation response was provided.
    /// </summary>
    public DateTimeOffset RespondedAt { get; init; } = DateTimeOffset.UtcNow;
}
