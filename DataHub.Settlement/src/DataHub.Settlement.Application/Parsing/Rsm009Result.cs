namespace DataHub.Settlement.Application.Parsing;

public record Rsm001ResponseResult(
    string CorrelationId,
    bool Accepted,
    string? RejectionReason,
    string? RejectionCode);
