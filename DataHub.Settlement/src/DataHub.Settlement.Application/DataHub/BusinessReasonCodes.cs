namespace DataHub.Settlement.Application.DataHub;

/// <summary>
/// Official Energinet business reason codes per BRS process.
/// See https://energinet.atlassian.net/wiki/spaces/DHDOCS/pages/152961170
/// </summary>
public static class BusinessReasonCodes
{
    /// <summary>E03 — Leverandørskift (Supplier switch). Used by BRS-001 and BRS-043.</summary>
    public const string SupplierSwitch = "E03";

    /// <summary>E20 — Leveranceophør (End of supply). Used by BRS-002 and BRS-044.</summary>
    public const string EndOfSupply = "E20";

    /// <summary>E65 — Tilflytning (Move-in). Used by BRS-009.</summary>
    public const string MoveIn = "E65";

    /// <summary>E66 — Fraflytning (Move-out). Used by BRS-010.</summary>
    public const string MoveOut = "E66";

    /// <summary>E34 — Tvunget leverandørskift (Forced supplier switch). Used by BRS-042.</summary>
    public const string ForcedSwitch = "E34";

    /// <summary>D07 — Fejlagtigt leverandørskift (Erroneous switch). Used by BRS-003.</summary>
    public const string ErroneousSwitch = "D07";
}

/// <summary>
/// Official Energinet RSM message type identifiers.
/// </summary>
public static class RsmMessageTypes
{
    /// <summary>RSM-001 — Request/response for change of supplier or move-in (BRS-001, BRS-009).</summary>
    public const string Request = "RSM-001";

    /// <summary>RSM-004 — Generic notification (grid area changes, stop of supply info).</summary>
    public const string Notification = "RSM-004";

    /// <summary>RSM-005 — End of supply / move-out request (BRS-002, BRS-010).</summary>
    public const string EndOfSupply = "RSM-005";

    /// <summary>RSM-012 — Metering data (time series).</summary>
    public const string MeteringData = "RSM-012";

    /// <summary>RSM-014 — Aggregation data.</summary>
    public const string Aggregation = "RSM-014";

    /// <summary>RSM-022 — Information om målepunktsstamdata (master data).</summary>
    public const string MasterData = "RSM-022";

    /// <summary>RSM-024 — Annullering (cancellation request).</summary>
    public const string Cancellation = "RSM-024";
}
