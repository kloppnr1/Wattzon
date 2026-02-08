namespace DataHub.Settlement.Application.Parsing;

public record Rsm009Result(
    string CorrelationId,
    bool Accepted,
    string? RejectionReason,
    string? RejectionCode);
