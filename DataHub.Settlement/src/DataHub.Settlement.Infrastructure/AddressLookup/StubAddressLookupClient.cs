using DataHub.Settlement.Application.AddressLookup;

namespace DataHub.Settlement.Infrastructure.AddressLookup;

public sealed class StubAddressLookupClient : IAddressLookupClient
{
    public Task<AddressLookupResult> LookupByDarIdAsync(string darId, CancellationToken ct)
    {
        var mp = new MeteringPointInfo("571313100000012345", "E17", "344");
        return Task.FromResult(new AddressLookupResult([mp]));
    }
}
