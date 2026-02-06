namespace DataHub.Settlement.Domain.MasterData;

public record ParsedMasterData(
    string MessageId,
    string MeteringPointId,
    string Type,
    string SettlementMethod,
    string GridAreaCode,
    string GridOperatorGln,
    string PriceArea,
    DateTimeOffset SupplyStart);
