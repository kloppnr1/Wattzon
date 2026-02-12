namespace DataHub.Settlement.Infrastructure.Dashboard;

/// <summary>
/// Builds example CIM JSON messages for the Operations page.
/// NOTE: This is a stub implementation. Full message audit functionality is planned for V021 migration.
/// </summary>
public static class CimMessageBuilder
{
    /// <summary>
    /// Builds an example CIM message for a given step name.
    /// </summary>
    /// <param name="stepName">The simulation step name (e.g., "BRS-001 Request", "RSM-012 Metering")</param>
    /// <param name="gsrn">The GSRN (metering point ID)</param>
    /// <param name="effectiveDate">The effective date for the process</param>
    /// <returns>Example CIM JSON message, or null if no example available</returns>
    public static string? BuildExampleMessage(string stepName, string gsrn, DateOnly effectiveDate)
    {
        // Returns example CIM messages for known step types; null for unsupported types
        return stepName switch
        {
            "BRS-001 Request" => BuildBrs001Example(gsrn, effectiveDate),
            "RSM-012 Metering" => BuildRsm012Example(gsrn),
            _ => null
        };
    }

    private static string BuildBrs001Example(string gsrn, DateOnly effectiveDate)
    {
        return $$"""
        {
          "mRID": "{{Guid.NewGuid()}}",
          "type": "E65",
          "process.processType": "E65",
          "businessSector.type": "23",
          "sender_MarketParticipant.mRID": "5790001234567",
          "receiver_MarketParticipant.mRID": "5790000432752",
          "createdDateTime": "{{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}}",
          "Series": [{
            "mRID": "1",
            "marketEvaluationPoint.mRID": "{{gsrn}}",
            "start_DateAndOrTime.dateTime": "{{effectiveDate:yyyy-MM-dd}}"
          }]
        }
        """;
    }

    private static string BuildRsm012Example(string gsrn)
    {
        return $$"""
        {
          "mRID": "{{Guid.NewGuid()}}",
          "type": "E66",
          "process.processType": "E23",
          "businessSector.type": "23",
          "sender_MarketParticipant.mRID": "5790000432752",
          "receiver_MarketParticipant.mRID": "5790001234567",
          "createdDateTime": "{{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}}",
          "Series": [{
            "mRID": "1",
            "marketEvaluationPoint.mRID": "{{gsrn}}",
            "Period": [{
              "resolution": "PT1H",
              "timeInterval": {
                "start": "{{DateTime.UtcNow.Date:yyyy-MM-ddTHH:mm:ssZ}}",
                "end": "{{DateTime.UtcNow.Date.AddDays(1):yyyy-MM-ddTHH:mm:ssZ}}"
              },
              "Point": [
                { "position": 1, "quantity": 0.523 },
                { "position": 2, "quantity": 0.481 }
              ]
            }]
          }]
        }
        """;
    }
}
