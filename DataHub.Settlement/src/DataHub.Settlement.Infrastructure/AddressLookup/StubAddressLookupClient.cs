using DataHub.Settlement.Application.AddressLookup;

namespace DataHub.Settlement.Infrastructure.AddressLookup;

public sealed class StubAddressLookupClient : IAddressLookupClient
{
    public Task<AddressLookupResult> LookupByDarIdAsync(string darId, CancellationToken ct)
    {
        // Derive a stable GSRN from the darId so repeated lookups return the same result
        var hash = (uint)darId.GetHashCode(StringComparison.Ordinal);
        var seq = hash % 1_000_000_000;
        var gsrn = $"57131310{seq:D10}";
        var mp = new MeteringPointInfo(gsrn, "E17", "344");
        return Task.FromResult(new AddressLookupResult([mp]));
    }
}
