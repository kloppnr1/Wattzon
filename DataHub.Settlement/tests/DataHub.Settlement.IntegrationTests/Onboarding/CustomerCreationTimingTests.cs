using DataHub.Settlement.Application.AddressLookup;
using DataHub.Settlement.Application.Lifecycle;
using DataHub.Settlement.Application.Onboarding;
using DataHub.Settlement.Application.Portfolio;
using DataHub.Settlement.Infrastructure.Database;
using DataHub.Settlement.Infrastructure.Lifecycle;
using DataHub.Settlement.Infrastructure.Onboarding;
using DataHub.Settlement.Infrastructure.Portfolio;
using DataHub.Settlement.UnitTests;
using FluentAssertions;
using Xunit;

namespace DataHub.Settlement.IntegrationTests.Onboarding;

[Collection("Database")]
public sealed class CustomerCreationTimingTests : IAsyncLifetime
{
    private readonly TestDatabase _db = new();
    private readonly ISignupRepository _signupRepo;
    private readonly IPortfolioRepository _portfolioRepo;
    private readonly IOnboardingService _onboardingService;

    public CustomerCreationTimingTests()
    {
        _signupRepo = new SignupRepository(TestDatabase.ConnectionString);
        _portfolioRepo = new PortfolioRepository(TestDatabase.ConnectionString);

        var processRepo = new ProcessRepository(TestDatabase.ConnectionString);
        var addressLookup = new StubAddressLookupClient();
        var clock = new TestClock();
        var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<OnboardingService>();

        _onboardingService = new OnboardingService(
            _signupRepo, _portfolioRepo, processRepo, addressLookup, clock, logger);
    }

    public Task InitializeAsync() => _db.InitializeAsync();
    public Task DisposeAsync() => _db.DisposeAsync();

    [Fact]
    public async Task Customer_NotCreated_UntilSignupActivated()
    {
        // Arrange
        var product = await _portfolioRepo.CreateProductAsync(
            "Spot Test", "spot", 0.04m, null, 39m, CancellationToken.None);

        var request = new SignupRequest(
            DarId: "test-dar-123",
            CustomerName: "John Doe",
            CprCvr: "1234567890",
            ContactType: "person",
            Email: "john@example.com",
            Phone: "+4512345678",
            ProductId: product.Id,
            Type: "switch",
            EffectiveDate: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(20),
            Gsrn: "571313180000000001"
        );

        // Act - Create signup
        var response = await _onboardingService.CreateSignupAsync(request, CancellationToken.None);

        // Assert - Customer should NOT exist yet
        var customers = await _portfolioRepo.GetCustomersAsync(CancellationToken.None);
        customers.Should().BeEmpty("customer should only be created when signup reaches 'active' state");

        // Verify signup was created with customer info but no customer_id
        var signup = await _signupRepo.GetBySignupNumberAsync(response.SignupId, CancellationToken.None);
        signup.Should().NotBeNull();
        signup!.CustomerId.Should().BeNull("customer_id should be null until activation");

        var signupDetail = await _signupRepo.GetDetailByIdAsync(signup.Id, CancellationToken.None);
        signupDetail.Should().NotBeNull();
        signupDetail!.CustomerName.Should().Be("John Doe");
        signupDetail.CprCvr.Should().Be("1234567890");
        signupDetail.ContactType.Should().Be("person");
    }

    [Fact]
    public async Task Customer_Created_WhenSignupBecomesActive()
    {
        // Arrange
        var product = await _portfolioRepo.CreateProductAsync(
            "Spot Test", "spot", 0.04m, null, 39m, CancellationToken.None);

        var request = new SignupRequest(
            DarId: "test-dar-456",
            CustomerName: "Jane Smith",
            CprCvr: "9876543210",
            ContactType: "company",
            Email: "jane@example.com",
            Phone: "+4587654321",
            ProductId: product.Id,
            Type: "switch",
            EffectiveDate: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(20),
            Gsrn: "571313180000000002"
        );

        var response = await _onboardingService.CreateSignupAsync(request, CancellationToken.None);
        var signup = await _signupRepo.GetBySignupNumberAsync(response.SignupId, CancellationToken.None);

        // Act - Simulate process completing
        await _onboardingService.SyncFromProcessAsync(
            signup!.ProcessRequestId!.Value, "completed", null, CancellationToken.None);

        // Assert - Customer should NOW exist
        var customers = await _portfolioRepo.GetCustomersAsync(CancellationToken.None);
        customers.Should().HaveCount(1, "customer should be created when signup becomes active");

        var customer = customers[0];
        customer.Name.Should().Be("Jane Smith");
        customer.CprCvr.Should().Be("9876543210");
        customer.ContactType.Should().Be("business", "signup contact_type 'company' maps to customer contact_type 'business'");

        // Verify signup is now linked to customer
        var updatedSignup = await _signupRepo.GetByIdAsync(signup.Id, CancellationToken.None);
        updatedSignup!.CustomerId.Should().Be(customer.Id);
        updatedSignup.Status.Should().Be("active");
    }

    [Fact]
    public async Task Customer_NotCreated_ForRejectedSignup()
    {
        // Arrange
        var product = await _portfolioRepo.CreateProductAsync(
            "Spot Test", "spot", 0.04m, null, 39m, CancellationToken.None);

        var request = new SignupRequest(
            DarId: "test-dar-789",
            CustomerName: "Bob Johnson",
            CprCvr: "1122334455",
            ContactType: "person",
            Email: "bob@example.com",
            Phone: "+4511223344",
            ProductId: product.Id,
            Type: "switch",
            EffectiveDate: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(20),
            Gsrn: "571313180000000003"
        );

        var response = await _onboardingService.CreateSignupAsync(request, CancellationToken.None);
        var signup = await _signupRepo.GetBySignupNumberAsync(response.SignupId, CancellationToken.None);

        // Act - Simulate process being rejected
        await _onboardingService.SyncFromProcessAsync(
            signup!.ProcessRequestId!.Value, "rejected", "CPR mismatch", CancellationToken.None);

        // Assert - Customer should NOT exist
        var customers = await _portfolioRepo.GetCustomersAsync(CancellationToken.None);
        customers.Should().BeEmpty("customer should not be created for rejected signups");

        // Verify signup remains without customer_id
        var updatedSignup = await _signupRepo.GetByIdAsync(signup.Id, CancellationToken.None);
        updatedSignup!.CustomerId.Should().BeNull();
        updatedSignup.Status.Should().Be("rejected");
    }

    [Fact]
    public async Task Customers_OnlyShowActivatedSignups()
    {
        // Arrange - Create multiple signups in different states
        var product = await _portfolioRepo.CreateProductAsync(
            "Spot Test", "spot", 0.04m, null, 39m, CancellationToken.None);

        var requests = new[]
        {
            new SignupRequest("dar-1", "Customer 1", "1111111111", "person", "c1@test.com", "+4511111111",
                product.Id, "switch", DateOnly.FromDateTime(DateTime.UtcNow).AddDays(20), "571313180000000011"),
            new SignupRequest("dar-2", "Customer 2", "2222222222", "person", "c2@test.com", "+4522222222",
                product.Id, "switch", DateOnly.FromDateTime(DateTime.UtcNow).AddDays(20), "571313180000000012"),
            new SignupRequest("dar-3", "Customer 3", "3333333333", "person", "c3@test.com", "+4533333333",
                product.Id, "switch", DateOnly.FromDateTime(DateTime.UtcNow).AddDays(20), "571313180000000013"),
        };

        var signups = new List<Signup>();
        foreach (var req in requests)
        {
            var response = await _onboardingService.CreateSignupAsync(req, CancellationToken.None);
            var signup = await _signupRepo.GetBySignupNumberAsync(response.SignupId, CancellationToken.None);
            signups.Add(signup!);
        }

        // Act - Activate first signup, reject second, leave third as registered
        await _onboardingService.SyncFromProcessAsync(
            signups[0].ProcessRequestId!.Value, "completed", null, CancellationToken.None);

        await _onboardingService.SyncFromProcessAsync(
            signups[1].ProcessRequestId!.Value, "rejected", "Test rejection", CancellationToken.None);

        // Assert - Only first customer should appear
        var customers = await _portfolioRepo.GetCustomersAsync(CancellationToken.None);
        customers.Should().HaveCount(1, "only activated signups should create customers");
        customers[0].Name.Should().Be("Customer 1");

        // Verify signup states
        var signup1 = await _signupRepo.GetByIdAsync(signups[0].Id, CancellationToken.None);
        signup1!.Status.Should().Be("active");
        signup1.CustomerId.Should().NotBeNull();

        var signup2 = await _signupRepo.GetByIdAsync(signups[1].Id, CancellationToken.None);
        signup2!.Status.Should().Be("rejected");
        signup2.CustomerId.Should().BeNull();

        var signup3 = await _signupRepo.GetByIdAsync(signups[2].Id, CancellationToken.None);
        signup3!.Status.Should().Be("registered");
        signup3.CustomerId.Should().BeNull();
    }

    [Fact]
    public async Task Customer_SharedAcrossMultipleMeteringPoints()
    {
        // Arrange - Create product
        var product = await _portfolioRepo.CreateProductAsync(
            "Spot Test", "spot", 0.04m, null, 39m, CancellationToken.None);

        // Act 1 - Create and activate first signup (home address)
        var request1 = new SignupRequest(
            DarId: "test-dar-home",
            CustomerName: "Jane Doe",
            CprCvr: "1234567890",
            ContactType: "person",
            Email: "jane@example.com",
            Phone: "+4512345678",
            ProductId: product.Id,
            Type: "switch",
            EffectiveDate: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(20),
            Gsrn: "571313180000000021"
        );

        var response1 = await _onboardingService.CreateSignupAsync(request1, CancellationToken.None);
        var signup1 = await _signupRepo.GetBySignupNumberAsync(response1.SignupId, CancellationToken.None);

        // Activate first signup → Customer created
        await _onboardingService.SyncFromProcessAsync(
            signup1!.ProcessRequestId!.Value, "completed", null, CancellationToken.None);

        var customersAfterFirst = await _portfolioRepo.GetCustomersAsync(CancellationToken.None);
        customersAfterFirst.Should().HaveCount(1, "first signup should create a customer");
        var customer = customersAfterFirst[0];

        // Act 2 - Create and activate second signup (summer residence, SAME CPR/CVR)
        var request2 = new SignupRequest(
            DarId: "test-dar-summer",
            CustomerName: "Jane Doe", // Same person
            CprCvr: "1234567890",     // SAME CPR/CVR
            ContactType: "person",
            Email: "jane@example.com",
            Phone: "+4512345678",
            ProductId: product.Id,
            Type: "switch",
            EffectiveDate: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(20),
            Gsrn: "571313180000000022" // DIFFERENT GSRN (second metering point)
        );

        var response2 = await _onboardingService.CreateSignupAsync(request2, CancellationToken.None);
        var signup2 = await _signupRepo.GetBySignupNumberAsync(response2.SignupId, CancellationToken.None);

        // Activate second signup → Should link to EXISTING customer
        await _onboardingService.SyncFromProcessAsync(
            signup2!.ProcessRequestId!.Value, "completed", null, CancellationToken.None);

        // Assert - Still only ONE customer exists
        var customersAfterSecond = await _portfolioRepo.GetCustomersAsync(CancellationToken.None);
        customersAfterSecond.Should().HaveCount(1, "second signup should NOT create a new customer");

        // Both signups should be linked to the SAME customer
        var updatedSignup1 = await _signupRepo.GetByIdAsync(signup1.Id, CancellationToken.None);
        var updatedSignup2 = await _signupRepo.GetByIdAsync(signup2.Id, CancellationToken.None);

        updatedSignup1!.CustomerId.Should().Be(customer.Id);
        updatedSignup2!.CustomerId.Should().Be(customer.Id);
        updatedSignup1.CustomerId.Should().Be(updatedSignup2.CustomerId, "both signups should share the same customer");
    }

    /// <summary>
    /// Stub that returns a matching GSRN for any DAR ID.
    /// Tests provide explicit GSRN in request which will be validated against this list.
    /// </summary>
    private sealed class StubAddressLookupClient : IAddressLookupClient
    {
        public Task<AddressLookupResult> LookupByDarIdAsync(string darId, CancellationToken ct)
        {
            // Extract GSRN from DAR ID pattern (test-dar-{number} or just use a default pattern)
            var gsrns = new List<MeteringPointInfo>
            {
                new("571313180000000001", "E17", "344"),
                new("571313180000000002", "E17", "344"),
                new("571313180000000003", "E17", "344"),
                new("571313180000000011", "E17", "344"),
                new("571313180000000012", "E17", "344"),
                new("571313180000000013", "E17", "344"),
                new("571313180000000021", "E17", "344"),
                new("571313180000000022", "E17", "344")
            };

            return Task.FromResult(new AddressLookupResult(gsrns));
        }
    }
}
