using DataHub.Settlement.Application.AddressLookup;

namespace DataHub.Settlement.UnitTests;

public sealed class FakeAddressLookupClient : IAddressLookupClient
{
    private readonly Dictionary<string, List<MeteringPointInfo>> _data = new();

    public void Register(string darId, params MeteringPointInfo[] meteringPoints)
    {
        _data[darId] = meteringPoints.ToList();
    }

    public Task<AddressLookupResult> LookupByDarIdAsync(string darId, CancellationToken ct)
    {
        var points = _data.TryGetValue(darId, out var list) ? list : [];
        return Task.FromResult(new AddressLookupResult(points));
    }
}
