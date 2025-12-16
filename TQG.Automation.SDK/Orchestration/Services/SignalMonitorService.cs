using TQG.Automation.SDK.Core;
using TQG.Automation.SDK.Logging;
using TQG.Automation.SDK.Orchestration.Models;
using TQG.Automation.SDK.Shared;

namespace TQG.Automation.SDK.Orchestration.Services;

/// <summary>
/// Service for monitoring PLC signals during command execution.
/// Runs as a background task and detects: Alarm, CommandFailed, CommandCompleted.
/// Can be reused across all executor types.
/// </summary>
internal sealed class SignalMonitorService
{
    private readonly IPlcClient _plcClient;
    private readonly SignalMap _signalMap;
    private ILogger? _logger;

    // State for current monitoring session (reset on each MonitorSignalsAsync call)
    private bool _alarmDetected;
    private bool _alarmNotificationSent;
    private ErrorDetail? _detectedError;

    public SignalMonitorService(IPlcClient plcClient, SignalMap signalMap)
    {
        _plcClient = plcClient ?? throw new ArgumentNullException(nameof(plcClient));
        _signalMap = signalMap ?? throw new ArgumentNullException(nameof(signalMap));
    }

    /// <summary>
    /// Sets the logger for this service.
    /// </summary>
    public void SetLogger(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Monitors PLC signals continuously until a terminating signal is detected or cancellation is requested.
    /// Terminating signals: Alarm (if failOnAlarm), CommandFailed, CommandCompleted.
    /// </summary>
    /// <param name="context">Context containing command info and monitoring configuration.</param>
    /// <param name="signalCts">CancellationTokenSource to cancel execution when signal is detected.</param>
    /// <param name="externalCancellation">External cancellation token (e.g., timeout, user cancel).</param>
    /// <returns>The signal that was detected, or None if cancelled externally.</returns>
    public async Task<SignalMonitorResult> MonitorSignalsAsync(
        SignalMonitorContext context,
        CancellationTokenSource signalCts,
        CancellationToken externalCancellation)
    {
        // Reset state for this monitoring session
        ResetState();

        var pollInterval = TimeSpan.FromMilliseconds(200);
        var commandFailedAddress = PlcAddress.Parse(_signalMap.CommandFailed);
        var completionAddress = PlcAddress.Parse(context.CompletionSignalAddress);

        _logger?.LogDebug($"[SignalMonitor] Started monitoring for {context.CommandId} on {context.PlcDeviceId}/Slot{context.SlotId}");

        while (!externalCancellation.IsCancellationRequested)
        {
            try
            {
                // 1. Check for alarm (ErrorCode != 0)
                var errorDetail = await CheckForAlarmAsync(externalCancellation).ConfigureAwait(false);

                if (errorDetail != null && !_alarmDetected)
                {
                    _alarmDetected = true;
                    _detectedError = errorDetail;

                    var elapsed = (DateTimeOffset.UtcNow - context.StartTime).TotalMilliseconds;
                    context.Steps.Add($"⚠️ Alarm detected after {elapsed:F0}ms");
                    context.Steps.Add($"Error Code: {errorDetail.ErrorCode} - {errorDetail.ErrorMessage}");

                    _logger?.LogWarning($"[SignalMonitor] Alarm detected for {context.CommandId}: Code={errorDetail.ErrorCode}, Message={errorDetail.ErrorMessage}, Elapsed={elapsed:F0}ms");

                    // Send alarm notification to result channel (only once)
                    await SendAlarmNotificationAsync(context, errorDetail, externalCancellation)
                        .ConfigureAwait(false);

                    // If failOnAlarm, cancel execution and return immediately
                    if (context.FailOnAlarm)
                    {
                        _logger?.LogWarning($"[SignalMonitor] FailOnAlarm=true, stopping execution for {context.CommandId}");
                        await signalCts.CancelAsync().ConfigureAwait(false);
                        return SignalMonitorResult.Alarm(errorDetail);
                    }
                    else
                    {
                        _logger?.LogInformation($"[SignalMonitor] FailOnAlarm=false, continuing execution for {context.CommandId}");
                    }
                }

                // 2. Check CommandFailed flag
                var hasFailed = await _plcClient.ReadAsync<bool>(commandFailedAddress, externalCancellation)
                    .ConfigureAwait(false);

                if (hasFailed)
                {
                    var elapsed = (DateTimeOffset.UtcNow - context.StartTime).TotalMilliseconds;
                    context.Steps.Add($"Command failed after {elapsed:F0}ms");

                    var errorInfo = _detectedError != null ? $" (Error: {_detectedError.ErrorCode})" : "";
                    _logger?.LogWarning($"[SignalMonitor] CommandFailed signal detected for {context.CommandId}{errorInfo}, Elapsed={elapsed:F0}ms");

                    await signalCts.CancelAsync().ConfigureAwait(false);
                    return SignalMonitorResult.Failed(_detectedError);
                }

                // 3. Check Completion flag
                var isCompleted = await _plcClient.ReadAsync<bool>(completionAddress, externalCancellation)
                    .ConfigureAwait(false);

                if (isCompleted)
                {
                    var elapsed = (DateTimeOffset.UtcNow - context.StartTime).TotalMilliseconds;
                    context.Steps.Add($"Command completed after {elapsed:F0}ms");

                    var warningInfo = _detectedError != null ? $" (with warning: {_detectedError.ErrorCode})" : "";
                    _logger?.LogInformation($"[SignalMonitor] Command {context.CommandId} completed successfully{warningInfo}, Elapsed={elapsed:F0}ms");

                    await signalCts.CancelAsync().ConfigureAwait(false);
                    return SignalMonitorResult.Completed(_detectedError);
                }

                await Task.Delay(pollInterval, externalCancellation).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _logger?.LogDebug($"[SignalMonitor] Monitoring cancelled for {context.CommandId}");
                break;
            }
        }

        _logger?.LogDebug($"[SignalMonitor] Monitoring ended for {context.CommandId} (external cancellation)");
        return SignalMonitorResult.None();
    }

    /// <summary>
    /// Checks for alarm condition by reading ErrorCode from PLC.
    /// </summary>
    /// <returns>ErrorDetail if alarm is active (ErrorCode != 0), null otherwise.</returns>
    private async Task<ErrorDetail?> CheckForAlarmAsync(CancellationToken cancellationToken)
    {
        var errorCodeAddress = PlcAddress.Parse(_signalMap.ErrorCode);
        var errorCode = await _plcClient.ReadAsync<int>(errorCodeAddress, cancellationToken)
            .ConfigureAwait(false);

        if (errorCode == 0)
            return null;

        var errorMessage = PlcErrorCodeMapper.GetMessage(errorCode);
        return new ErrorDetail(errorCode, errorMessage);
    }

    /// <summary>
    /// Sends alarm notification to result channel (only once per monitoring session).
    /// </summary>
    private async Task SendAlarmNotificationAsync(
        SignalMonitorContext context,
        ErrorDetail errorDetail,
        CancellationToken cancellationToken)
    {
        if (_alarmNotificationSent)
            return;

        _alarmNotificationSent = true;

        var alarmNotification = new CommandResult
        {
            CommandId = context.CommandId,
            PlcDeviceId = context.PlcDeviceId,
            SlotId = context.SlotId,
            Status = ExecutionStatus.Alarm,
            Message = $"⚠️ Alarm detected during execution: {errorDetail}",
            StartedAt = context.StartTime,
            CompletedAt = DateTimeOffset.UtcNow,
            PlcError = errorDetail
        };

        await context.ResultChannel.Writer.WriteAsync(alarmNotification, cancellationToken)
            .ConfigureAwait(false);

        context.Steps.Add("Sent alarm notification to result channel");
    }

    /// <summary>
    /// Resets internal state for a new monitoring session.
    /// </summary>
    private void ResetState()
    {
        _alarmDetected = false;
        _alarmNotificationSent = false;
        _detectedError = null;
    }
}
