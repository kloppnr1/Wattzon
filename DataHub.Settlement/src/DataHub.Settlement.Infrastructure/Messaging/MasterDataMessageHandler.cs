using DataHub.Settlement.Application.DataHub;
using DataHub.Settlement.Application.Lifecycle;
using DataHub.Settlement.Application.Messaging;
using DataHub.Settlement.Application.Onboarding;
using DataHub.Settlement.Application.Parsing;
using DataHub.Settlement.Application.Portfolio;
using DataHub.Settlement.Application.Tariff;
using DataHub.Settlement.Domain;
using DataHub.Settlement.Infrastructure.Parsing;
using Microsoft.Extensions.Logging;

namespace DataHub.Settlement.Infrastructure.Messaging;

/// <summary>
/// Handles all inbound messages from the MasterData queue.
/// Each RSM handler is a focused method routed via switch expression.
/// </summary>
public sealed class MasterDataMessageHandler : IMessageHandler
{
    private readonly ICimParser _parser;
    private readonly IPortfolioRepository _portfolioRepo;
    private readonly IProcessRepository _processRepo;
    private readonly ISignupRepository _signupRepo;
    private readonly IOnboardingService _onboardingService;
    private readonly ITariffRepository _tariffRepo;
    private readonly IClock _clock;
    private readonly EffectuationService _effectuationService;
    private readonly ILogger<MasterDataMessageHandler> _logger;

    public MasterDataMessageHandler(
        ICimParser parser,
        IPortfolioRepository portfolioRepo,
        IProcessRepository processRepo,
        ISignupRepository signupRepo,
        IOnboardingService onboardingService,
        ITariffRepository tariffRepo,
        IClock clock,
        EffectuationService effectuationService,
        ILogger<MasterDataMessageHandler> logger)
    {
        _parser = parser;
        _portfolioRepo = portfolioRepo;
        _processRepo = processRepo;
        _signupRepo = signupRepo;
        _onboardingService = onboardingService;
        _tariffRepo = tariffRepo;
        _clock = clock;
        _effectuationService = effectuationService;
        _logger = logger;
    }

    public QueueName Queue => QueueName.MasterData;

    /// <summary>
    /// Routes a MasterData message to the appropriate handler based on normalized message type.
    /// </summary>
    public Task HandleAsync(DataHubMessage message, CancellationToken ct)
    {
        var normalizedType = RsmMessageTypes.Normalize(message.MessageType);

        return normalizedType switch
        {
            RsmMessageTypes.MasterData => HandleActivationAsync(message, ct),
            RsmMessageTypes.Request => HandleSupplierSwitchResponseAsync(message, ct),
            RsmMessageTypes.EndOfSupply => HandleEndOfSupplyResponseAsync(message, ct),
            RsmMessageTypes.Cancellation => HandleCancellationResponseAsync(message, ct),
            RsmMessageTypes.CustomerData => HandleCustomerDataAsync(message, ct),
            RsmMessageTypes.PriceAttachments => HandleTariffDataAsync(message, ct),
            RsmMessageTypes.Notification => HandleChangeNotificationAsync(message, ct),
            _ => HandleUnknownAsync(message),
        };
    }

    /// <summary>RSM-022 — Metering point activation (authoritative signal from DataHub that supply has started).</summary>
    private async Task HandleActivationAsync(DataHubMessage message, CancellationToken ct)
    {
        var masterData = _parser.ParseRsm022(message.RawPayload);

        await _portfolioRepo.EnsureGridAreaAsync(
            masterData.GridAreaCode, masterData.GridOperatorGln,
            $"Grid {masterData.GridAreaCode}", masterData.PriceArea, ct);

        var mp = new MeteringPoint(
            masterData.MeteringPointId, masterData.Type, masterData.SettlementMethod,
            masterData.GridAreaCode, masterData.GridOperatorGln, masterData.PriceArea, "connected");

        try
        {
            await _portfolioRepo.CreateMeteringPointAsync(mp, ct);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Npgsql.PostgresException)
        {
            _logger.LogInformation("Metering point {Gsrn} already exists, skipping create", masterData.MeteringPointId);
        }

        await _portfolioRepo.ActivateMeteringPointAsync(
            masterData.MeteringPointId, masterData.SupplyStart.UtcDateTime, ct);

        // RSM-022 is the authoritative activation signal from DataHub
        // This is the ONLY place where processes are marked "completed"
        var signup = await _signupRepo.GetActiveByGsrnAsync(masterData.MeteringPointId, ct);
        if (signup is not null && signup.ProcessRequestId.HasValue)
        {
            var process = await _processRepo.GetAsync(signup.ProcessRequestId.Value, ct);
            if (process is not null && process.Status is "cancellation_pending" or "cancelled")
            {
                _logger.LogInformation(
                    "RSM-022: Skipping activation for process {ProcessId} — process is {Status}",
                    process.Id, process.Status);
            }
            else
            {
                if (process is not null && !process.CustomerDataReceived)
                    _logger.LogWarning("RSM-022: Customer data (RSM-028) not yet received for process {ProcessId}", process.Id);
                if (process is not null && !process.TariffDataReceived)
                    _logger.LogWarning("RSM-022: Tariff data (RSM-031) not yet received for process {ProcessId}", process.Id);

                var effectiveDate = DateOnly.FromDateTime(masterData.SupplyStart.UtcDateTime);

                // Delegate to EffectuationService which wraps all DB operations in transactions
                await _effectuationService.ActivateAsync(
                    signup.ProcessRequestId.Value,
                    signup.Id,
                    masterData.MeteringPointId,
                    effectiveDate,
                    process?.ProcessType,
                    process?.DatahubCorrelationId,
                    ct);
            }
        }
        else if (signup is null)
        {
            // No signup — create supply period without contract (pre-onboarding metering points)
            await _portfolioRepo.CreateSupplyPeriodAsync(
                masterData.MeteringPointId, DateOnly.FromDateTime(masterData.SupplyStart.UtcDateTime), ct);

            _logger.LogInformation("RSM-022: Activated metering point {Gsrn}, supply from {Start} (no signup)",
                masterData.MeteringPointId, masterData.SupplyStart);
        }
        else
        {
            _logger.LogWarning(
                "RSM-022: Signup {SignupNumber} for GSRN {Gsrn} has no process request — portfolio not created",
                signup.SignupNumber, masterData.MeteringPointId);
        }
    }

    /// <summary>RSM-001 — BRS-001/BRS-009 response (supplier switch / move-in ack or reject).</summary>
    private async Task HandleSupplierSwitchResponseAsync(DataHubMessage message, CancellationToken ct)
    {
        var receipt = _parser.ParseRsm001Response(message.RawPayload);

        var process = await _processRepo.GetByCorrelationIdAsync(receipt.CorrelationId, ct);
        if (process is null)
        {
            _logger.LogWarning("RSM-001: No process found for correlation {CorrelationId}", receipt.CorrelationId);
            return;
        }

        var stateMachine = new ProcessStateMachine(_processRepo, _clock);

        // Status-based disambiguation: same correlation ID is used for both original and cancel RSM-001 response
        if (process.Status == "cancellation_pending")
        {
            if (receipt.Accepted)
            {
                await stateMachine.MarkCancelledAsync(process.Id, "Cancellation acknowledged by DataHub", ct);
                await _onboardingService.SyncFromProcessAsync(process.Id, "cancelled", null, ct);
                _logger.LogInformation("RSM-001: Cancellation acknowledged for process {ProcessId}", process.Id);
            }
            else
            {
                var reason = receipt.RejectionReason ?? receipt.RejectionCode ?? "Cancellation rejected";
                await stateMachine.RevertCancellationAsync(process.Id, reason, ct);
                await _onboardingService.SyncFromProcessAsync(process.Id, "effectuation_pending", null, ct);
                _logger.LogWarning("RSM-001: Cancellation rejected for process {ProcessId}: {Reason}", process.Id, reason);
            }
        }
        else if (receipt.Accepted)
        {
            await stateMachine.MarkAcknowledgedAsync(process.Id, ct);
            await _onboardingService.SyncFromProcessAsync(process.Id, "effectuation_pending", null, ct);

            _logger.LogInformation("RSM-001: Process {ProcessId} accepted for GSRN {Gsrn}",
                process.Id, process.Gsrn);
        }
        else
        {
            var reason = receipt.RejectionReason ?? receipt.RejectionCode ?? "Unknown rejection";
            await stateMachine.MarkRejectedAsync(process.Id, reason, ct);
            await _onboardingService.SyncFromProcessAsync(process.Id, "rejected", reason, ct);

            _logger.LogWarning("RSM-001: Process {ProcessId} rejected for GSRN {Gsrn}: {Reason}",
                process.Id, process.Gsrn, reason);
        }
    }

    /// <summary>RSM-005 — BRS-002/BRS-010 response (end of supply / move-out ack or reject).</summary>
    private async Task HandleEndOfSupplyResponseAsync(DataHubMessage message, CancellationToken ct)
    {
        var receipt = _parser.ParseRsm005Response(message.RawPayload);

        var process = await _processRepo.GetByCorrelationIdAsync(receipt.CorrelationId, ct);
        if (process is null)
        {
            _logger.LogWarning("RSM-005: No process found for correlation {CorrelationId}", receipt.CorrelationId);
            return;
        }

        var stateMachine = new ProcessStateMachine(_processRepo, _clock);

        if (receipt.Accepted)
        {
            // BRS-002/010: sent_to_datahub → acknowledged → effectuation_pending → completed
            if (process.Status == "sent_to_datahub")
                await stateMachine.MarkAcknowledgedAsync(process.Id, ct);

            await stateMachine.MarkCompletedAsync(process.Id, ct);

            // End supply period + contract on effective date
            if (process.EffectiveDate.HasValue)
            {
                await _portfolioRepo.EndSupplyPeriodAsync(process.Gsrn, process.EffectiveDate.Value, process.ProcessType, ct);
                await _portfolioRepo.EndContractAsync(process.Gsrn, process.EffectiveDate.Value, ct);
            }

            await _onboardingService.SyncFromProcessAsync(process.Id, "completed", null, ct);
            _logger.LogInformation("RSM-005: Process {ProcessId} accepted — supply ended for {Gsrn}", process.Id, process.Gsrn);
        }
        else
        {
            var reason = receipt.RejectionReason ?? receipt.RejectionCode ?? "Unknown rejection";
            await stateMachine.MarkRejectedAsync(process.Id, reason, ct);
            await _onboardingService.SyncFromProcessAsync(process.Id, "rejected", reason, ct);
            _logger.LogWarning("RSM-005: Process {ProcessId} rejected for {Gsrn}: {Reason}", process.Id, process.Gsrn, reason);
        }
    }

    /// <summary>RSM-024 — Cancellation response from DataHub.</summary>
    private async Task HandleCancellationResponseAsync(DataHubMessage message, CancellationToken ct)
    {
        var receipt = _parser.ParseRsm024Response(message.RawPayload);

        var process = await _processRepo.GetByCorrelationIdAsync(receipt.CorrelationId, ct);
        if (process is null)
        {
            _logger.LogWarning("RSM-024: No process found for correlation {CorrelationId}", receipt.CorrelationId);
            return;
        }

        var stateMachine = new ProcessStateMachine(_processRepo, _clock);

        if (receipt.Accepted)
        {
            await stateMachine.MarkCancelledAsync(process.Id, "Cancellation acknowledged by DataHub (RSM-024)", ct);
            await _onboardingService.SyncFromProcessAsync(process.Id, "cancelled", null, ct);
            _logger.LogInformation("RSM-024: Cancellation acknowledged for process {ProcessId}", process.Id);
        }
        else
        {
            var reason = receipt.RejectionReason ?? receipt.RejectionCode ?? "Cancellation rejected";
            await stateMachine.RevertCancellationAsync(process.Id, reason, ct);
            await _onboardingService.SyncFromProcessAsync(process.Id, "effectuation_pending", null, ct);
            _logger.LogWarning("RSM-024: Cancellation rejected for process {ProcessId}: {Reason}", process.Id, reason);
        }
    }

    /// <summary>RSM-028 — Customer data reception.</summary>
    private async Task HandleCustomerDataAsync(DataHubMessage message, CancellationToken ct)
    {
        var customerData = _parser.ParseRsm028(message.RawPayload);

        await _portfolioRepo.StageCustomerDataAsync(
            customerData.MeteringPointId, customerData.CustomerName, customerData.CprCvr,
            customerData.CustomerType, customerData.Phone, customerData.Email,
            message.CorrelationId, ct);

        if (message.CorrelationId is not null)
            await _processRepo.MarkCustomerDataReceivedAsync(message.CorrelationId, ct);

        _logger.LogInformation(
            "RSM-028: Staged customer data for {Gsrn} — {CustomerName} ({CustomerType})",
            customerData.MeteringPointId, customerData.CustomerName, customerData.CustomerType);
    }

    /// <summary>RSM-031 — Tariff/price attachment data reception.</summary>
    private async Task HandleTariffDataAsync(DataHubMessage message, CancellationToken ct)
    {
        var priceData = _parser.ParseRsm031(message.RawPayload);

        await _tariffRepo.StoreTariffAttachmentsAsync(
            priceData.MeteringPointId, priceData.Tariffs, message.CorrelationId, ct);

        if (message.CorrelationId is not null)
            await _processRepo.MarkTariffDataReceivedAsync(message.CorrelationId, ct);

        _logger.LogInformation(
            "RSM-031: Stored {TariffCount} tariff attachments for {Gsrn}",
            priceData.Tariffs.Count, priceData.MeteringPointId);
    }

    /// <summary>RSM-004 — Change notifications (auto-cancel, forced transfer, stop of supply, etc.).</summary>
    private async Task HandleChangeNotificationAsync(DataHubMessage message, CancellationToken ct)
    {
        var change = _parser.ParseRsm004(message.RawPayload);

        switch (change.ReasonCode)
        {
            case Rsm004ReasonCodes.AutoCancel:
            {
                // Auto-cancellation: customer data deadline exceeded
                var signup = await _signupRepo.GetActiveByGsrnAsync(change.Gsrn, ct);
                if (signup?.ProcessRequestId is not null)
                {
                    var stateMachine = new ProcessStateMachine(_processRepo, _clock);
                    await stateMachine.MarkAutoCancelledAsync(
                        signup.ProcessRequestId.Value,
                        "DataHub auto-cancellation: customer data deadline exceeded (D11)",
                        ct);
                    await _onboardingService.SyncFromProcessAsync(
                        signup.ProcessRequestId.Value, "cancelled", null, ct);

                    _logger.LogWarning("RSM-004/D11: Auto-cancelled process for {Gsrn}", change.Gsrn);
                }
                else
                {
                    _logger.LogWarning("RSM-004/D11: No active signup/process found for {Gsrn}", change.Gsrn);
                }

                break;
            }
            case Rsm004ReasonCodes.ForcedTransfer or Rsm004ReasonCodes.StopOfSupplyByOtherSupplier:
            {
                // Forced transfer (D31) or stop of supply by other supplier (E01): end supply period + contract
                var effectiveDate = DateOnly.FromDateTime(change.EffectiveDate.UtcDateTime);
                var endReason = change.ReasonCode == Rsm004ReasonCodes.ForcedTransfer ? "forced_transfer" : "other_supplier_takeover";
                await _portfolioRepo.EndSupplyPeriodAsync(change.Gsrn, effectiveDate, endReason, ct);
                await _portfolioRepo.EndContractAsync(change.Gsrn, effectiveDate, ct);

                _logger.LogWarning("RSM-004/{ReasonCode}: Ended supply for {Gsrn}, effective {Date}",
                    change.ReasonCode, change.Gsrn, effectiveDate);
                break;
            }
            case Rsm004ReasonCodes.StopOfSupply:
            {
                // Stop of supply notification
                var effectiveDate = DateOnly.FromDateTime(change.EffectiveDate.UtcDateTime);
                await _portfolioRepo.EndSupplyPeriodAsync(change.Gsrn, effectiveDate, "stop_of_supply", ct);
                await _portfolioRepo.EndContractAsync(change.Gsrn, effectiveDate, ct);

                _logger.LogInformation("RSM-004/E03: Stop of supply for {Gsrn}, effective {Date}",
                    change.Gsrn, effectiveDate);
                break;
            }
            case Rsm004ReasonCodes.CorrectionAccepted:
                _logger.LogInformation("RSM-004/D34: Correction accepted for {Gsrn}", change.Gsrn);
                break;
            case Rsm004ReasonCodes.CorrectionRejected:
                _logger.LogWarning("RSM-004/D35: Correction rejected for {Gsrn}", change.Gsrn);
                break;
            case Rsm004ReasonCodes.EndOfSupplyStop:
                _logger.LogInformation("RSM-004/E20: End of supply stop notification for {Gsrn}", change.Gsrn);
                break;
            case Rsm004ReasonCodes.SpecialRules:
                _logger.LogInformation(
                    "RSM-004/D46: Special rules for start of supply on {Gsrn}, effective {Date}",
                    change.Gsrn, change.EffectiveDate);
                break;
            default:
            {
                if (change.NewGridAreaCode is not null)
                {
                    var priceArea = CimJsonParser.GridAreaToPriceArea.GetValueOrDefault(change.NewGridAreaCode, "DK1");
                    await _portfolioRepo.UpdateMeteringPointGridAreaAsync(
                        change.Gsrn, change.NewGridAreaCode, priceArea, ct);

                    _logger.LogInformation("RSM-004: Updated grid area for {Gsrn} to {GridArea}",
                        change.Gsrn, change.NewGridAreaCode);
                }
                else
                {
                    _logger.LogInformation("RSM-004: Received change notification for {Gsrn}, no grid area update",
                        change.Gsrn);
                }

                break;
            }
        }
    }

    private Task HandleUnknownAsync(DataHubMessage message)
    {
        _logger.LogInformation("Received MasterData message type {Type}, not handled yet", message.MessageType);
        return Task.CompletedTask;
    }
}
