using DataHub.Settlement.Application.AddressLookup;
using DataHub.Settlement.Application.DataHub;
using DataHub.Settlement.Application.Lifecycle;
using DataHub.Settlement.Application.Messaging;
using DataHub.Settlement.Application.Onboarding;
using DataHub.Settlement.Application.Portfolio;
using DataHub.Settlement.Infrastructure.DataHub;
using DataHub.Settlement.Infrastructure.Lifecycle;
using DataHub.Settlement.Infrastructure.Onboarding;
using DataHub.Settlement.Infrastructure.Portfolio;
using DataHub.Settlement.UnitTests;
using FluentAssertions;
using Xunit;

namespace DataHub.Settlement.IntegrationTests.Onboarding;

[Collection("Database")]
public sealed class BillingFrequencyTests : IAsyncLifetime
{
    private readonly TestDatabase _db = new();
    private readonly ISignupRepository _signupRepo;
    private readonly IPortfolioRepository _portfolioRepo;
    private readonly IOnboardingService _onboardingService;

    public BillingFrequencyTests()
    {
        _signupRepo = new SignupRepository(TestDatabase.ConnectionString);
        _portfolioRepo = new PortfolioRepository(TestDatabase.ConnectionString);

        var processRepo = new ProcessRepository(TestDatabase.ConnectionString);
        var addressLookup = new StubAddressLookupClient();
        var clock = new TestClock();
        var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<OnboardingService>();

        var dataHubClient = new FakeDataHubClient();
        var brsBuilder = new BrsRequestBuilder();
        var messageRepo = new NullMessageRepository();

        _onboardingService = new OnboardingService(
            _signupRepo, _portfolioRepo, processRepo, addressLookup,
            dataHubClient, brsBuilder, messageRepo, clock, logger);
    }

    public Task InitializeAsync() => _db.InitializeAsync();
    public Task DisposeAsync() => _db.DisposeAsync();

    [Theory]
    [InlineData("weekly")]
    [InlineData("monthly")]
    [InlineData("quarterly")]
    public async Task Signup_stores_billing_frequency(string frequency)
    {
        var product = await _portfolioRepo.CreateProductAsync(
            "Spot Test", "spot", 0.04m, null, 39m, CancellationToken.None);

        var request = new SignupRequest(
            DarId: null,
            CustomerName: "Frequency Test",
            CprCvr: "1234567890",
            ContactType: "private",
            Email: "test@example.com",
            Phone: "+4512345678",
            ProductId: product.Id,
            Type: "switch",
            EffectiveDate: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(20),
            Mobile: "+4512345678",
            Gsrn: "571313180000000001",
            BillingFrequency: frequency
        );

        var response = await _onboardingService.CreateSignupAsync(request, CancellationToken.None);

        var signup = await _signupRepo.GetBySignupNumberAsync(response.SignupId, CancellationToken.None);
        signup.Should().NotBeNull();

        var detail = await _signupRepo.GetDetailByIdAsync(signup!.Id, CancellationToken.None);
        detail.Should().NotBeNull();
        detail!.BillingFrequency.Should().Be(frequency);
    }

    [Fact]
    public async Task Signup_defaults_to_monthly_when_not_specified()
    {
        var product = await _portfolioRepo.CreateProductAsync(
            "Spot Test", "spot", 0.04m, null, 39m, CancellationToken.None);

        var request = new SignupRequest(
            DarId: null,
            CustomerName: "Default Freq Test",
            CprCvr: "1234567890",
            ContactType: "private",
            Email: "test@example.com",
            Phone: "+4512345678",
            ProductId: product.Id,
            Type: "switch",
            EffectiveDate: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(20),
            Mobile: "+4512345678",
            Gsrn: "571313180000000002"
        );

        var response = await _onboardingService.CreateSignupAsync(request, CancellationToken.None);

        var signup = await _signupRepo.GetBySignupNumberAsync(response.SignupId, CancellationToken.None);
        var detail = await _signupRepo.GetDetailByIdAsync(signup!.Id, CancellationToken.None);
        detail!.BillingFrequency.Should().Be("monthly");
    }

    private sealed class StubAddressLookupClient : IAddressLookupClient
    {
        public Task<AddressLookupResult> LookupByDarIdAsync(string darId, CancellationToken ct)
            => Task.FromResult(new AddressLookupResult(new[] { new MeteringPointInfo("571313180000000001", "E17", "344") }));
    }
}
