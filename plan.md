# Plan: Fix Settlement Run Creation

## Problem Summary

`SettlementOrchestrationService.TrySettleAsync` has **four bugs**:

### Bug 1: Billing period hardcoded as `EffectiveDate + 1 month` (line 78)
```csharp
var periodEnd = periodStart.AddMonths(1);
```
RSM-012 delivers metered data as **whole single days** (24 hourly readings per message). Data accumulates in the DB day by day. The settlement period should be derived from the **actual received metering data range**, not an arbitrary 1-month window from the effective date.

**Fix**: Add `GetDataRangeAsync(gsrn, fromUtc)` to `IMeteringDataRepository`. It returns the actual `(periodStart, periodEnd)` boundaries of received data. Use that to define the settlement period — calculate for exactly the received data, nothing more.

### Bug 2: Grid area and price area hardcoded as `"344"` / `"DK1"` (line 108)
```csharp
var input = await _dataLoader.LoadAsync(process.Gsrn, "344", "DK1", ...);
```

**Fix**: Look up the metering point via existing `GetMeteringPointByGsrnAsync` and use its `GridAreaCode` / `PriceArea`.

### Bug 3: No idempotency — no status transition after settlement
The orchestration polls "completed" processes every 5 minutes. After settling, the process stays "completed", so it gets re-processed every tick, creating duplicate settlement runs.

**Fix**: After successful settlement, transition the process to "settled" via `UpdateStatusAsync`.

### Bug 4: No guard against duplicate settlement runs
Even with the status transition, a crash between storing the result and updating status could create duplicates.

**Fix**: Add `HasSettlementRunAsync(gsrn, periodStart, periodEnd)` to `ISettlementResultStore` as an idempotency pre-check.

---

## Changes

### 1. `IMeteringDataRepository` + `MeteringDataRepository` — add `GetDataRangeAsync`

**Interface** (`Application/Metering/IMeteringDataRepository.cs`):
```csharp
Task<(DateTime Start, DateTime End)?> GetDataRangeAsync(string meteringPointId, DateTime fromUtc, CancellationToken ct);
```

**Implementation** (`Infrastructure/Metering/MeteringDataRepository.cs`):
```sql
SELECT MIN(timestamp), MAX(timestamp) + INTERVAL '1 hour'
FROM metering.metering_data
WHERE metering_point_id = @MeteringPointId AND timestamp >= @From
```
Returns `null` if no data exists (RSM-012 hasn't arrived yet). The `+ 1 hour` converts the last data point timestamp to the exclusive end boundary (e.g., last reading at 23:00 → period ends at 00:00 next day).

### 2. `ISettlementResultStore` + `SettlementResultStore` — add `HasSettlementRunAsync`

**Interface** (`Application/Settlement/ISettlementResultStore.cs`):
```csharp
Task<bool> HasSettlementRunAsync(string gsrn, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct);
```

**Implementation** (`Infrastructure/Settlement/SettlementResultStore.cs`):
```sql
SELECT EXISTS (
    SELECT 1 FROM settlement.settlement_run sr
    JOIN settlement.billing_period bp ON bp.id = sr.billing_period_id
    WHERE sr.metering_point_id = @Gsrn
      AND bp.period_start = @PeriodStart
      AND bp.period_end = @PeriodEnd
)
```

### 3. `SettlementOrchestrationService.TrySettleAsync` — fix all four bugs

Rewritten flow:

1. Skip if no `EffectiveDate`
2. **Derive period from actual data**: Call `GetDataRangeAsync(gsrn, effectiveDate)`. Return early if `null` (no data yet).
3. Convert range to `DateOnly periodStart` / `DateOnly periodEnd`
4. **Completeness check** for the data-derived period (validates no gaps in daily data)
5. **Idempotency check**: Call `HasSettlementRunAsync`. If already exists, mark process "settled" and return.
6. **Lookup metering point**: Call `GetMeteringPointByGsrnAsync` for real `GridAreaCode` / `PriceArea`. Warn and return if not found.
7. Load data, calculate, store result — using real grid area
8. **Transition process** to "settled" to prevent re-processing

### 4. Add `IMeteringDataRepository` to orchestration service

The service currently doesn't have `IMeteringDataRepository` injected. Add it to the constructor so we can call `GetDataRangeAsync`.

---

## Files Modified

| File | Change |
|------|--------|
| `Application/Metering/IMeteringDataRepository.cs` | Add `GetDataRangeAsync` |
| `Infrastructure/Metering/MeteringDataRepository.cs` | Implement `GetDataRangeAsync` |
| `Application/Settlement/ISettlementResultStore.cs` | Add `HasSettlementRunAsync` |
| `Infrastructure/Settlement/SettlementResultStore.cs` | Implement `HasSettlementRunAsync` |
| `Infrastructure/Settlement/SettlementOrchestrationService.cs` | Fix all four bugs, add `IMeteringDataRepository` dependency |

No new files. No schema changes (all queries use existing tables/columns).
