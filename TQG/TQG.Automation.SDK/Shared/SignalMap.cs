namespace TQG.Automation.SDK.Shared;

/// <summary>
/// Maps PLC signal addresses to logical operations.
/// Contains all register addresses for command execution, status monitoring, and feedback.
/// </summary>
public sealed record SignalMap
{
    #region Command Signals

    /// <summary>
    /// Inbound command trigger signal.
    /// Variable: Req_ImportPallet | Type: Bool
    /// </summary>
    public required string InboundTrigger { get; init; }

    /// <summary>
    /// Outbound command trigger signal.
    /// Variable: Req_ExportPallet | Type: Bool
    /// </summary>
    public required string OutboundTrigger { get; init; }

    /// <summary>
    /// Transfer command trigger signal.
    /// Variable: Req_TransferPallet | Type: Bool
    /// </summary>
    public required string TransferTrigger { get; init; }

    /// <summary>
    /// Pallet check command signal.
    /// Variable: Req_CheckPallet | Type: Bool
    /// </summary>
    public required string PalletCheckTrigger { get; init; }

    /// <summary>
    /// Start process command signal.
    /// Variable: Req_StartProcess | Type: Bool
    /// </summary>
    public required string StartProcess { get; init; }

    #endregion

    #region Status Signals

    /// <summary>
    /// Device ready status.
    /// Variable: Device_Ready | Type: Bool
    /// </summary>
    public required string DeviceReady { get; init; }

    /// <summary>
    /// Software connection status.
    /// Variable: Connected_To_Software | Type: Bool
    /// </summary>
    public required string SoftwareConnected { get; init; }

    /// <summary>
    /// Command acknowledged by PLC.
    /// Variable: Status_ShuttleBusy | Type: Bool
    /// </summary>
    public required string CommandAccepted { get; init; }

    /// <summary>
    /// Command rejected by PLC.
    /// Variable: Status_InvalidPosition | Type: Bool
    /// </summary>
    public required string CommandRejected { get; init; }

    /// <summary>
    /// Inbound process completion status.
    /// Variable: Done_ImportProcess | Type: Bool
    /// </summary>
    public required string InboundCompleted { get; init; }

    /// <summary>
    /// Outbound process completion status.
    /// Variable: Done_ExportProcess | Type: Bool
    /// </summary>
    public required string OutboundCompleted { get; init; }

    /// <summary>
    /// Transfer process completion status.
    /// Variable: Done_TransferProcess | Type: Bool
    /// </summary>
    public required string TransferCompleted { get; init; }

    /// <summary>
    /// Pallet check completion status.
    /// Variable: Done_CheckPallet | Type: Bool
    /// </summary>
    public required string PalletCheckCompleted { get; init; }

    /// <summary>
    /// Available pallet status.
    /// Variable: Status_AvailablePallet | Type: Bool
    /// </summary>
    public required string AvailablePallet { get; init; }

    /// <summary>
    /// Unavailable pallet status.
    /// Variable: Status_UnavailablePallet | Type: Bool
    /// </summary>
    public required string UnavailablePallet { get; init; }

    /// <summary>
    /// Alarm/error during execution.
    /// Variable: Error_Running | Type: Bool
    /// </summary>
    public required string ErrorAlarm { get; init; }

    /// <summary>
    /// Command failed status.
    /// Variable: Status_CommandFailed | Type: Bool
    /// </summary>
    public required string CommandFailed { get; init; }

    #endregion

    #region Direction & Gate Signals

    /// <summary>
    /// Exit direction for Floor 3 operations.
    /// True: Exit from Top, False: Exit from Bottom
    /// Variable: Dir_Src_Block3 | Type: Bool
    /// </summary>
    public required string ExitDirection { get; init; }

    /// <summary>
    /// Enter direction for Floor 3 operations.
    /// True: Enter from Top, False: Enter from Bottom
    /// Variable: Dir_Taget_Block3 | Type: Bool
    /// </summary>
    public required string EnterDirection { get; init; }

    /// <summary>
    /// Gate number for I/O operations.
    /// Variable: Port_IO_Number | Type: Int
    /// </summary>
    public required string GateNumber { get; init; }

    #endregion

    #region Source Position Signals

    /// <summary>
    /// Source floor number.
    /// Variable: Source_Floor | Type: Int
    /// </summary>
    public required string SourceFloor { get; init; }

    /// <summary>
    /// Source rail number.
    /// Variable: Source_Rail | Type: Int
    /// </summary>
    public required string SourceRail { get; init; }

    /// <summary>
    /// Source block number.
    /// Variable: Source_Block | Type: Int
    /// </summary>
    public required string SourceBlock { get; init; }

    /// <summary>
    /// Source depth number.
    /// Variable: Source_Depth | Type: Int
    /// </summary>
    public required string SourceDepth { get; init; }

    #endregion

    #region Target Position Signals

    /// <summary>
    /// Target floor number.
    /// Variable: Target_Floor | Type: Int
    /// </summary>
    public required string TargetFloor { get; init; }

    /// <summary>
    /// Target rail number.
    /// Variable: Target_Rail | Type: Int
    /// </summary>
    public required string TargetRail { get; init; }

    /// <summary>
    /// Target block number.
    /// Variable: Target_Block | Type: Int
    /// </summary>
    public required string TargetBlock { get; init; }

    #endregion

    #region Feedback Signals

    /// <summary>
    /// Barcode validation success status.
    /// Variable: Barcode_Valid | Type: Bool
    /// </summary>
    public required string BarcodeValid { get; init; }

    /// <summary>
    /// Barcode validation failure status.
    /// Variable: Barcode_Invalid | Type: Bool
    /// </summary>
    public required string BarcodeInvalid { get; init; }

    /// <summary>
    /// Current floor position.
    /// Variable: Cur_Shuttle_Floor | Type: Int
    /// </summary>
    public required string CurrentFloor { get; init; }

    /// <summary>
    /// Current rail position.
    /// Variable: Cur_Shuttle_Rail | Type: Int
    /// </summary>
    public required string CurrentRail { get; init; }

    /// <summary>
    /// Current block position.
    /// Variable: Cur_Shuttle_Block | Type: Int
    /// </summary>
    public required string CurrentBlock { get; init; }

    /// <summary>
    /// Current depth position.
    /// Variable: Cur_Shuttle_Depth | Type: Int
    /// </summary>
    public required string CurrentDepth { get; init; }

    /// <summary>
    /// System error code.
    /// Variable: System_ErrorCode | Type: Int
    /// </summary>
    public required string ErrorCode { get; init; }

    #endregion

    #region Barcode Characters

    /// <summary>
    /// Barcode character 1.
    /// Variable: Barcode | Type: Int
    /// </summary>
    public required string BarcodeChar1 { get; init; }

    /// <summary>
    /// Barcode character 2.
    /// Variable: Barcode | Type: Int
    /// </summary>
    public required string BarcodeChar2 { get; init; }

    /// <summary>
    /// Barcode character 3.
    /// Variable: Barcode | Type: Int
    /// </summary>
    public required string BarcodeChar3 { get; init; }

    /// <summary>
    /// Barcode character 4.
    /// Variable: Barcode | Type: Int
    /// </summary>
    public required string BarcodeChar4 { get; init; }

    /// <summary>
    /// Barcode character 5.
    /// Variable: Barcode | Type: Int
    /// </summary>
    public required string BarcodeChar5 { get; init; }

    /// <summary>
    /// Barcode character 6.
    /// Variable: Barcode | Type: Int
    /// </summary>
    public required string BarcodeChar6 { get; init; }

    /// <summary>
    /// Barcode character 7.
    /// Variable: Barcode | Type: Int
    /// </summary>
    public required string BarcodeChar7 { get; init; }

    /// <summary>
    /// Barcode character 8.
    /// Variable: Barcode | Type: Int
    /// </summary>
    public required string BarcodeChar8 { get; init; }

    /// <summary>
    /// Barcode character 9.
    /// Variable: Barcode | Type: Int
    /// </summary>
    public required string BarcodeChar9 { get; init; }

    /// <summary>
    /// Barcode character 10.
    /// Variable: Barcode | Type: Int
    /// </summary>
    public required string BarcodeChar10 { get; init; }

    #endregion

    /// <summary>
    /// Validates all signal addresses are non-empty.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when any signal address is null or empty.</exception>
    public void Validate()
    {
        ValidateSignal(InboundTrigger, nameof(InboundTrigger));
        ValidateSignal(OutboundTrigger, nameof(OutboundTrigger));
        ValidateSignal(TransferTrigger, nameof(TransferTrigger));
        ValidateSignal(PalletCheckTrigger, nameof(PalletCheckTrigger));
        ValidateSignal(StartProcess, nameof(StartProcess));

        ValidateSignal(DeviceReady, nameof(DeviceReady));
        ValidateSignal(SoftwareConnected, nameof(SoftwareConnected));
        ValidateSignal(CommandAccepted, nameof(CommandAccepted));
        ValidateSignal(CommandRejected, nameof(CommandRejected));
        ValidateSignal(InboundCompleted, nameof(InboundCompleted));
        ValidateSignal(OutboundCompleted, nameof(OutboundCompleted));
        ValidateSignal(TransferCompleted, nameof(TransferCompleted));
        ValidateSignal(PalletCheckCompleted, nameof(PalletCheckCompleted));
        ValidateSignal(AvailablePallet, nameof(AvailablePallet));
        ValidateSignal(UnavailablePallet, nameof(UnavailablePallet));
        ValidateSignal(ErrorAlarm, nameof(ErrorAlarm));
        ValidateSignal(CommandFailed, nameof(CommandFailed));

        ValidateSignal(ExitDirection, nameof(ExitDirection));
        ValidateSignal(EnterDirection, nameof(EnterDirection));
        ValidateSignal(GateNumber, nameof(GateNumber));

        ValidateSignal(SourceFloor, nameof(SourceFloor));
        ValidateSignal(SourceRail, nameof(SourceRail));
        ValidateSignal(SourceBlock, nameof(SourceBlock));
        ValidateSignal(SourceDepth, nameof(SourceDepth));

        ValidateSignal(TargetFloor, nameof(TargetFloor));
        ValidateSignal(TargetRail, nameof(TargetRail));
        ValidateSignal(TargetBlock, nameof(TargetBlock));

        ValidateSignal(BarcodeValid, nameof(BarcodeValid));
        ValidateSignal(BarcodeInvalid, nameof(BarcodeInvalid));
        ValidateSignal(CurrentFloor, nameof(CurrentFloor));
        ValidateSignal(CurrentRail, nameof(CurrentRail));
        ValidateSignal(CurrentBlock, nameof(CurrentBlock));
        ValidateSignal(CurrentDepth, nameof(CurrentDepth));
        ValidateSignal(ErrorCode, nameof(ErrorCode));

        ValidateSignal(BarcodeChar1, nameof(BarcodeChar1));
        ValidateSignal(BarcodeChar2, nameof(BarcodeChar2));
        ValidateSignal(BarcodeChar3, nameof(BarcodeChar3));
        ValidateSignal(BarcodeChar4, nameof(BarcodeChar4));
        ValidateSignal(BarcodeChar5, nameof(BarcodeChar5));
        ValidateSignal(BarcodeChar6, nameof(BarcodeChar6));
        ValidateSignal(BarcodeChar7, nameof(BarcodeChar7));
        ValidateSignal(BarcodeChar8, nameof(BarcodeChar8));
        ValidateSignal(BarcodeChar9, nameof(BarcodeChar9));
        ValidateSignal(BarcodeChar10, nameof(BarcodeChar10));
    }

    private static void ValidateSignal(string address, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(address))
            throw new ArgumentException($"Signal address '{propertyName}' cannot be null or empty.", propertyName);
    }
}
