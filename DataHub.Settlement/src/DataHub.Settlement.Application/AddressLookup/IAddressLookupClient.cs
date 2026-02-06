namespace DataHub.Settlement.Application.AddressLookup;

public interface IAddressLookupClient
{
    Task<AddressLookupResult> LookupByDarIdAsync(string darId, CancellationToken ct);
}

public record AddressLookupResult(IReadOnlyList<MeteringPointInfo> MeteringPoints);

public record MeteringPointInfo(string Gsrn, string Type, string GridAreaCode);
