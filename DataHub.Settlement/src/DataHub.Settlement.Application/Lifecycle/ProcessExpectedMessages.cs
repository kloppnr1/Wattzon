using DataHub.Settlement.Application.DataHub;

namespace DataHub.Settlement.Application.Lifecycle;

/// <summary>
/// Defines the expected inbound messages for each process type.
/// Single source of truth — extend this dictionary when adding new process types.
/// </summary>
public static class ProcessExpectedMessages
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> ExpectedByType =
        new Dictionary<string, IReadOnlyList<string>>
        {
            [ProcessTypes.SupplierSwitch] = new[] { RsmMessageTypes.Request, RsmMessageTypes.CustomerData, RsmMessageTypes.PriceAttachments, RsmMessageTypes.MasterData },
            [ProcessTypes.MoveIn]         = new[] { RsmMessageTypes.Request, RsmMessageTypes.CustomerData, RsmMessageTypes.PriceAttachments, RsmMessageTypes.MasterData },
            [ProcessTypes.EndOfSupply]    = new[] { RsmMessageTypes.EndOfSupply },
            [ProcessTypes.MoveOut]        = new[] { RsmMessageTypes.EndOfSupply },
        };

    public static IReadOnlyList<string> For(string processType)
        => ExpectedByType.TryGetValue(processType, out var list) ? list : Array.Empty<string>();
}
