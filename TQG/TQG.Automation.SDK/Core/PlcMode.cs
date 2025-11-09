namespace TQG.Automation.SDK.Core;

/// <summary>
/// Defines the connection mode for PLC communication.
/// </summary>
public enum PlcMode
{
    /// <summary>
    /// Real hardware connection using S7.NET Plus.
    /// </summary>
    Real,

    /// <summary>
    /// Emulated TCP connection for testing and development.
    /// </summary>
    Emulated
}
