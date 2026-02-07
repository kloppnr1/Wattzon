using DataHub.Settlement.Application.Lifecycle;
using DataHub.Settlement.Domain;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DataHub.Settlement.Infrastructure.Lifecycle;

public sealed class ProcessSchedulerService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);

    private readonly IProcessRepository _processRepo;
    private readonly IClock _clock;
    private readonly ILogger<ProcessSchedulerService> _logger;

    public ProcessSchedulerService(
        IProcessRepository processRepo,
        IClock clock,
        ILogger<ProcessSchedulerService> logger)
    {
        _processRepo = processRepo;
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
        var pending = await _processRepo.GetByStatusAsync("effectuation_pending", ct);
        var today = _clock.Today;

        foreach (var process in pending)
        {
            if (!process.EffectiveDate.HasValue || process.EffectiveDate.Value > today)
                continue;

            try
            {
                var stateMachine = new ProcessStateMachine(_processRepo, _clock);
                await stateMachine.MarkCompletedAsync(process.Id, ct);

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
