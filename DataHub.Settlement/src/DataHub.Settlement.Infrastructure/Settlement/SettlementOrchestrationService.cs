using DataHub.Settlement.Application.Lifecycle;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DataHub.Settlement.Infrastructure.Settlement;

public sealed class SettlementOrchestrationService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromHours(1);

    private readonly IProcessRepository _processRepo;
    private readonly SettlementTriggerService _trigger;
    private readonly ILogger<SettlementOrchestrationService> _logger;

    public SettlementOrchestrationService(
        IProcessRepository processRepo,
        SettlementTriggerService trigger,
        ILogger<SettlementOrchestrationService> logger)
    {
        _processRepo = processRepo;
        _trigger = trigger;
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
                _logger.LogError(ex, "Settlement orchestration tick failed");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    internal async Task RunTickAsync(CancellationToken ct)
    {
        // Regular settlement for active supply periods
        var completedProcesses = await _processRepo.GetByStatusAsync("completed", ct);
        foreach (var process in completedProcesses)
        {
            try
            {
                await _trigger.TrySettleProcessAsync(process, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Failed to settle process {ProcessId} for GSRN {Gsrn}", process.Id, process.Gsrn);
            }
        }

        // Final settlement for offboarding processes (supply ended, settle remaining periods)
        var offboardingProcesses = await _processRepo.GetByStatusAsync("offboarding", ct);
        foreach (var process in offboardingProcesses)
        {
            try
            {
                await _trigger.TryFinalSettleAsync(process, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Final settlement failed for offboarding process {ProcessId}, GSRN {Gsrn}",
                    process.Id, process.Gsrn);
            }
        }
    }
}
