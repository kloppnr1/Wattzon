# DataHub 3: Edge Cases and Error Handling

All edge cases and special scenarios collected in one place. Covers metering data corrections, erroneous processes, reconciliation discrepancies, customer disputes, concurrent processes, electrical heating, and solar panel production.

Standard lifecycle processes (cancellations, final settlement at offboarding) are documented in [Customer lifecycle](datahub3-customer-lifecycle.md). System errors and recovery procedures are documented in [System architecture](datahub3-proposed-architecture.md#error-handling-and-recovery).

---

## 1. Metering Data Corrections

The most important edge case in the system. The grid company (netvirksomheden) can submit **corrected measurements** for an already settled period.

### How corrections arrive

- We receive a new RSM-012 for the same metering point + period via the Timeseries queue
- There is **no explicit flag** indicating "this is a correction"
- We must compare against what we have already stored ourselves
- Any RSM-012 can potentially be a correction

### Detection logic

```
1. Receive RSM-012 -> parse MeteringPointId + period + Point[]
2. Look up metering_data for same MeteringPointId + time interval
3. If NO existing data -> initial data (normal ingestion)
4. If existing data EXISTS -> this is a correction:
   a. Calculate delta per interval: new_quantity - old_quantity
   b. Calculate financial impact (see correction formulas)
   c. Overwrite metering data with new values
   d. Generate credit/debit note
5. Dequeue the message
```

### Correction formulas

| Component | Formula |
|-----------|---------|
| **Energy** | `deltaKwh × calculatedPrice` |
| **Tariff** | `originalKwh × (newRate - oldRate) + deltaKwh × newRate` |
| **Product margin** | `deltaKwh × productRate` |
| **Subscription** | Unchanged (fixed amount, does not depend on consumption) |
| **Electricity tax (elafgift)** | `deltaKwh × taxRate` |
| **VAT (moms)** | 25% of the sum of all changes |

### Possible causes of corrections

| Cause | Typical timing | Frequency |
|-------|---------------|-----------|
| Meter error corrected | Weeks/months after original reading | Rare |
| Estimated data replaced with actual | Days after original | Common |
| Grid company corrects validation error | Days-weeks | Occasional |
| Quality code upgrade (A02->A03) | Days | Common |

### System design implications

- **Idempotent update:** Overwrite logic must handle the same correction arriving multiple times
- **History:** Preserve the original reading in an audit log before overwriting
- **Recalculation:** Settlement engine must be able to recalculate for arbitrary historical periods
- **Time limit:** Corrections can arrive up to 3 years after the original reading (WARNING: VERIFY)

---

## 2. Erroneous Processes

### 2.1 Erroneous supplier switch (leverandørskifte) (BRS-042)

A supplier switch has occurred by mistake — e.g., wrong metering point or the customer has not accepted.

**Flow:**

| Step | Direction | Action |
|------|-----------|--------|
| 1 | DDQ -> DataHub | **BRS-042** reversal request |
| 2 | DataHub | Validates request, reverses the switch |
| 3 | DataHub -> old DDQ | Old supplier is reinstated |
| 4 | Internal | All metering data for the erroneous period is reversed |
| 5 | Internal | Issued invoices for the period are credited |

**Deadline:** Within 20 business days after the effective date (WARNING: VERIFY, cf. Regulation H1 (Forskrift H1))

**Consequences for the system:**
- Metering point switches back to old supplier -> supply_period must be corrected
- All settlement results for the period must be marked as invalid
- Credit notes are generated for any issued invoices
- Received metering data for the period is deleted or marked as reversed

### 2.2 Erroneous move (BRS-011)

A move-in or move-out date was incorrect.

**Flow:**

| Step | Direction | Action |
|------|-----------|--------|
| 1 | DDQ -> DataHub | **BRS-011** with the corrected date |
| 2 | DataHub | Adjusts the supply period (leveranceperioden) |
| 3 | DataHub -> DDQ | Updated metering data for the affected period (RSM-012) |
| 4 | Internal | Recalculate settlement for the affected period |
| 5 | Internal | Issue credit/debit notes for differences |

**Consequences for the system:**
- The supply period's start or end date changes
- Metering data for the affected period may change
- Subscription calculation (pro rata) is adjusted
- Aconto settlement (acontoopgørelse) may need to be recalculated

---

## 3. Reconciliation Discrepancies (BRS-027)

Our own settlement calculation deviates from DataHub's wholesale settlement (engrosopgørelse) (RSM-014).

### Discrepancy procedure

```
1. Receive RSM-014 (aggregated data per grid area)
2. Compare with own settlement for the same period and grid area
3. If discrepancy:
   a. Identify deviating metering points
   b. Request detailed aggregated data (RSM-016)
   c. Analyze cause:
      - Missing metering data?
      - Incorrect tariff rates?
      - Calculation error?
4. Correct:
   - Missing data -> request historical data (RSM-015)
   - Incorrect rates -> update and recalculate
   - Calculation error -> fix and recalculate
5. Issue credit/debit notes for affected customers
```

### Typical causes of discrepancies

| Cause | Action |
|-------|--------|
| Missing RSM-012 for one or more metering points | Request historical data via RSM-015, recalculate |
| Incorrect/outdated tariff rates used | Update rates from Charges queue, recalculate |
| Correction received after our settlement run | Recalculate with corrected data |
| Timezone/rounding difference | Adjust calculation logic |
| Metering point missing from our portfolio | Investigate — possible error in BRS-001 flow |

---

## 4. Customer Disputes Invoice

### Procedure

| Step | Action |
|------|--------|
| 1 | Customer contacts support with an objection |
| 2 | Request historical validated data from DataHub (RSM-015) for verification |
| 3 | Request aggregated data (RSM-016) for cross-checking |
| 4 | Compare our settlement with DataHub data |
| 5a | If metering data was incorrect -> grid company submits correction (BRS-021) -> new RSM-012 -> correction (see section 1) |
| 5b | If our calculation was incorrect -> recalculate and issue credit/debit note |
| 5c | If everything matches -> inform the customer with documentation |

---

## 5. Concurrent Processes

### Supplier switch and correction at the same time

A correction arrives for a period that overlaps with a supplier switch (leverandørskifte):
- The correction applies only to the period during which we were the supplier
- Filter correction data to our supply period (supply_period.start_date -> end_date)
- Ignore data outside our supply period

### Move-out and aconto settlement

The customer moves out mid-quarter:
- Final settlement is calculated for the partial period (quarter start -> move-out date)
- Aconto settlement (acontoopgørelse) calculates the difference against proportional aconto payments
- The final invoice is issued as a separate credit/debit note (not the normal combined quarterly invoice)

### Tariff change mid-billing period

The grid company (netvirksomheden) changes tariff rates effective mid-month:
- Settlement calculation must apply the old rate before the change date and the new rate after
- Tariff lookup uses `valid_from`/`valid_to` per hour, not per period
- Existing invoices are **not** affected unless a correction is received

---

## 6. Electrical Heating (Elvarme)

Customers with electrical heating as their primary heat source are eligible for a **reduced electricity tax (elafgift)** rate on consumption above a yearly threshold. This affects the settlement calculation.

### How it works

- The Danish electricity tax (elafgift) has two rates: a **standard rate** and a **reduced rate** for registered electrical heating customers (WARNING: VERIFY — as of 2025, the elafgift has been reduced significantly; confirm whether the elvarme distinction still produces a meaningful rate difference)
- The reduced rate applies to consumption **above 4,000 kWh/year** (WARNING: VERIFY current threshold)
- The customer's metering point must be registered as electrical heating eligible in DataHub

### Data flow

| Step | Source | Detail |
|------|--------|--------|
| 1 | RSM-007 (master data) | Contains a heating indicator or tax reduction flag for the metering point (WARNING: VERIFY exact field name in CIM format) |
| 2 | Our system | Stores the heating flag on `metering_point` |
| 3 | Settlement engine | Tracks cumulative annual kWh for the metering point |
| 4 | Settlement engine | Once the threshold is exceeded, applies the reduced rate to the remaining consumption |

### System design implications

- **Annual tracking:** The settlement engine must track cumulative consumption per metering point per calendar year to determine when the 4,000 kWh threshold is crossed
- **Threshold crossing mid-period:** If the threshold is crossed within a billing period, that period has a **split rate** — standard rate up to the threshold, reduced rate above
- **Year boundary:** The threshold resets on 1 January — the first billing period of the year always starts at the standard rate
- **Master data change:** If a customer installs or removes electrical heating, RSM-004 (master data change) updates the flag — settlement must apply the correct rate from the change date
- **Corrections:** If metering data is corrected for a period that crosses the threshold, the threshold calculation must be re-evaluated and the tax difference recalculated
- **Database:** Consider adding a `heating_type` column to `portfolio.metering_point` (e.g., `standard`, `electrical_heating`) and a yearly accumulator table or query

### Possible values (WARNING: VERIFY)

| Attribute | Values | Source |
|-----------|--------|--------|
| Heating type | Standard / Electrical heating (elvarme) | RSM-007 master data |
| Standard elafgift rate | ~0.008 DKK/kWh (2025) | Legislation |
| Reduced elafgift rate | ~0.005 DKK/kWh (2025) (WARNING: VERIFY) | Legislation |
| Yearly threshold | 4,000 kWh (WARNING: VERIFY) | Legislation |

---

## 7. Solar Panels and Production Metering Points

Customers with solar panels (solceller) have both a **consumption metering point (E17)** and a **production metering point (E18)**. This introduces net settlement (nettoafregning) schemes and production credit handling.

### How DataHub models solar customers

- The solar installation is represented as a **separate metering point** with type **E18** (production)
- The customer's main consumption point remains **E17**
- Both metering points are associated with the same customer and the same grid area
- We receive **RSM-012 data for both** metering points — consumption _and_ production

### Net settlement schemes (nettoafregningsgrupper)

Denmark has several net settlement schemes, depending on when the installation was registered (WARNING: VERIFY current scheme details):

| Scheme | Applies to | How it works |
|--------|-----------|-------------|
| **Hourly net settlement** (timeafregning) | Most new installations (post-2012) | Production is netted against consumption **per hour**. Excess production within an hour is credited at spot price. Consumption exceeding production is billed normally. |
| **Annual net settlement** (årsafregning) | Legacy installations (WARNING: VERIFY cutoff date) | Production is accumulated and netted against consumption over a full year. Far more favorable for the customer. |
| **Instant net settlement** | Very small installations (WARNING: VERIFY) | Production offsets consumption instantly — effectively the meter "runs backwards" |

The net settlement group is indicated in DataHub master data (WARNING: VERIFY exact field in RSM-007 or CIM format).

### Impact on settlement

**Hourly net settlement (most common):**

```
For each hour:
  net_consumption = consumption_kwh - production_kwh

  If net_consumption > 0:
    → Customer pays: net_consumption × (spot + margin + tariffs + tax)
    → Normal settlement calculation applies

  If net_consumption < 0 (excess production):
    → Customer is credited: |net_consumption| × spot price
    → No tariffs/tax on excess production credit (WARNING: VERIFY)
```

**Annual net settlement (legacy):**

```
Over the full year:
  net_consumption = total_consumption - total_production

  If net_consumption > 0:
    → Customer pays for net_consumption (settled annually)

  If net_consumption < 0:
    → Customer is credited for excess production
```

### System design implications

- **Paired metering points:** The system must link the E17 (consumption) and E18 (production) metering points for the same customer. Consider a `parent_gsrn` or `linked_gsrn` column on `portfolio.metering_point`
- **RSM-012 for E18:** Production data arrives via the same Timeseries queue as consumption data. The `MeteringPointType` field in the message distinguishes them. The metering data ingestion pipeline must handle both types
- **Net settlement group:** Store the customer's net settlement group (from RSM-007) to determine the correct settlement logic
- **Settlement engine branching:** The settlement engine must check for linked production metering points and apply the correct netting scheme before calculating invoice lines
- **Spot price credit:** Excess production is typically credited at the **spot price only** (no margin, no tariffs). This creates a new invoice line type
- **Grid tariff exemption:** Excess production fed into the grid is typically exempt from grid tariffs and electricity tax — only the net consumption is subject to these charges (WARNING: VERIFY)
- **Negative settlement lines:** The production credit results in a negative invoice line, reducing the total amount. Handle gracefully in invoice generation
- **Corrections:** If production or consumption data is corrected, the netting calculation for the entire period must be re-evaluated

### Database considerations

```sql
-- Suggested additions to portfolio.metering_point:
-- heating_type TEXT CHECK (heating_type IN ('standard', 'electrical_heating'))
-- net_settlement_group TEXT  -- e.g., 'hourly', 'annual', 'instant', NULL
-- linked_gsrn TEXT REFERENCES portfolio.metering_point(gsrn)  -- for E17↔E18 pairing
```

### Data flow for solar customer

```
DataHub                    Our system                  Settlement
  │                           │                            │
  │ RSM-012 (E17, consumption)│                            │
  ├──────────────────────────►│ Store in metering_data     │
  │                           │ (type = consumption)       │
  │ RSM-012 (E18, production) │                            │
  ├──────────────────────────►│ Store in metering_data     │
  │                           │ (type = production)        │
  │                           │                            │
  │                           │ At billing period end ────►│
  │                           │                            │ For each hour:
  │                           │                            │   net = E17 - E18
  │                           │                            │   if net > 0: charge
  │                           │                            │   if net < 0: credit
  │                           │                            │
  │                           │◄──── Settlement result ────│
  │                           │   Invoice with net amounts │
  │                           │   + production credit line │
```

---

## Deadline Overview

| Process | Deadline | Source |
|---------|----------|--------|
| BRS-001 supplier switch (notice period) | Min. 15 business days | Regulation H1 (Forskrift H1) |
| BRS-043 short notice | 1 business day (WARNING: VERIFY) | Regulation H1 (Forskrift H1) |
| BRS-003 cancel switch | Before the effective date | Regulation H1 (Forskrift H1) |
| BRS-042 reversal | 20 business days after effective date (WARNING: VERIFY) | Regulation H1 (Forskrift H1) |
| BRS-044 cancel termination | Before the effective date | Regulation H1 (Forskrift H1) |
| Final invoice at offboarding | 4 weeks after customer departure | Electricity Supply Order section 17 (Elleveringsbekendtgørelsen §17) |
| Customer data archiving | 5 years (WARNING: VERIFY) | GDPR / Danish Bookkeeping Act (bogføringsloven) |
| Metering data retention | 3+ years (WARNING: VERIFY) | Legal requirement |

---

## Sources

- [Customer lifecycle](datahub3-customer-lifecycle.md) — phases and offboarding flow
- [Sequence diagrams](datahub3-sequence-diagrams.md) — message flows for BRS/RSM
- [Business processes](datahub3-ddq-business-processes.md) — BRS/RSM reference
- [RSM-012 reference](rsm-012-datahub3-measure-data.md) — correction flow for metering data
- [Settlement overview](datahub3-settlement-overview.md) — corrections as edge case
- [System architecture](datahub3-proposed-architecture.md) — error handling and recovery procedures
- [Database model](datahub3-database-model.md) — dead_letter table, metering_point schema
- [Product structure and billing](datahub3-product-and-billing.md) — invoice lines and settlement calculation
