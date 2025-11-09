namespace TQG.Automation.SDK.Models;

/// <summary>
/// Response from user for barcode validation.
/// User must call RespondToBarcodeValidation() within timeout period (2 minutes).
/// If IsValid = true, must provide DestinationLocation and other parameters for INBOUND execution.
/// </summary>
public sealed record BarcodeValidationResponse
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
    /// Exit direction for material flow (optional).
    /// </summary>
    public Direction? ExitDirection { get; init; }

    /// <summary>
    /// Optional reason for rejection (if IsValid = false).
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// When the validation response was provided.
    /// </summary>
    public DateTimeOffset RespondedAt { get; init; } = DateTimeOffset.UtcNow;
}
