namespace DataHub.Settlement.Application.Settlement;

public sealed class ErroneousSwitchService
{
    private readonly ISettlementEngine _engine;

    public ErroneousSwitchService(ISettlementEngine engine)
    {
        _engine = engine;
    }

    public ErroneousSwitchResult CalculateReversal(IReadOnlyList<SettlementRequest> erroneousPeriodRequests)
    {
        var creditNotes = new List<SettlementResult>();
        decimal totalCredited = 0m;

        foreach (var request in erroneousPeriodRequests)
        {
            var result = _engine.Calculate(request);
            creditNotes.Add(result);
            totalCredited += result.Total;
        }

        return new ErroneousSwitchResult(creditNotes, totalCredited);
    }
}

public record ErroneousSwitchResult(
    IReadOnlyList<SettlementResult> CreditNotes,
    decimal TotalCredited);
