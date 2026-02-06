namespace DataHub.Settlement.Application.DataHub;

public interface IBrsRequestBuilder
{
    string BuildBrs001(string gsrn, string cprCvr, DateOnly effectiveDate);
}
