namespace DataHub.Settlement.Application.Onboarding;

public record Signup(
    Guid Id,
    string SignupNumber,
    string DarId,
    string Gsrn,
    Guid CustomerId,
    Guid ProductId,
    Guid? ProcessRequestId,
    string Type,
    DateOnly EffectiveDate,
    string Status,
    string? RejectionReason);

public record SignupStatusResponse(
    string SignupId,
    string Status,
    string Gsrn,
    DateOnly EffectiveDate,
    string? RejectionReason);

public record SignupRequest(
    string DarId,
    string CustomerName,
    string CprCvr,
    string ContactType,
    string Email,
    string Phone,
    Guid ProductId,
    string Type,
    DateOnly EffectiveDate);

public record SignupResponse(
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
    Guid CustomerId,
    string CustomerName,
    string CprCvr,
    string ContactType,
    Guid ProductId,
    string ProductName,
    Guid? ProcessRequestId,
    DateTime CreatedAt,
    DateTime UpdatedAt);
