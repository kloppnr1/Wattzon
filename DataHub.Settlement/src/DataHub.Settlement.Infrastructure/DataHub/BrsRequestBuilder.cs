using System.Text.Json;
using DataHub.Settlement.Application.DataHub;

namespace DataHub.Settlement.Infrastructure.DataHub;

public sealed class BrsRequestBuilder : IBrsRequestBuilder
{
    private const string OurGln = "5790002000000";
    private const string DataHubGln = "5790001330552";

    public string BuildBrs001(string gsrn, string cprCvr, DateOnly effectiveDate)
    {
        return BuildChangeOfSupplier(gsrn, cprCvr, effectiveDate, BusinessReasonCodes.SupplierSwitch);
    }

    public string BuildBrs002(string gsrn, DateOnly effectiveDate)
    {
        return BuildEndOfSupply(gsrn, effectiveDate, BusinessReasonCodes.EndOfSupply);
    }

    public string BuildBrs003(string gsrn, string originalCorrelationId)
    {
        return BuildCancelRequest(gsrn, originalCorrelationId, BusinessReasonCodes.SupplierSwitch);
    }

    public string BuildBrs009(string gsrn, string cprCvr, DateOnly effectiveDate)
    {
        return BuildChangeOfSupplier(gsrn, cprCvr, effectiveDate, BusinessReasonCodes.MoveIn);
    }

    public string BuildBrs010(string gsrn, DateOnly effectiveDate)
    {
        return BuildEndOfSupply(gsrn, effectiveDate, BusinessReasonCodes.MoveOut);
    }

    public string BuildBrs043(string gsrn, string cprCvr, DateOnly effectiveDate)
    {
        return BuildChangeOfSupplier(gsrn, cprCvr, effectiveDate, BusinessReasonCodes.SupplierSwitch);
    }

    public string BuildBrs044(string gsrn, string originalCorrelationId)
    {
        return BuildCancelRequest(gsrn, originalCorrelationId, BusinessReasonCodes.EndOfSupply);
    }

    public string BuildBrs042(string gsrn, DateOnly effectiveDate)
    {
        return BuildEndOfSupply(gsrn, effectiveDate, BusinessReasonCodes.ForcedSwitch);
    }

    private static string BuildChangeOfSupplier(string gsrn, string cprCvr, DateOnly effectiveDate, string processType)
    {
        var doc = new
        {
            RequestChangeOfSupplier_MarketDocument = new
            {
                mRID = Guid.NewGuid().ToString(),
                process = new { processType = new { value = processType } },
                sender_MarketParticipant = new
                {
                    mRID = new { value = OurGln, codingScheme = "A10" },
                    marketRole = new { type = new { value = "DDQ" } },
                },
                receiver_MarketParticipant = new
                {
                    mRID = new { value = DataHubGln, codingScheme = "A10" },
                    marketRole = new { type = new { value = "DGL" } },
                },
                createdDateTime = DateTime.UtcNow.ToString("O"),
                MktActivityRecord = new
                {
                    mRID = Guid.NewGuid().ToString(),
                    marketEvaluationPoint = new { mRID = new { value = gsrn, codingScheme = "A10" } },
                    start_DateAndOrTime = new { dateTime = effectiveDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).ToString("O") },
                    customer_MarketParticipant = new { mRID = new { value = cprCvr } },
                },
            },
        };

        return JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = false });
    }

    private static string BuildEndOfSupply(string gsrn, DateOnly effectiveDate, string processType)
    {
        var doc = new
        {
            RequestEndOfSupply_MarketDocument = new
            {
                mRID = Guid.NewGuid().ToString(),
                process = new { processType = new { value = processType } },
                sender_MarketParticipant = new
                {
                    mRID = new { value = OurGln, codingScheme = "A10" },
                    marketRole = new { type = new { value = "DDQ" } },
                },
                receiver_MarketParticipant = new
                {
                    mRID = new { value = DataHubGln, codingScheme = "A10" },
                    marketRole = new { type = new { value = "DGL" } },
                },
                createdDateTime = DateTime.UtcNow.ToString("O"),
                MktActivityRecord = new
                {
                    mRID = Guid.NewGuid().ToString(),
                    marketEvaluationPoint = new { mRID = new { value = gsrn, codingScheme = "A10" } },
                    end_DateAndOrTime = new { dateTime = effectiveDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).ToString("O") },
                },
            },
        };

        return JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = false });
    }

    private static string BuildCancelRequest(string gsrn, string originalCorrelationId, string processType)
    {
        var doc = new
        {
            RequestCancelChangeOfSupplier_MarketDocument = new
            {
                mRID = Guid.NewGuid().ToString(),
                process = new { processType = new { value = processType } },
                sender_MarketParticipant = new
                {
                    mRID = new { value = OurGln, codingScheme = "A10" },
                    marketRole = new { type = new { value = "DDQ" } },
                },
                receiver_MarketParticipant = new
                {
                    mRID = new { value = DataHubGln, codingScheme = "A10" },
                    marketRole = new { type = new { value = "DGL" } },
                },
                createdDateTime = DateTime.UtcNow.ToString("O"),
                MktActivityRecord = new
                {
                    mRID = Guid.NewGuid().ToString(),
                    marketEvaluationPoint = new { mRID = new { value = gsrn, codingScheme = "A10" } },
                    originalTransactionID = originalCorrelationId,
                },
            },
        };

        return JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = false });
    }
}
