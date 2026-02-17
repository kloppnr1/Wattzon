namespace DataHub.Settlement.Application.Lifecycle;

public record ProcessRequest(
    Guid Id,
    string ProcessType,
    string Gsrn,
    string Status,
    DateOnly? EffectiveDate,
    string? DatahubCorrelationId,
    bool CustomerDataReceived = false,
    bool TariffDataReceived = false);

public record ProcessEvent(
    Guid Id,
    Guid ProcessRequestId,
    DateTime OccurredAt,
    string EventType,
    string? Payload,
    string? Source);

public record ExpectedMessageItem(string MessageType, bool Received, DateTime? ReceivedAt, string? Status);

public record ProcessListItem(
    Guid Id, string ProcessType, string Gsrn, string Status,
    DateOnly? EffectiveDate, string? DatahubCorrelationId,
    DateTime CreatedAt, string? CustomerName, Guid? CustomerId);

public record ProcessDetail(
    Guid Id, string ProcessType, string Gsrn, string Status,
    DateOnly? EffectiveDate, string? DatahubCorrelationId,
    bool CustomerDataReceived, bool TariffDataReceived,
    DateTime CreatedAt, DateTime UpdatedAt,
    IReadOnlyList<ExpectedMessageItem> ExpectedMessages);
