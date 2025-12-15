namespace TQG.Automation.SDK.Shared;

/// <summary>
/// Template for PLC signal addresses without DB prefix.
/// Used with <see cref="SlotConfiguration.DbNumber"/> to generate full <see cref="SignalMap"/> addresses.
/// Example: Address "DBX52.0" with DbNumber 33 generates "DB33.DBX52.0".
/// </summary>
public sealed record SignalMapTemplate
{
    #region Command Signals

    /// <summary>
    /// Inbound command trigger signal (without DB prefix).
    /// Variable: Req_ImportPallet | Type: Bool
    /// </summary>
    public required string InboundTrigger { get; init; }

    /// <summary>
    /// Outbound command trigger signal (without DB prefix).
    /// Variable: Req_ExportPallet | Type: Bool
    /// </summary>
    public required string OutboundTrigger { get; init; }

    /// <summary>
    /// Transfer command trigger signal (without DB prefix).
    /// Variable: Req_TransferPallet | Type: Bool
    /// </summary>
    public required string TransferTrigger { get; init; }

    /// <summary>
    /// Pallet check command signal (without DB prefix).
    /// Variable: Req_CheckPallet | Type: Bool
    /// </summary>
    public required string PalletCheckTrigger { get; init; }

    /// <summary>
    /// Start process command signal (without DB prefix).
    /// Variable: Req_StartProcess | Type: Bool
    /// </summary>
    public required string StartProcess { get; init; }

    #endregion

    #region Status Signals

    /// <summary>
    /// Device ready status (without DB prefix).
    /// Variable: Device_Ready | Type: Bool
    /// </summary>
    public required string DeviceReady { get; init; }

    /// <summary>
    /// Software connection status (without DB prefix).
    /// Variable: Connected_To_Software | Type: Bool
    /// </summary>
    public required string SoftwareConnected { get; init; }

    /// <summary>
    /// Command acknowledged by PLC (without DB prefix).
    /// Variable: Status_ShuttleBusy | Type: Bool
    /// </summary>
    public required string CommandAccepted { get; init; }

    /// <summary>
    /// Command rejected by PLC (without DB prefix).
    /// Variable: Status_InvalidPosition | Type: Bool
    /// </summary>
    public required string CommandRejected { get; init; }

    /// <summary>
    /// Inbound process completion status (without DB prefix).
    /// Variable: Done_ImportProcess | Type: Bool
    /// </summary>
    public required string InboundCompleted { get; init; }

    /// <summary>
    /// Outbound process completion status (without DB prefix).
    /// Variable: Done_ExportProcess | Type: Bool
    /// </summary>
    public required string OutboundCompleted { get; init; }

    /// <summary>
    /// Transfer process completion status (without DB prefix).
    /// Variable: Done_TransferProcess | Type: Bool
    /// </summary>
    public required string TransferCompleted { get; init; }

    /// <summary>
    /// Pallet check completion status (without DB prefix).
    /// Variable: Done_CheckPallet | Type: Bool
    /// </summary>
    public required string PalletCheckCompleted { get; init; }

    /// <summary>
    /// Available pallet status (without DB prefix).
    /// Variable: Status_AvailablePallet | Type: Bool
    /// </summary>
    public required string AvailablePallet { get; init; }

    /// <summary>
    /// Unavailable pallet status (without DB prefix).
    /// Variable: Status_UnavailablePallet | Type: Bool
    /// </summary>
    public required string UnavailablePallet { get; init; }

    /// <summary>
    /// Alarm/error during execution (without DB prefix).
    /// Variable: Error_Running | Type: Bool
    /// </summary>
    public required string ErrorAlarm { get; init; }

    /// <summary>
    /// Command failed status (without DB prefix).
    /// Variable: Status_CommandFailed | Type: Bool
    /// </summary>
    public required string CommandFailed { get; init; }

    #endregion

    #region Direction & Gate Signals

    /// <summary>
    /// Exit direction for Floor 3 operations (without DB prefix).
    /// True: Exit from Top, False: Exit from Bottom
    /// Variable: Dir_Src_Block3 | Type: Bool
    /// </summary>
    public required string ExitDirection { get; init; }

    /// <summary>
    /// Enter direction for Floor 3 operations (without DB prefix).
    /// True: Enter from Top, False: Enter from Bottom
    /// Variable: Dir_Taget_Block3 | Type: Bool
    /// </summary>
    public required string EnterDirection { get; init; }

    /// <summary>
    /// Gate number for I/O operations (without DB prefix).
    /// Variable: Port_IO_Number | Type: Int
    /// </summary>
    public required string GateNumber { get; init; }

    #endregion

    #region Source Position Signals

    /// <summary>
    /// Source floor number (without DB prefix).
    /// Variable: Source_Floor | Type: Int
    /// </summary>
    public required string SourceFloor { get; init; }

    /// <summary>
    /// Source rail number (without DB prefix).
    /// Variable: Source_Rail | Type: Int
    /// </summary>
    public required string SourceRail { get; init; }

    /// <summary>
    /// Source block number (without DB prefix).
    /// Variable: Source_Block | Type: Int
    /// </summary>
    public required string SourceBlock { get; init; }

    /// <summary>
    /// Source depth number (without DB prefix).
    /// Variable: Source_Depth | Type: Int
    /// </summary>
    public required string SourceDepth { get; init; }

    #endregion

    #region Target Position Signals

    /// <summary>
    /// Target floor number (without DB prefix).
    /// Variable: Target_Floor | Type: Int
    /// </summary>
    public required string TargetFloor { get; init; }

    /// <summary>
    /// Target rail number (without DB prefix).
    /// Variable: Target_Rail | Type: Int
    /// </summary>
    public required string TargetRail { get; init; }

    /// <summary>
    /// Target block number (without DB prefix).
    /// Variable: Target_Block | Type: Int
    /// </summary>
    public required string TargetBlock { get; init; }

    #endregion

    #region Feedback Signals

    /// <summary>
    /// Barcode validation success status (without DB prefix).
    /// Variable: Barcode_Valid | Type: Bool
    /// </summary>
    public required string BarcodeValid { get; init; }

    /// <summary>
    /// Barcode validation failure status (without DB prefix).
    /// Variable: Barcode_Invalid | Type: Bool
    /// </summary>
    public required string BarcodeInvalid { get; init; }

    /// <summary>
    /// Current floor position (without DB prefix).
    /// Variable: Cur_Shuttle_Floor | Type: Int
    /// </summary>
    public required string CurrentFloor { get; init; }

    /// <summary>
    /// Current rail position (without DB prefix).
    /// Variable: Cur_Shuttle_Rail | Type: Int
    /// </summary>
    public required string CurrentRail { get; init; }

    /// <summary>
    /// Current block position (without DB prefix).
    /// Variable: Cur_Shuttle_Block | Type: Int
    /// </summary>
    public required string CurrentBlock { get; init; }

    /// <summary>
    /// Current depth position (without DB prefix).
    /// Variable: Cur_Shuttle_Depth | Type: Int
    /// </summary>
    public required string CurrentDepth { get; init; }

    /// <summary>
    /// System error code (without DB prefix).
    /// Variable: System_ErrorCode | Type: Int
    /// </summary>
    public required string ErrorCode { get; init; }

    #endregion

    #region Barcode Characters

    /// <summary>
    /// Barcode character 1 (without DB prefix).
    /// Variable: Barcode | Type: Int
    /// </summary>
    public required string BarcodeChar1 { get; init; }

    /// <summary>
    /// Barcode character 2 (without DB prefix).
    /// Variable: Barcode | Type: Int
    /// </summary>
    public required string BarcodeChar2 { get; init; }

    /// <summary>
    /// Barcode character 3 (without DB prefix).
    /// Variable: Barcode | Type: Int
    /// </summary>
    public required string BarcodeChar3 { get; init; }

    /// <summary>
    /// Barcode character 4 (without DB prefix).
    /// Variable: Barcode | Type: Int
    /// </summary>
    public required string BarcodeChar4 { get; init; }

    /// <summary>
    /// Barcode character 5 (without DB prefix).
    /// Variable: Barcode | Type: Int
    /// </summary>
    public required string BarcodeChar5 { get; init; }

    /// <summary>
    /// Barcode character 6 (without DB prefix).
    /// Variable: Barcode | Type: Int
    /// </summary>
    public required string BarcodeChar6 { get; init; }

    /// <summary>
    /// Barcode character 7 (without DB prefix).
    /// Variable: Barcode | Type: Int
    /// </summary>
    public required string BarcodeChar7 { get; init; }

    /// <summary>
    /// Barcode character 8 (without DB prefix).
    /// Variable: Barcode | Type: Int
    /// </summary>
    public required string BarcodeChar8 { get; init; }

    /// <summary>
    /// Barcode character 9 (without DB prefix).
    /// Variable: Barcode | Type: Int
    /// </summary>
    public required string BarcodeChar9 { get; init; }

    /// <summary>
    /// Barcode character 10 (without DB prefix).
    /// Variable: Barcode | Type: Int
    /// </summary>
    public required string BarcodeChar10 { get; init; }

    #endregion

    /// <summary>
    /// Generates a full <see cref="SignalMap"/> by prefixing all addresses with the specified DB number.
    /// </summary>
    /// <param name="dbNumber">The DB number to prefix (e.g., 33 generates "DB33.").</param>
    /// <returns>A fully-qualified <see cref="SignalMap"/> with DB prefixes.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when dbNumber is not positive.</exception>
    public SignalMap ToSignalMap(int dbNumber)
    {
        if (dbNumber <= 0)
            throw new ArgumentOutOfRangeException(nameof(dbNumber), dbNumber, "DB number must be positive.");

        var prefix = $"DB{dbNumber}.";

        return new SignalMap
        {
            // Command Signals
            InboundTrigger = prefix + InboundTrigger,
            OutboundTrigger = prefix + OutboundTrigger,
            TransferTrigger = prefix + TransferTrigger,
            PalletCheckTrigger = prefix + PalletCheckTrigger,
            StartProcess = prefix + StartProcess,

            // Status Signals
            DeviceReady = prefix + DeviceReady,
            SoftwareConnected = prefix + SoftwareConnected,
            CommandAccepted = prefix + CommandAccepted,
            CommandRejected = prefix + CommandRejected,
            InboundCompleted = prefix + InboundCompleted,
            OutboundCompleted = prefix + OutboundCompleted,
            TransferCompleted = prefix + TransferCompleted,
            PalletCheckCompleted = prefix + PalletCheckCompleted,
            AvailablePallet = prefix + AvailablePallet,
            UnavailablePallet = prefix + UnavailablePallet,
            ErrorAlarm = prefix + ErrorAlarm,
            CommandFailed = prefix + CommandFailed,

            // Direction & Gate Signals
            ExitDirection = prefix + ExitDirection,
            EnterDirection = prefix + EnterDirection,
            GateNumber = prefix + GateNumber,

            // Source Position Signals
            SourceFloor = prefix + SourceFloor,
            SourceRail = prefix + SourceRail,
            SourceBlock = prefix + SourceBlock,
            SourceDepth = prefix + SourceDepth,

            // Target Position Signals
            TargetFloor = prefix + TargetFloor,
            TargetRail = prefix + TargetRail,
            TargetBlock = prefix + TargetBlock,

            // Feedback Signals
            BarcodeValid = prefix + BarcodeValid,
            BarcodeInvalid = prefix + BarcodeInvalid,
            CurrentFloor = prefix + CurrentFloor,
            CurrentRail = prefix + CurrentRail,
            CurrentBlock = prefix + CurrentBlock,
            CurrentDepth = prefix + CurrentDepth,
            ErrorCode = prefix + ErrorCode,

            // Barcode Characters
            BarcodeChar1 = prefix + BarcodeChar1,
            BarcodeChar2 = prefix + BarcodeChar2,
            BarcodeChar3 = prefix + BarcodeChar3,
            BarcodeChar4 = prefix + BarcodeChar4,
            BarcodeChar5 = prefix + BarcodeChar5,
            BarcodeChar6 = prefix + BarcodeChar6,
            BarcodeChar7 = prefix + BarcodeChar7,
            BarcodeChar8 = prefix + BarcodeChar8,
            BarcodeChar9 = prefix + BarcodeChar9,
            BarcodeChar10 = prefix + BarcodeChar10
        };
    }

    /// <summary>
    /// Validates all signal addresses are non-empty and properly formatted.
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

        // Validate format: should start with DB (like DBX, DBW, DBD, DBB)
        if (!address.StartsWith("DB", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(
                $"Signal address '{propertyName}' must start with 'DB' (e.g., 'DBX52.0', 'DBW50'). Current: '{address}'",
                propertyName);
    }
}
