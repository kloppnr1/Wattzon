using System.Text.Json;
using DataHub.Settlement.Application.DataHub;

namespace DataHub.Settlement.Infrastructure.DataHub;

public sealed class Brs001RequestBuilder : IBrsRequestBuilder
{
    private const string OurGln = "5790002000000";
    private const string DataHubGln = "5790001330552";

    public string BuildBrs001(string gsrn, string cprCvr, DateOnly effectiveDate)
    {
        var doc = new
        {
            RequestChangeOfSupplier_MarketDocument = new
            {
                mRID = Guid.NewGuid().ToString(),
                process = new { processType = new { value = "E65" } },
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
}
