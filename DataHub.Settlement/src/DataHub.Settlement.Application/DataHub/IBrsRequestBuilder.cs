namespace DataHub.Settlement.Application.DataHub;

public interface IBrsRequestBuilder
{
    string BuildBrs001(string gsrn, string cprCvr, DateOnly effectiveDate);
    string BuildBrs002(string gsrn, DateOnly effectiveDate);
    string BuildBrs003(string gsrn, string originalCorrelationId);
    string BuildBrs009(string gsrn, string cprCvr, DateOnly effectiveDate);
    string BuildBrs010(string gsrn, DateOnly effectiveDate);
    string BuildBrs043(string gsrn, string cprCvr, DateOnly effectiveDate);
    string BuildBrs044(string gsrn, string originalCorrelationId);
    string BuildBrs042(string gsrn, DateOnly effectiveDate);
}
