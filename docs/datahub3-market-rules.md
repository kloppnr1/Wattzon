# DataHub 3: Market Rules Validation

Rules enforced by DataHub to prevent invalid operations. Our system validates these both in the application layer (`MarketRules`) and in the Simulator (`SimulatorState`).

---

## 1. BRS-001 — Change of Supplier

### Pre-conditions

| Check | DataHub Error | Description |
|-------|---------------|-------------|
| No active supply by same supplier | E16 | Supplier already holds the metering point |
| No pending/in-progress process | E16 | Conflicting process exists for this GSRN |

### Our enforcement

- **Application**: `MarketRules.CanChangeSupplierAsync` queries `portfolio.supply_period` (active) and `lifecycle.process_request` (non-terminal status)
- **Simulator**: `SimulatorState.IsGsrnActive` checks in-memory GSRN registry; rejects with `Accepted: false, RejectReason: "E16"`
- **Database**: Unique partial index `idx_supply_period_one_active` on `portfolio.supply_period (gsrn) WHERE end_date IS NULL`

---

## 2. RSM-012 — Receive Metering Data

### Pre-conditions

| Check | Description |
|-------|-------------|
| Active supply period exists | `end_date IS NULL` in `portfolio.supply_period` |
| Metering point is activated | `activated_at IS NOT NULL AND deactivated_at IS NULL` |

### Our enforcement

- **Application**: `MarketRules.CanReceiveMeteringAsync` checks both conditions
- **DataHub behavior**: DataHub only sends RSM-012 to the current supplier; we won't receive it if we don't hold the metering point

---

## 3. Settlement

### Pre-conditions

| Check | Description |
|-------|-------------|
| Active contract exists | `end_date IS NULL` in `portfolio.contract` |
| Unsettled metering data exists | Data beyond the last settled `period_end` |

### Our enforcement

- **Application**: `MarketRules.CanRunSettlementAsync` checks for active contract and unsettled data
- Prevents running settlement on already-settled periods or metering points without contracts

---

## 4. BRS-002 — Offboard (End of Supply)

### Pre-conditions

| Check | Description |
|-------|-------------|
| Active supply period | Must currently supply this GSRN |
| Completed process exists | Must have a process in `completed` state to transition to offboarding |

### Our enforcement

- **Application**: `MarketRules.CanOffboardAsync` verifies both conditions
- **Simulator**: `SimulatorState.IsGsrnActive` check on `/v1.0/cim/requestendofsupply`; deactivates GSRN on success

---

## 5. Aconto Billing

### Pre-conditions

| Check | Description |
|-------|-------------|
| Active supply period | Must currently supply this GSRN |
| Active contract | Must have billing relationship |

### Our enforcement

- **Application**: `MarketRules.CanBillAcontoAsync` checks both `supply_period` and `contract`

---

## Simulator GSRN Tracking

The Simulator maintains an in-memory `ConcurrentDictionary<string, string>` tracking active GSRNs:

- **Activate**: On successful BRS-001 (change of supplier) or via `/admin/activate/{gsrn}`
- **Deactivate**: On successful BRS-002 (end of supply) or via `/admin/deactivate/{gsrn}`
- **Scenario load**: Scenarios with RSM-007 (`sunshine`, `full_lifecycle`, `cancellation`) auto-register the default GSRN
- **Reset**: `/admin/reset` clears all GSRN tracking

---

## Cross-references

- [Customer lifecycle](datahub3-customer-lifecycle.md) — state machine transitions
- [Edge cases](datahub3-edge-cases.md) — correction handling, concurrent processes
- [Settlement overview](datahub3-settlement-overview.md) — billing periods and settlement engine
