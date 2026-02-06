# Customer Lifecycle: Onboarding to Offboarding

End-to-end walkthrough of a typical residential customer from contract signing to move-out. Covers both DataHub communication and the internal billing process.

---

## Timeline Overview

Arrow direction: `->` = we send to DataHub, `<-` = we receive from DataHub.

```
PHASE 1: ONBOARDING                                             ~15 business days
─────────────────────────────────────────────────────────────────────────────
Trigger:      Contract signed — customer selects us as supplier
DataHub:      → BRS-001 (supplier switch / leverandørskifte) with GSRN + desired date + CPR/CVR
              → BRS-043 (short notice) or → BRS-009 (move-in / tilflytning)
              → BRS-015 (submit customer master data / kundestamdata)
              → BRS-003 (cancel if the customer withdraws)
Billing:      Create customer record, select product/tariff plan
              Set up billing schedule (monthly/quarterly)
              Calculate aconto estimate based on expected annual consumption
                                    │
                                    ▼
PHASE 2: ACTIVATION                                              ~1 day
─────────────────────────────────────────────────────────────────────────────
Trigger:      Effective date reached — we are now the supplier on the metering point
DataHub:      ← RSM-007 (master data: settlement method, grid area, GLN, type)
              ← RSM-012 (first metering data, possibly historical for the transition)
Billing:      Assign grid tariffs based on grid area + grid company GLN
              Load Nordpool spot price feed
              Activate metering point in the portfolio
                                    │
                                    ▼
PHASE 3: FIRST INVOICE                                           ~1 month
─────────────────────────────────────────────────────────────────────────────
Trigger:      First billing period completed
DataHub:      ← RSM-012 (ongoing hourly metering data — daily for flex)
              ← RSM-014 (aggregated data for reconciliation)
Billing:      Settlement run per hour for the entire period:
                energy        = kWh × (Nordpool spot + supplier margin)
                grid tariff   = kWh × tariff rate (time-differentiated day/night/peak)
                product margin = kWh × product rate
                + subscription (daily rate) + electricity tax (kWh) + VAT (25%)
              Generate invoice → send to customer (e-Boks/email/post)
                                    │
                                    ▼
PHASE 4: OPERATIONS                                              months/years
─────────────────────────────────────────────────────────────────────────────
Trigger:      Customer is active — ongoing supply
DataHub:      ← RSM-012 (daily hourly metering data)
              ← RSM-014 (monthly aggregations)
              ← BRS-027 (wholesale settlement / engrosopgørelse)
              ← Charges (tariff updates from the grid company)
              ← RSM-004/007 (master data changes)
              → BRS-028/029/030 (on-demand data requests)
Billing:      Periodic invoicing (monthly/quarterly)
              Wholesale reconciliation: own calculation vs. DataHub (RSM-014/BRS-027)
              Tariff updates when new rates from grid company
              Aconto settlement (acontoopgørelse): actual consumption vs. aconto payments (each quarter)
                                    │
                                    ▼
PHASE 5: OFFBOARDING                                             ~15 business days
─────────────────────────────────────────────────────────────────────────────
Trigger:      Customer moves out / another supplier takes over / non-payment
DataHub:      → BRS-002 (supply termination / leveranceophør — we terminate, scenario B/D)
              → BRS-010 (move-out / fraflytning — scenario C)
              → BRS-044 (cancel termination if customer changes mind)
              ← BRS-001 (incoming switch from another DDQ — scenario A)
Billing:      Mark metering point inactive, record end date
              Run final settlement for partial period
                                    │
                                    ▼
PHASE 6: CLOSING                                                 ~1 month
─────────────────────────────────────────────────────────────────────────────
Trigger:      Final metering data received from DataHub
DataHub:      ← RSM-012 (final metering data up to end date)
Billing:      Final settlement: energy + tariff + subscription (pro-rated)
              Aconto settlement (acontoopgørelse): actual consumption vs. total aconto payments
              Final invoice: credit → refund / debit → collection
              Archive customer record (5 years) + retain metering data (3+ years)

─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─

SPECIAL CASES (can occur in any phase)
─────────────────────────────────────────────────────────────────────────────
              → BRS-042 (erroneous switch)    → credit all invoices
              → BRS-011 (erroneous move)      → recalculate + credit/debit
              → RSM-015 (historical data)     → verification in disputes
              → RSM-016 (aggregated data)     → reconciliation
              ← BRS-021 (corrected metering data)  → recalculate affected periods
```

---

## Process-to-Phase Mapping

| BRS/RSM | Phase | Role | Billing Consequence |
|---------|-------|------|--------------------|
| BRS-001 (supplier switch / leverandørskifte) | 1 - Onboarding | We initiate | Set up billing plan + aconto calculation |
| BRS-043 (short-notice switch) | 1 - Onboarding | We initiate | Set up billing plan + aconto calculation |
| BRS-009 (move-in / tilflytning) | 1 - Onboarding | We initiate | Set up billing plan + aconto calculation |
| BRS-015 (customer master data / kundestamdata) | 1 - Onboarding | We submit | No direct consequence |
| BRS-003 (cancel switch) | 1 - Onboarding | We initiate (if customer cancels before activation) | Cancel created billing plan |
| RSM-007 (master data snapshot) | 2 - Activation | We receive | Assign tariffs and product plan |
| RSM-012 (metering data / måledata) | 2-6 | We receive (ongoing) | Calculation basis for settlement |
| RSM-014 (aggregated data) | 3-4 | We receive (periodic) | Reconciliation against wholesale settlement |
| BRS-027 (wholesale settlement / engrosopgørelse) | 4 - Operations | We receive | Reconcile own settlement against DataHub |
| BRS-028/029/030 (on-demand data) | 4 - Operations | We request | Verification of settlement components |
| BRS-006 (change of balance responsible party / balanceansvarlig) | 4 - Operations | We receive notification | May affect settlement configuration |
| RSM-004 (master data change) | 4 - Operations | We receive | May trigger recalculation if settlement method/grid area changes |
| Charges (tariff updates) | 4 - Operations | We receive | Update rate tables, affects future invoices |
| BRS-002 (supply termination / leveranceophør) | 5 - Offboarding | We initiate (scenario B, D) | Final settlement + final invoice + aconto settlement (acontoopgørelse) |
| BRS-010 (move-out / fraflytning) | 5 - Offboarding | We or DDM initiate (scenario C) | Final settlement + final invoice + aconto settlement (acontoopgørelse) |
| BRS-044 (cancel supply termination) | 5 - Offboarding | We initiate (upon cancellation) | Cancel planned final settlement |
| BRS-001 from another DDQ | 5 - Offboarding | We receive (scenario A) | Final settlement + final invoice + aconto settlement (acontoopgørelse) |
| BRS-042 (erroneous switch) | Special case | We initiate | Credit all invoices for the erroneous period |
| BRS-011 (erroneous move) | Special case | We initiate | Recalculate affected periods, credit/debit notes |
| RSM-015 (request historical data) | Special case | We request (disputes, verification) | No direct consequence (verification) |
| RSM-016 (request aggregated data) | Special case | We request (reconciliation) | No direct consequence (reconciliation) |

---

## Invoice Calculation: Building a Correct Invoice

An invoice is calculated per hour (flex settlement / flexafregning) for the entire billing period. Each hour has its own consumption (kWh from RSM-012) and its own spot price from Nordpool.

### Energy (Nordpool Spot + Supplier Margin)

The energy price per hour is composed of two parts:

| Component | Source | Description |
|-----------|--------|-------------|
| Nordpool spot price | Power exchange via market data | Hourly price in DKK/kWh, varies hour by hour |
| Supplier margin (leverandørmargin / markup) | Product plan / contract terms | Fixed øre/kWh markup on top of the spot price |

```
Energy per hour = kWh × (spot price + supplier margin)
```

In Xellent this is pre-calculated:
- `PowerExchangePrice` = pure Nordpool spot price
- `CalculatedPrice` = spot price + supplier margin (already combined)
- `TimeValue` = kWh consumed in the hour

The supplier margin is the profit the supplier charges per kWh on top of the purchase price from Nordpool. The amount depends on the customer's product plan (e.g. a fixed markup of X øre/kWh).

### Grid Tariffs (Transport / Nettariffer)

Grid tariffs are charged by the grid company (netvirksomhed) for transporting electricity. The rates are time-differentiated (different rates for day/night/peak).

```
Grid tariff per hour = kWh × tariff_rate_for_the_hour
```

- Rates from `PriceElementRates` (columns Price, Price2..Price24 for hours 1-24)
- Association via `PriceElementCheckData` (date interval for when the tariff applies)
- Only entries with `ChargeTypeCode = 3` are tariffs
- Grid area (from RSM-007) determines which grid company's tariffs apply

### Product Margin

Additional per-kWh charge defined in the customer's product plan (e.g. green energy surcharge, service surcharge).

```
Product margin per hour = kWh × product_rate
```

- Rates from `ExuRateTable` based on product type
- Product association via `ProductExtentTable`

### Fixed Charges (Subscription / Abonnement)

| Charge | Source | Calculation |
|--------|--------|-------------|
| Grid subscription (netabonnement) | Grid company (netvirksomhed) | Fixed monthly amount, prorated per day |
| Own subscription (supplier) | Product plan | Fixed monthly amount, prorated per day |

### Taxes and VAT (Afgifter og moms)

| Tax | Calculation |
|-----|-------------|
| Electricity tax (elafgift) | kWh × tax rate |
| VAT (moms, 25%) | Calculated on the sum of all above components |

### Total Calculation

```
For each hour in the billing period:
  energy        = kWh × (Nordpool spot price + supplier margin)
  grid tariff   = kWh × tariff_rate_for_the_hour
  product margin = kWh × product_rate
  electricity tax = kWh × tax_rate
  subscription  = daily_rate / 24

Invoice line   = Σ all hours for each component
VAT            = 25% of total
Invoice total  = sum of all lines + VAT
```

### Verifying an Invoice

1. Retrieve RSM-012 metering data for the period (kWh per hour)
2. Retrieve Nordpool spot prices for the same hours
3. Confirm that `CalculatedPrice ≈ spot price + agreed supplier margin` for each hour
4. Retrieve applicable tariff rates from the grid company for the period
5. Calculate each component per hour and sum
6. Compare with wholesale settlement (RSM-014 / BRS-027) for reconciliation

---

## Phase 1: Onboarding (Contract to Switch Request)

**Trigger:** The customer signs a supply agreement with us.

### Internal Steps

1. Sales creates customer record (name, CPR/CVR, contact details, contract terms)
2. Sales registers the metering point's GSRN (18-digit number from the customer's current bill or via Eloverblik)
3. The system determines the correct process:
   - **New customer on existing metering point** -> supplier switch (leverandørskifte) (BRS-001 or BRS-043)
   - **Customer moving into a new address** -> move-in (tilflytning) (BRS-009)
4. The system selects product/tariff plan based on contract terms
5. Onboarding record is created with status `awaiting_datahub`

### DataHub Communication

| Step | Direction | BRS/RSM | What happens |
|------|-----------|---------|--------------|
| 1 | DDQ -> DataHub | **BRS-001** (RSM-001) | Submit supplier switch request with GSRN + desired effective date + customer's CPR/CVR |
| 2 | DataHub -> DDQ | Acknowledgement | DataHub validates: metering point exists, no conflicts, CPR/CVR matches |
| 3 | DataHub -> old DDQ | Notification | Current supplier is notified that they are losing the metering point |

**If short notice is needed** (e.g. urgent case), use **BRS-043** instead — same message, shorter notice period.

**For move-in (tilflytning)** (no current supplier at the address), use **BRS-009** — similar flow but without the old supplier.

### Deadlines

- BRS-001: minimum 15 business days' notice before effective date. VERIFY
- BRS-043: 1 business day's notice. VERIFY
- BRS-009: can take effect immediately or on a future date. VERIFY

### What Can Go Wrong

- **Rejection:** DataHub rejects the request (incorrect GSRN, conflicting process, CPR mismatch) -> correct data and resubmit
- **Customer cancellation:** The customer changes their mind -> send **BRS-003** to cancel before the effective date

---

## Phase 2: Activation (Switch Takes Effect)

**Trigger:** The effective date for the supplier switch has been reached.

### DataHub Communication

| Step | Direction | BRS/RSM | What happens |
|------|-----------|---------|--------------|
| 1 | DataHub -> DDQ | **RSM-007** (MasterData queue) | Complete master data snapshot for the metering point: type, settlement method, grid area, connection status, grid company |
| 2 | DataHub -> DDQ | **BRS-015** response | Confirmation of customer master data. VERIFY |
| 3 | DataHub -> DDQ | **RSM-012** (Timeseries queue) | First metering data delivery — may include historical data for the transition period |

### Internal Steps

1. Receive and store master data -> metering point is now `active` in the portfolio
2. Record the supply period start date
3. Assign product/tariff plan to the metering point
4. Load grid tariffs for the metering point's grid area (from Charges queue data)
5. Set up billing schedule (monthly or quarterly — per contract)
6. For aconto billing: calculate estimated quarterly aconto amount based on expected annual consumption
7. The customer is now visible in the customer portal

### Key Data Received at Activation

| Data | Source | Used for |
|------|--------|----------|
| Metering point type (E17 consumption, E18 production) | RSM-007 | Determines settlement method |
| Settlement method (flex / profile) | RSM-007 | Determines how metering data is received and settlement is calculated |
| Grid area (netområde) | RSM-007 | Maps to the grid company's tariff plan |
| Estimated annual consumption | RSM-007. VERIFY | Calculation basis for aconto |
| Grid company GLN | RSM-007 | Identifies which tariffs apply |

---

## Phase 3: First Invoice

**Trigger:** The first billing period ends (typically 1 month after activation).

### Metering Data Flow (Ongoing from Activation)

| Event | Direction | Message | Frequency |
|-------|-----------|---------|-----------|
| Grid company reads meter | MDR -> DataHub | BRS-021 | Daily (flex) or monthly (profile) |
| DataHub forwards to us | DataHub -> DDQ | RSM-012 (E66, Timeseries queue) | Same frequency |
| DataHub runs wholesale settlement | DataHub -> DDQ | RSM-014 (E31, Aggregations queue) | Monthly. VERIFY |

### Invoice Calculation

For a **flex-settled** metering point (most common for customers with remotely read meters):

```
For each interval in the billing period:
  1. Energy cost     = quantity_kwh × spot_price_for_interval
  2. Grid tariff     = quantity_kwh × grid_tariff_rate_for_interval
  3. Product margin  = quantity_kwh × product_rate_for_interval
  4. Subscription    = daily_subscription_fee / intervals_per_day
  5. Taxes           = quantity_kwh × applicable_tax_rates

Invoice line total = sum of all intervals for each component
```

For a **profile-settled** metering point:
- Uses estimated consumption profile distributed across intervals
- Actual consumption is reconciled subsequently via BRS-020 consumption statement (forbrugsopgørelse)

### Invoice Components

| Line | Source | Calculation basis |
|------|--------|-------------------|
| Energy (spot + margin) | RSM-012 quantities × market price + product margin | Per interval |
| Grid tariff (transport / nettarif) | RSM-012 quantities × grid tariff rates | Per interval, time-differentiated |
| System tariff (systemtarif) | RSM-012 quantities × system tariff rate | Per interval |
| Subscription (grid / netabonnement) | Grid company's fixed monthly fee | Per day |
| Subscription (own) | Product plan's fixed fee | Per day |
| Electricity tax (elafgift) | RSM-012 quantities × tax rate | Per kWh |
| PSO / green tax. VERIFY | RSM-012 quantities × tax rate | Per kWh |
| VAT (moms, 25%) | Sum of the above | Standard Danish VAT |

### Aconto vs. Actual

Aconto (prepayment on account) is a billing model where the customer pays a fixed estimated amount each period rather than the actual consumption cost.

| Model | Description | Reconciliation |
|-------|-------------|----------------|
| **Aconto** | The customer pays a fixed quarterly estimate. The aconto settlement (acontoopgørelse) reconciles against actual consumption. | Each quarter (at billing period end) |
| **Actual** | The customer pays based on actually measured consumption each period. | Each invoice is final (no reconciliation needed) |

### Internal Steps

1. Settlement engine runs for the billing period
2. Settlement results are grouped by invoice line types
3. Invoice is generated and sent to the customer (email, e-Boks, or post)
4. Payment follow-up begins (payment due date typically net 14-30 days)
5. Settlement results are stored for audit and reconciliation

---

## Phase 4: Operations (Ongoing Supply / Løbende leverance)

The customer is active. The following occurs on an ongoing basis:

### Daily Operations

| Event | DataHub | Internal |
|-------|---------|----------|
| Metering data received | RSM-012 via Timeseries queue | Stored in time series database |
| Tariff/charge updates | Charges queue | Rate tables updated |
| Master data changes | RSM-004/007 via MasterData queue | Portfolio updated |

### Periodic Operations

| Event | Frequency | DataHub | Internal |
|-------|-----------|---------|----------|
| Invoice generation | Monthly / quarterly | — | Settlement run -> invoice -> send to customer |
| Wholesale settlement reconciliation | Monthly. VERIFY | RSM-014 (BRS-027) | Compare own settlement with DataHub aggregation |
| Aconto settlement (acontoopgørelse) | Quarterly (for aconto) | — | Calculate actual vs. aconto payments, net on quarterly invoice |
| Product/price changes | Per contract | — | Update product rates, notify customer |
| Grid tariff changes | Typically annually | Charges queue | Update rate tables, recalculate future estimates |
| Change of balance responsible party (balanceansvarlig) | Rare | BRS-006 notification | Update portfolio records |

### Customer Self-Service (Portal)

- View consumption data (hourly/daily/monthly graphs)
- View and download invoices
- Update contact information
- View contract details and product plan

### Payment and Collections (Inkasso)

| Event | Action |
|-------|--------|
| Invoice issued | Record receivable, send to customer |
| Payment received | Match to invoice, update balance |
| Payment overdue | Send reminder (1st, 2nd) |
| Persistent non-payment | Initiate supply termination (BRS-002) — see Phase 5 |

---

## Phase 5: Offboarding

A customer leaves us for one of several reasons. Each follows a different process:

### Scenario A: Customer Switches to Another Supplier

**Trigger:** Another supplier submits BRS-001 for our metering point.

| Step | Direction | What happens |
|------|-----------|--------------|
| 1 | DataHub -> DDQ | We receive notification that a new supplier has requested our metering point |
| 2 | (waiting) | The effective date is reached — the supply obligation transfers |
| 3 | DataHub -> DDQ | RSM-012 with final metering data up to the switch date |
| 4 | Internal | Mark metering point as `inactive`, record supply period end date |
| 5 | Internal | Run final settlement for the partial period |
| 6 | Internal | Generate final invoice / credit note |

We do not initiate anything — the incoming supplier drives the process.

### Scenario B: Customer Terminates Contract

**Trigger:** The customer notifies us that they wish to terminate supply (moving abroad, switching to own production, etc.).

| Step | Direction | BRS/RSM | What happens |
|------|-----------|---------|--------------|
| 1 | DDQ -> DataHub | **BRS-002** (RSM-005) | Submit supply termination request with effective date |
| 2 | DataHub -> DDQ | Acknowledgement | DataHub confirms |
| 3 | (effective date) | | Supply terminates. The metering point may transfer to the "supplier of last resort" (forsyningspligtig leverandør). VERIFY |
| 4 | DataHub -> DDQ | RSM-012 | Final metering data |
| 5 | Internal | | Final settlement + invoice |

**Cancellation option:** If the customer changes their mind before the effective date, send **BRS-044** to cancel the supply termination.

### Scenario C: Customer Moves Out (Fraflytning)

**Trigger:** The customer reports moving out to a new address, or the grid company (netvirksomhed) reports the move-out.

| Step | Direction | BRS/RSM | What happens |
|------|-----------|---------|--------------|
| 1 | DDQ -> DataHub (or DDM -> DataHub) | **BRS-010** | Move-out message with effective date |
| 2 | DataHub -> DDQ | Acknowledgement | DataHub confirms |
| 3 | DataHub -> DDQ | RSM-012 | Final metering data up to move-out date |
| 4 | Internal | | Final settlement + invoice |

If the customer also **moves into** a new address where we supply, the move-out and a new BRS-009 move-in run in parallel.

### Scenario D: Non-Payment (Forced Supply Termination)

**Trigger:** The customer has not paid after repeated reminders.

| Step | Direction | BRS/RSM | What happens |
|------|-----------|---------|--------------|
| 1 | Internal | | Collections process exhausted, decision to terminate |
| 2 | DDQ -> DataHub | **BRS-002** (RSM-005) | Submit supply termination with reason: non-payment. VERIFY |
| 3 | DataHub -> DDQ | Acknowledgement | DataHub confirms |
| 4 | (effective date) | | Supply terminates |
| 5 | DataHub -> DDQ | RSM-012 | Final metering data |
| 6 | Internal | | Final settlement + invoice. Outstanding debt transfers to collections/write-off |

**Cancellation option:** If the customer pays before the effective date, send **BRS-044** to cancel.

---

## Phase 6: Closing (Final Settlement / Slutafregning)

Regardless of the offboarding reason, the closing process is the same:

1. Receive final RSM-012 metering data from DataHub (up to end date)
2. Run settlement for the partial billing period
3. For aconto customers: calculate final aconto settlement (acontoopgørelse) — actual consumption vs. aconto payments
4. Issue final invoice within 4 weeks (per the Electricity Supply Order / elleveringsbekendtgørelsen section 17)
5. Archive customer record, retain metering data per retention policy

> Details: [Special Cases and Error Handling](datahub3-edge-cases.md#4-slutafregning-ved-offboarding)

---

## Sources

- [DataHub 3 DDQ Business Process Reference](datahub3-ddq-business-processes.md)
- [Proposed System Architecture](datahub3-proposed-architecture.md)
- [RSM-012 Metering Data Reference](rsm-012-datahub3-measure-data.md)
