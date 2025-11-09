namespace TQG.Automation.SDK.Models;

/// <summary>
/// Type of warehouse command operation.
/// </summary>
public enum CommandType
{
    /// <summary>
    /// Inbound operation: Material entering warehouse from external source.
    /// </summary>
    Inbound,

    /// <summary>
    /// Outbound operation: Material leaving warehouse to external destination.
    /// </summary>
    Outbound,

    /// <summary>
    /// Transfer operation: Material moving between internal warehouse positions.
    /// </summary>
    Transfer,

    /// <summary>
    /// Check pallet operation: Verify pallet existence at specific location.
    /// </summary>
    CheckPallet
}
