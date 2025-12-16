using System.Threading.Channels;
using TQG.Automation.SDK.Clients;
using TQG.Automation.SDK.Core;
using TQG.Automation.SDK.Events;
using TQG.Automation.SDK.Shared;

namespace TQG.Automation.SDK.Orchestration.Executors.Strategies;

/// <summary>
/// Strategy for INBOUND command execution.
/// Flow: Trigger → Start Process → Read Barcode → Validate → Write Parameters → Wait for completion.
/// The barcode validation logic is handled via callback in ExecutePostTriggerAsync.
/// </summary>
internal sealed class InboundStrategy : BaseCommandStrategy
{
    private readonly Func<BarcodeReceivedEventArgs, CancellationToken, Task<BarcodeValidationResponse>> _barcodeValidationCallback;

    // Store validation response for use in WriteParametersAsync
    private BarcodeValidationResponse? _lastValidationResponse;

    public InboundStrategy(
        Func<BarcodeReceivedEventArgs, CancellationToken, Task<BarcodeValidationResponse>> barcodeValidationCallback)
    {
        _barcodeValidationCallback = barcodeValidationCallback ?? throw new ArgumentNullException(nameof(barcodeValidationCallback));
    }

    /// <inheritdoc />
    public override CommandType SupportedCommandType => CommandType.Inbound;

    /// <inheritdoc />
    public override string GetTriggerAddress(SignalMap signalMap) => signalMap.InboundTrigger;

    /// <inheritdoc />
    public override string GetCompletionAddress(SignalMap signalMap) => signalMap.InboundCompleted;

    /// <inheritdoc />
    public override async Task WriteParametersAsync(
        IPlcClient plcClient,
        SignalMap signalMap,
        Models.CommandEnvelope command,
        List<string> steps,
        CancellationToken cancellationToken)
    {
        // For INBOUND, parameters are written after barcode validation (in ExecutePostTriggerAsync)
        // This method is called before trigger, but INBOUND writes parameters after validation
        // So this is intentionally empty - actual parameter writing happens in WriteBarcodeParametersAsync
        await Task.CompletedTask;
    }

    /// <summary>
    /// Executes post-trigger logic: Read barcode, validate, write parameters.
    /// </summary>
    public override async Task<Models.CommandExecutionResult?> ExecutePostTriggerAsync(
        IPlcClient plcClient,
        SignalMap signalMap,
        Models.CommandEnvelope command,
        Channel<Models.CommandResult> resultChannel,
        List<string> steps,
        CancellationToken cancellationToken)
    {
        // Step 1: Read barcode from PLC
        var barcode = await ReadBarcodeAsync(plcClient, signalMap, command, steps, cancellationToken)
            .ConfigureAwait(false);

        // Step 2: Validate barcode with user
        var validationResponse = await ValidateBarcodeAsync(command, barcode, steps, cancellationToken)
            .ConfigureAwait(false);

        // Step 3: Write validation result flags to PLC
        if (!validationResponse.IsValid)
        {
            await WriteBarcodeValidationFlagsAsync(plcClient, signalMap, isValid: false, steps, cancellationToken)
                .ConfigureAwait(false);
            
            // Don't return Failed here - let PLC decide the final result
            // PLC may allow operator to continue via HMI, or it may fail the command
            // Signal monitor will detect CommandCompleted or CommandFailed
            return null;
        }

        await WriteBarcodeValidationFlagsAsync(plcClient, signalMap, isValid: true, steps, cancellationToken)
            .ConfigureAwait(false);

        // Step 4: Write command parameters (from validation response)
        await WriteBarcodeParametersAsync(
            plcClient, signalMap, validationResponse, steps, cancellationToken)
            .ConfigureAwait(false);

        // Store for success message building
        _lastValidationResponse = validationResponse;

        // Continue execution (no early return)
        return null;
    }

    /// <inheritdoc />
    public override string BuildSuccessMessage(Models.CommandEnvelope command, bool hasWarning)
    {
        if (_lastValidationResponse != null)
        {
            var gate = _lastValidationResponse.GateNumber;
            var dest = _lastValidationResponse.DestinationLocation;
            return hasWarning
                ? $"INBOUND completed with alarm: Gate {gate} → {dest}"
                : $"INBOUND completed: Gate {gate} → {dest}";
        }

        return hasWarning
            ? "INBOUND completed with alarm"
            : "INBOUND completed";
    }

    #region Barcode Operations

    /// <summary>
    /// Reads barcode from PLC by polling 10 character registers.
    /// </summary>
    private static async Task<string> ReadBarcodeAsync(
        IPlcClient plcClient,
        SignalMap signalMap,
        Models.CommandEnvelope command,
        List<string> steps,
        CancellationToken cancellationToken)
    {
        var pollInterval = TimeSpan.FromMilliseconds(500);
        var startTime = DateTimeOffset.UtcNow;
        var defaultBarcode = "0000000000";

        steps.Add("Waiting for barcode...");

        while (!cancellationToken.IsCancellationRequested)
        {
            var barcodeChars = new List<string>(10);

            var addresses = new[]
            {
                signalMap.BarcodeChar1, signalMap.BarcodeChar2, signalMap.BarcodeChar3,
                signalMap.BarcodeChar4, signalMap.BarcodeChar5, signalMap.BarcodeChar6,
                signalMap.BarcodeChar7, signalMap.BarcodeChar8, signalMap.BarcodeChar9,
                signalMap.BarcodeChar10
            };

            for (int i = 0; i < addresses.Length; i++)
            {
                var charAddress = PlcAddress.Parse(addresses[i]);
                var charValue = await plcClient.ReadAsync<string>(charAddress, cancellationToken)
                    .ConfigureAwait(false);

                if (charValue.Length != 1)
                {
                    steps.Add($"Stopped at position {i + 1}: character has length {charValue.Length} ('{charValue}'). Using {i} characters collected.");
                    break;
                }

                barcodeChars.Add(charValue);
            }

            var barcode = string.Join("", barcodeChars);

            if (barcode != defaultBarcode && !string.IsNullOrEmpty(barcode))
            {
                var elapsed = (DateTimeOffset.UtcNow - startTime).TotalMilliseconds;
                steps.Add($"Barcode detected after {elapsed:F0}ms: '{barcode}'");
                return barcode;
            }

            await Task.Delay(pollInterval, cancellationToken).ConfigureAwait(false);
        }

        throw new OperationCanceledException("Barcode reading was cancelled", cancellationToken);
    }

    /// <summary>
    /// Validates barcode with user via callback.
    /// </summary>
    private async Task<BarcodeValidationResponse> ValidateBarcodeAsync(
        Models.CommandEnvelope command,
        string barcode,
        List<string> steps,
        CancellationToken cancellationToken)
    {
        if (barcode == "0000000000")
        {
            steps.Add("No valid barcode detected");
            return new BarcodeValidationResponse
            {
                CommandId = command.CommandId,
                IsValid = false
            };
        }

        steps.Add($"Barcode read: {barcode}");

        var validationRequest = new BarcodeReceivedEventArgs
        {
            TaskId = command.CommandId,
            DeviceId = command.PlcDeviceId ?? "Unknown",
            Barcode = barcode
        };

        var validationResponse = await _barcodeValidationCallback(validationRequest, cancellationToken)
            .ConfigureAwait(false);

        if (!validationResponse.IsValid)
        {
            steps.Add("Barcode validation rejected.");
            return validationResponse;
        }

        if (validationResponse.DestinationLocation == null)
        {
            steps.Add("Validation response missing DestinationLocation");
            return new BarcodeValidationResponse
            {
                CommandId = command.CommandId,
                IsValid = false
            };
        }

        if (!validationResponse.GateNumber.HasValue || validationResponse.GateNumber <= 0)
        {
            steps.Add("Validation response missing or invalid GateNumber");
            return new BarcodeValidationResponse
            {
                CommandId = command.CommandId,
                IsValid = false
            };
        }

        steps.Add($"Barcode validation approved - Destination: {validationResponse.DestinationLocation}, Gate: {validationResponse.GateNumber}");
        return validationResponse;
    }

    /// <summary>
    /// Writes barcode validation result flags to PLC.
    /// </summary>
    private static async Task WriteBarcodeValidationFlagsAsync(
        IPlcClient plcClient,
        SignalMap signalMap,
        bool isValid,
        List<string> steps,
        CancellationToken cancellationToken)
    {
        if (isValid)
        {
            await plcClient.WriteAsync(
                signalMap.BarcodeValid,
                true,
                steps,
                "Wrote BarcodeValid flag (true)",
                cancellationToken).ConfigureAwait(false);

            await plcClient.WriteAsync(
                signalMap.BarcodeInvalid,
                false,
                steps,
                "Wrote BarcodeInvalid flag (false)",
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await plcClient.WriteAsync(
                signalMap.BarcodeInvalid,
                true,
                steps,
                "Wrote BarcodeInvalid flag (true)",
                cancellationToken).ConfigureAwait(false);

            await plcClient.WriteAsync(
                signalMap.BarcodeValid,
                false,
                steps,
                "Wrote BarcodeValid flag (false)",
                cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Writes command parameters to PLC (destination location, gate, directions).
    /// </summary>
    private static async Task WriteBarcodeParametersAsync(
        IPlcClient plcClient,
        SignalMap signalMap,
        BarcodeValidationResponse validationResponse,
        List<string> steps,
        CancellationToken cancellationToken)
    {
        // Write destination location
        await plcClient.WriteLocationAsync(
            validationResponse.DestinationLocation!,
            signalMap.TargetFloor,
            signalMap.TargetRail,
            signalMap.TargetBlock,
            steps,
            "destination",
            cancellationToken).ConfigureAwait(false);

        // Write gate number
        await plcClient.WriteAsync(
            signalMap.GateNumber,
            (short)validationResponse.GateNumber!.Value,
            steps,
            $"Wrote gate number: {validationResponse.GateNumber}",
            cancellationToken).ConfigureAwait(false);

        // Write enter direction
        await plcClient.WriteDirectionAsync(
            signalMap.EnterDirection,
            validationResponse.EnterDirection,
            steps,
            "enter",
            cancellationToken).ConfigureAwait(false);
    }

    #endregion
}
