namespace DataHub.Settlement.Application.DataHub;

/// <summary>
/// Official Energinet business reason codes per BRS process.
/// See https://energinet.atlassian.net/wiki/spaces/DHDOCS/pages/152961170
/// </summary>
public static class BusinessReasonCodes
{
    /// <summary>E03 — Leverandørskift (Supplier switch). Used by BRS-001.</summary>
    public const string SupplierSwitch = "E03";

    /// <summary>E20 — Leveranceophør (End of supply). Used by BRS-002 and BRS-044.</summary>
    public const string EndOfSupply = "E20";

    /// <summary>E65 — Tilflytning (Move-in). Used by BRS-009.</summary>
    public const string MoveIn = "E65";

    /// <summary>E66 — Fraflytning (Move-out). Used by BRS-010.</summary>
    public const string MoveOut = "E66";

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

    /// <summary>RSM-027 — Customer data update (outbound post-switch).</summary>
    public const string CustomerDataUpdate = "RSM-027";

    /// <summary>RSM-028 — Kundedata (customer master data).</summary>
    public const string CustomerData = "RSM-028";

    /// <summary>RSM-031 — Prisbilag (price/tariff attachments).</summary>
    public const string PriceAttachments = "RSM-031";
}

/// <summary>
/// RSM-004 reason codes for different notification types.
/// </summary>
public static class Rsm004ReasonCodes
{
    /// <summary>D11 — Auto-cancellation (customer data deadline exceeded).</summary>
    public const string AutoCancel = "D11";

    /// <summary>D31 — Forced transfer (metering point override, BRS-044).</summary>
    public const string ForcedTransfer = "D31";

    /// <summary>D34 — Correction accepted (BRS-003 acceptance).</summary>
    public const string CorrectionAccepted = "D34";

    /// <summary>D35 — Correction rejected (BRS-003 rejection).</summary>
    public const string CorrectionRejected = "D35";

    /// <summary>D46 — Special rules for start of supply.</summary>
    public const string SpecialRules = "D46";

    /// <summary>E01 — Stop of supply (another supplier taking over).</summary>
    public const string StopOfSupplyByOtherSupplier = "E01";

    /// <summary>E03 — Stop of supply notification.</summary>
    public const string StopOfSupply = "E03";

    /// <summary>E20 — End of supply stop notification.</summary>
    public const string EndOfSupplyStop = "E20";
}
