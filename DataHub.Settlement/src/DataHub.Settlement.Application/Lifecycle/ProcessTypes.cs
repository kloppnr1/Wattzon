namespace DataHub.Settlement.Application.Lifecycle;

/// <summary>
/// Canonical process type identifiers matching the values stored in lifecycle.process_request.
/// Single source of truth for all process type strings used across scheduling, state transitions,
/// DataHub communication, and message routing.
/// </summary>
public static class ProcessTypes
{
    public const string SupplierSwitch = "supplier_switch";
    public const string MoveIn = "move_in";
    public const string EndOfSupply = "end_of_supply";
    public const string MoveOut = "move_out";
    public const string CancelSwitch = "cancel_switch";
    public const string CancelEndOfSupply = "cancel_end_of_supply";
    public const string CustomerDataUpdate = "customer_data_update";
}
