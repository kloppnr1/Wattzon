namespace DataHub.Settlement.Application.Onboarding;

public record Signup(
    Guid Id,
    string SignupNumber,
    string DarId,
    string Gsrn,
    Guid? CustomerId,
    Guid ProductId,
    Guid? ProcessRequestId,
    string Type,
    DateOnly EffectiveDate,
    string Status,
    string? RejectionReason,
    Guid? CorrectedFromId);

public record SignupStatusResponse(
    string SignupId,
    string Status,
    string Gsrn,
    DateOnly EffectiveDate,
    string? RejectionReason);

public record SignupRequest(
    string? DarId,
    string CustomerName,
    string CprCvr,
    string ContactType,
    string Email,
    string Phone,
    Guid ProductId,
    string Type,
    DateOnly EffectiveDate,
    string? Mobile = null,
    string? Gsrn = null,
    string? BillingFrequency = null,
    string? PaymentModel = null,
    Guid? CorrectedFromId = null,
    // Billing address (customer's postal address — distinct from supply point)
    string? BillingDarId = null,
    string? BillingStreet = null,
    string? BillingHouseNumber = null,
    string? BillingFloor = null,
    string? BillingDoor = null,
    string? BillingPostalCode = null,
    string? BillingCity = null,
    // Optional separate payer (if someone other than the customer pays)
    string? PayerName = null,
    string? PayerCprCvr = null,
    string? PayerContactType = null,
    string? PayerEmail = null,
    string? PayerPhone = null,
    string? PayerBillingStreet = null,
    string? PayerBillingHouseNumber = null,
    string? PayerBillingFloor = null,
    string? PayerBillingDoor = null,
    string? PayerBillingPostalCode = null,
    string? PayerBillingCity = null);

public record SignupResponse(
    Guid Id,
    string SignupId,
    string Status,
    string Gsrn,
    DateOnly EffectiveDate);

public record SignupListItem(
    Guid Id,
    string SignupNumber,
    string Gsrn,
    string Type,
    DateOnly EffectiveDate,
    string Status,
    string? RejectionReason,
    string CustomerName,
    DateTime CreatedAt);

public record SignupDetail(
    Guid Id,
    string SignupNumber,
    string DarId,
    string Gsrn,
    string Type,
    DateOnly EffectiveDate,
    string Status,
    string? RejectionReason,
    Guid? CustomerId,
    string CustomerName,
    string CprCvr,
    string ContactType,
    Guid ProductId,
    string ProductName,
    Guid? ProcessRequestId,
    string BillingFrequency,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    Guid? CorrectedFromId,
    string? CorrectedFromSignupNumber);

public record SignupCorrectionLink(
    Guid Id,
    string SignupNumber,
    string Status,
    DateTime CreatedAt);

/// <summary>Billing address and optional payer info captured at signup time.</summary>
public record SignupAddressInfo(
    string? BillingDarId,
    string? BillingStreet, string? BillingHouseNumber, string? BillingFloor,
    string? BillingDoor, string? BillingPostalCode, string? BillingCity,
    string? PayerName, string? PayerCprCvr, string? PayerContactType,
    string? PayerEmail, string? PayerPhone,
    string? PayerBillingStreet, string? PayerBillingHouseNumber, string? PayerBillingFloor,
    string? PayerBillingDoor, string? PayerBillingPostalCode, string? PayerBillingCity);
