namespace DataHub.Settlement.Application.Settlement;

public interface ISettlementEngine
{
    SettlementResult Calculate(SettlementRequest request);
}
