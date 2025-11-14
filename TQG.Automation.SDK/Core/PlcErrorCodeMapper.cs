namespace TQG.Automation.SDK.Core;

/// <summary>
/// Maps PLC error codes to human-readable error messages.
/// </summary>
public static class PlcErrorCodeMapper
{
    /// <summary>
    /// Mapping of error codes to descriptive messages.
    /// </summary>
    public static readonly IReadOnlyDictionary<int, string> Messages =
        new Dictionary<int, string>
        {
            [1] = "Shuttle Lift Time Over",
            [2] = "Speed not set",
            [3] = "Shuttle Stop Time Over",
            [4] = "Shuttle Not Matching Block",
            [5] = "Encountered an obstacle while changing lanes",
            [6] = "Floor mismatch",
            [7] = "Target location does not match",
            [8] = "Shuttle not in Elevator",
            [9] = "Shuttle lost connection",
            [10] = "Pallet input location is full",
            [11] = "RFID reader connection lost",
            [12] = "Pallet not detected at pick location",
            [13] = "Elevator Stop Time Over",
            [14] = "Elevator coordinate limit exceeded",
            [15] = "Warning: Pallet not meeting requirements",
            [16] = "Conveyor Lift Motor Error",
            [17] = "Gate No. 1 opening/closing time over",
            [18] = "Gate No. 2 opening/closing time over",
            [19] = "No Pallet Detected on Elevator",
            [20] = "Invalid input/output location",
            [21] = "Can't control Shuttle",
            [22] = "Check position: Shuttle is not in correct position",
            [23] = "Shuttle not on Elevator",
            [24] = "Manual mode: required floor must be current floor",
            [25] = "Inverter Error",
            [26] = "Elevator reaches travel limit",
            [27] = "QR code reader timeout",
            [28] = "Timeout stop Conveyor 6 & 4 or 8 & 7",
            [29] = "QR Code read error",
            [30] = "Inbound in progress: cannot send outbound command",
            [31] = "Outbound in progress: cannot send inbound command",
            [100] = "Emergency stop",
            [101] = "Shuttle Servo_1 Alarm",
            [102] = "Shuttle Servo_2 Alarm",
        };

    /// <summary>
    /// Gets the error message for a given error code.
    /// </summary>
    /// <param name="errorCode">The error code from PLC.</param>
    /// <returns>The descriptive error message, or a default message if code is unknown.</returns>
    public static string GetMessage(int errorCode)
    {
        return Messages.TryGetValue(errorCode, out var message)
            ? message
            : $"Unknown error code: {errorCode}";
    }

    /// <summary>
    /// Checks if an error code exists in the mapping.
    /// </summary>
    public static bool IsKnownErrorCode(int errorCode) => Messages.ContainsKey(errorCode);
}
