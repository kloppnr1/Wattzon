using DataHub.Settlement.Application.Settlement;
using DataHub.Settlement.Domain.MasterData;
using DataHub.Settlement.Domain.Metering;

namespace DataHub.Settlement.Application.Parsing;

public interface ICimParser
{
    IReadOnlyList<ParsedTimeSeries> ParseRsm012(string json);
    ParsedMasterData ParseRsm022(string json);
    Rsm004Result ParseRsm004(string json);
    Rsm014Aggregation ParseRsm014(string json);
    Rsm001ResponseResult ParseRsm001Response(string json);
    Rsm001ResponseResult ParseRsm005Response(string json);
    Rsm001ResponseResult ParseRsm024Response(string json);
    Rsm028Result ParseRsm028(string json);
    Rsm031Result ParseRsm031(string json);
    GridTariffResult ParseGridTariff(string json);
}
