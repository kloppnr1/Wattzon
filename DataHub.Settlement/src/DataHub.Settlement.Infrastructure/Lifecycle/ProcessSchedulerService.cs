using DataHub.Settlement.Application.DataHub;
using DataHub.Settlement.Application.Lifecycle;
using DataHub.Settlement.Application.Onboarding;
using DataHub.Settlement.Domain;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DataHub.Settlement.Infrastructure.Lifecycle;

public sealed class ProcessSchedulerService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);

    private readonly IProcessRepository _processRepo;
    private readonly ISignupRepository _signupRepo;
    private readonly IDataHubClient _dataHubClient;
    private readonly IBrsRequestBuilder _brsBuilder;
    private readonly IOnboardingService _onboardingService;
    private readonly IClock _clock;
    private readonly ILogger<ProcessSchedulerService> _logger;

    public ProcessSchedulerService(
        IProcessRepository processRepo,
        ISignupRepository signupRepo,
        IDataHubClient dataHubClient,
        IBrsRequestBuilder brsBuilder,
        IOnboardingService onboardingService,
        IClock clock,
        ILogger<ProcessSchedulerService> logger)
    {
        _processRepo = processRepo;
        _signupRepo = signupRepo;
        _dataHubClient = dataHubClient;
        _brsBuilder = brsBuilder;
        _onboardingService = onboardingService;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunTickAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Process scheduler tick failed");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    internal async Task RunTickAsync(CancellationToken ct)
    {
        await SendPendingRequestsAsync(ct);
        await EffectuateReadyProcessesAsync(ct);
    }

    private async Task SendPendingRequestsAsync(CancellationToken ct)
    {
        var pending = await _processRepo.GetByStatusAsync("pending", ct);

        foreach (var process in pending)
        {
            try
            {
                // Look up signup to get CPR/CVR for BRS request
                var signup = await _signupRepo.GetByProcessRequestIdAsync(process.Id, ct);
                if (signup is null)
                {
                    _logger.LogWarning("No signup found for pending process {ProcessId}, skipping", process.Id);
                    continue;
                }

                var cprCvr = await _signupRepo.GetCustomerCprCvrAsync(signup.Id, ct);
                if (cprCvr is null)
                {
                    _logger.LogWarning("No customer found for signup {SignupId}, skipping", signup.Id);
                    continue;
                }

                // Build the BRS request
                var cimPayload = process.ProcessType switch
                {
                    "supplier_switch" => _brsBuilder.BuildBrs001(process.Gsrn, cprCvr, process.EffectiveDate!.Value),
                    "move_in" => _brsBuilder.BuildBrs009(process.Gsrn, cprCvr, process.EffectiveDate!.Value),
                    _ => null,
                };

                if (cimPayload is null)
                {
                    _logger.LogWarning("Unsupported process type {Type} for process {ProcessId}", process.ProcessType, process.Id);
                    continue;
                }

                // Send to DataHub
                var response = await _dataHubClient.SendRequestAsync(process.ProcessType, cimPayload, ct);

                // Transition: pending → sent_to_datahub
                var stateMachine = new ProcessStateMachine(_processRepo, _clock);
                await stateMachine.MarkSentAsync(process.Id, response.CorrelationId, ct);

                // Sync signup status
                await _onboardingService.SyncFromProcessAsync(process.Id, "sent_to_datahub", null, ct);

                _logger.LogInformation(
                    "Sent {ProcessType} to DataHub for GSRN {Gsrn}, correlation={CorrelationId}",
                    process.ProcessType, process.Gsrn, response.CorrelationId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Failed to send pending process {ProcessId} to DataHub", process.Id);
            }
        }
    }

    private async Task EffectuateReadyProcessesAsync(CancellationToken ct)
    {
        var effectuationPending = await _processRepo.GetByStatusAsync("effectuation_pending", ct);
        var today = _clock.Today;

        foreach (var process in effectuationPending)
        {
            if (!process.EffectiveDate.HasValue || process.EffectiveDate.Value > today)
                continue;

            try
            {
                var stateMachine = new ProcessStateMachine(_processRepo, _clock);
                await stateMachine.MarkCompletedAsync(process.Id, ct);

                // Sync signup status
                await _onboardingService.SyncFromProcessAsync(process.Id, "completed", null, ct);

                _logger.LogInformation("Auto-effectuated process {ProcessId} for GSRN {Gsrn} (effective date {EffectiveDate})",
                    process.Id, process.Gsrn, process.EffectiveDate.Value);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Failed to auto-effectuate process {ProcessId}", process.Id);
            }
        }
    }
}
