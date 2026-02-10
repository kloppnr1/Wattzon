namespace DataHub.Settlement.Application.Portfolio;

/// <summary>Danish postal address (billing or contact).</summary>
public record Address(
    string? Street,
    string? HouseNumber,
    string? Floor,
    string? Door,
    string? PostalCode,
    string? City,
    string? DarId = null);

public record Customer(
    Guid Id, string Name, string CprCvr, string ContactType, string Status,
    Address? BillingAddress = null);

public record Payer(
    Guid Id, string Name, string CprCvr, string ContactType,
    string? Email, string? Phone,
    Address? BillingAddress = null);

public record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize)
{
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
}

public record DashboardStats(int PendingSignups, int ActiveCustomers, int RejectedSignups, int ProductCount);

public record MeteringPoint(
    string Gsrn, string Type, string SettlementMethod,
    string GridAreaCode, string GridOperatorGln, string PriceArea, string ConnectionStatus);

public record Contract(
    Guid Id, Guid CustomerId, string Gsrn, Guid ProductId,
    string BillingFrequency, string PaymentModel, DateOnly StartDate,
    Guid? PayerId = null);

public record SupplyPeriod(Guid Id, string Gsrn, DateOnly StartDate, DateOnly? EndDate);

public record Product(
    Guid Id, string Name, string EnergyModel,
    decimal MarginOrePerKwh, decimal? SupplementOrePerKwh,
    decimal SubscriptionKrPerMonth,
    string? Description = null, bool GreenEnergy = false, int DisplayOrder = 0);

public record MeteringPointWithSupply(
    string Gsrn, string Type, string SettlementMethod,
    string GridAreaCode, string PriceArea, string ConnectionStatus,
    DateOnly? SupplyStart, DateOnly? SupplyEnd);
