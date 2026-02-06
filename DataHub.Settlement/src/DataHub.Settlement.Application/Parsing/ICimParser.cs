using DataHub.Settlement.Domain.MasterData;
using DataHub.Settlement.Domain.Metering;

namespace DataHub.Settlement.Application.Parsing;

public interface ICimParser
{
    IReadOnlyList<ParsedTimeSeries> ParseRsm012(string json);
    ParsedMasterData ParseRsm007(string json);
}
