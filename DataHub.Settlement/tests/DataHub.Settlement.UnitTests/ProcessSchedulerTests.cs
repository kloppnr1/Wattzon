using DataHub.Settlement.Application.DataHub;
using DataHub.Settlement.Application.Lifecycle;
using DataHub.Settlement.Application.Onboarding;
using DataHub.Settlement.Application.Portfolio;
using DataHub.Settlement.Infrastructure.DataHub;
using DataHub.Settlement.Infrastructure.Lifecycle;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DataHub.Settlement.UnitTests;

public class ProcessSchedulerTests
{
    private readonly ProcessStateMachineTests.InMemoryProcessRepository _processRepo = new();
    private readonly TestClock _clock = new();
    private readonly StubSignupRepository _signupRepo = new();
    private readonly StubDataHubClient _dataHubClient = new();
    private readonly StubBrsRequestBuilder _brsBuilder = new();
    private readonly StubOnboardingService _onboardingService = new();

    private ProcessSchedulerService CreateSut() => new(
        _processRepo,
        _signupRepo,
        _dataHubClient,
        _brsBuilder,
        _onboardingService,
        _clock,
        NullLogger<ProcessSchedulerService>.Instance);

    [Fact]
    public async Task Does_not_effectuate_processes()
    {
        // ProcessScheduler only sends pending processes to DataHub
        // Effectuation (marking completed) is handled exclusively by RSM-007 receipt
        _clock.Today = new DateOnly(2025, 2, 1);
        var sm = new ProcessStateMachine(_processRepo, _clock);

        var request = await sm.CreateRequestAsync("571313100000012345", "supplier_switch",
            new DateOnly(2025, 2, 1), CancellationToken.None);
        await sm.MarkSentAsync(request.Id, "corr-1", CancellationToken.None);
        await sm.MarkAcknowledgedAsync(request.Id, CancellationToken.None);

        // Process is in effectuation_pending
        var before = await _processRepo.GetAsync(request.Id, CancellationToken.None);
        before!.Status.Should().Be("effectuation_pending");

        var sut = CreateSut();
        await sut.RunTickAsync(CancellationToken.None);

        // Process should STILL be in effectuation_pending (not completed)
        var after = await _processRepo.GetAsync(request.Id, CancellationToken.None);
        after!.Status.Should().Be("effectuation_pending",
            "ProcessScheduler no longer effectuates - only RSM-007 marks processes completed");
    }

    // ── Test stubs ──

    private sealed class StubSignupRepository : ISignupRepository
    {
        public Task<Signup> CreateAsync(string signupNumber, string darId, string gsrn,
            string customerName, string customerCprCvr, string customerContactType,
            Guid productId, Guid processRequestId, string type, DateOnly effectiveDate,
            Guid? correctedFromId, CancellationToken ct)
            => throw new NotImplementedException();
        public Task<string> NextSignupNumberAsync(CancellationToken ct)
            => throw new NotImplementedException();
        public Task<Signup?> GetBySignupNumberAsync(string signupNumber, CancellationToken ct)
            => throw new NotImplementedException();
        public Task<Signup?> GetByIdAsync(Guid id, CancellationToken ct)
            => throw new NotImplementedException();
        public Task<Signup?> GetByProcessRequestIdAsync(Guid processRequestId, CancellationToken ct)
            => Task.FromResult<Signup?>(null);
        public Task<Signup?> GetActiveByGsrnAsync(string gsrn, CancellationToken ct)
            => throw new NotImplementedException();
        public Task UpdateStatusAsync(Guid id, string status, string? rejectionReason, CancellationToken ct)
            => Task.CompletedTask;
        public Task SetProcessRequestIdAsync(Guid id, Guid processRequestId, CancellationToken ct)
            => throw new NotImplementedException();
        public Task LinkCustomerAsync(Guid signupId, Guid customerId, CancellationToken ct)
            => Task.CompletedTask;
        public Task<string?> GetCustomerCprCvrAsync(Guid signupId, CancellationToken ct)
            => throw new NotImplementedException();
        public Task<IReadOnlyList<SignupListItem>> GetAllAsync(string? statusFilter, CancellationToken ct)
            => throw new NotImplementedException();
        public Task<PagedResult<SignupListItem>> GetAllPagedAsync(string? statusFilter, int page, int pageSize, CancellationToken ct)
            => throw new NotImplementedException();
        public Task<IReadOnlyList<SignupListItem>> GetRecentAsync(int limit, CancellationToken ct)
            => throw new NotImplementedException();
        public Task<SignupDetail?> GetDetailByIdAsync(Guid id, CancellationToken ct)
            => throw new NotImplementedException();
        public Task<IReadOnlyList<SignupCorrectionLink>> GetCorrectionChainAsync(Guid signupId, CancellationToken ct)
            => throw new NotImplementedException();
    }

    private sealed class StubDataHubClient : IDataHubClient
    {
        public Task<DataHubMessage?> PeekAsync(QueueName queue, CancellationToken ct)
            => throw new NotImplementedException();
        public Task DequeueAsync(string messageId, CancellationToken ct)
            => throw new NotImplementedException();
        public Task<DataHubResponse> SendRequestAsync(string processType, string cimPayload, CancellationToken ct)
            => Task.FromResult(new DataHubResponse("corr-stub", true, null));
    }

    private sealed class StubBrsRequestBuilder : IBrsRequestBuilder
    {
        public string BuildBrs001(string gsrn, string cprCvr, DateOnly effectiveDate)
            => "{}";
        public string BuildBrs002(string gsrn, DateOnly effectiveDate)
            => throw new NotImplementedException();
        public string BuildBrs003(string gsrn, string originalCorrelationId)
            => throw new NotImplementedException();
        public string BuildBrs009(string gsrn, string cprCvr, DateOnly effectiveDate)
            => "{}";
        public string BuildBrs010(string gsrn, DateOnly effectiveDate)
            => throw new NotImplementedException();
        public string BuildBrs043(string gsrn, string cprCvr, DateOnly effectiveDate)
            => throw new NotImplementedException();
        public string BuildBrs044(string gsrn, string originalCorrelationId)
            => throw new NotImplementedException();
        public string BuildBrs042(string gsrn, DateOnly effectiveDate)
            => throw new NotImplementedException();
    }

    private sealed class StubOnboardingService : IOnboardingService
    {
        public Task<AddressLookupResponse> LookupAddressAsync(string darId, CancellationToken ct)
            => throw new NotImplementedException();
        public Task<SignupResponse> CreateSignupAsync(SignupRequest request, CancellationToken ct)
            => throw new NotImplementedException();
        public Task<SignupStatusResponse?> GetStatusAsync(string signupNumber, CancellationToken ct)
            => throw new NotImplementedException();
        public Task CancelAsync(string signupNumber, CancellationToken ct)
            => throw new NotImplementedException();
        public Task SyncFromProcessAsync(Guid processRequestId, string processStatus, string? reason, CancellationToken ct)
            => Task.CompletedTask;
    }
}
