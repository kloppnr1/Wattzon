using DataHub.Settlement.Application.AddressLookup;

namespace DataHub.Settlement.Infrastructure.AddressLookup;

public sealed class StubAddressLookupClient : IAddressLookupClient
{
    private static int _counter;

    public Task<AddressLookupResult> LookupByDarIdAsync(string darId, CancellationToken ct)
    {
        var seq = Interlocked.Increment(ref _counter);
        var gsrn = $"57131310{seq:D10}";
        var mp = new MeteringPointInfo(gsrn, "E17", "344");
        return Task.FromResult(new AddressLookupResult([mp]));
    }
}
