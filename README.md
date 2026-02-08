# DataHub Settlement System

A settlement system for a Danish electricity supplier (elleverandør) integrating with Energinet's DataHub 3 — the central market hub that coordinates all electricity suppliers, grid companies, and metering data in Denmark.

## What this system does

In Denmark, every electricity customer has a metering point (identified by a GSRN number) that measures hourly consumption. When a customer signs up with us, we become responsible for billing them — not just for our own margin, but for the full invoice including grid tariffs, system tariffs, and taxes set by other parties. The complexity is in calculating every hour correctly and keeping in sync with DataHub.

The system covers the full customer lifecycle:

1. **[Products & Pricing](#1-products--pricing)** — What we sell: margin + subscription. Everything else on the invoice is pass-through.
2. **[Onboarding](#2-onboarding)** — A customer signs up, we tell DataHub, and after validation + a waiting period the metering point becomes ours.
3. **[Settlement](#3-settlement)** — Every hour of consumption is calculated: energy + grid tariff + system tariff + transmission + electricity tax + subscriptions + VAT.
4. **[Billing & Payment](#4-billing--payment)** — Settlement results become invoices. Two models: pay for actual use (arrears) or estimated quarterly payments (aconto).
5. **[Offboarding](#5-offboarding)** — Customer leaves (switches supplier, moves out, or non-payment). Final settlement, final invoice.
6. **[Corrections & Edge Cases](#6-corrections--edge-cases)** — Corrected metering data, erroneous switches, tariff changes mid-period, electrical heating thresholds, solar net settlement.
7. **[Reconciliation](#7-reconciliation)** — DataHub calculates aggregated totals independently. We compare against ours and investigate discrepancies.
8. **[DataHub Communication](#8-datahub-communication)** — Queue-based integration: we poll 4 queues for incoming data and send BRS requests for market processes.

All communication with DataHub happens through CIM JSON messages over HTTP queues. We never call DataHub on demand — data arrives when DataHub has something for us, and we send requests when we need to initiate a market process.


### Quick Start

```bash
cd DataHub.Settlement
docker compose up -d          # TimescaleDB + DataHub simulator + Aspire Dashboard
dotnet build
dotnet test
dotnet run --project src/DataHub.Settlement.Worker    # Background services
dotnet run --project src/DataHub.Settlement.Web       # Dashboard at localhost:5000
```

**Aspire Dashboard:** http://localhost:18888 — logs, traces, metrics.

### Technology

.NET 9, PostgreSQL 16 + TimescaleDB, Dapper, DbUp migrations, OpenTelemetry, xUnit + FluentAssertions, Docker Compose, GitHub Actions CI.

---

## 1. Products & Pricing

A customer choosing an electricity supplier is really choosing **two numbers**: a margin and a subscription fee. Everything else on the invoice — grid tariffs, system tariffs, transmission, electricity tax — is identical regardless of supplier. These are pass-through costs.

The supplier's product is defined by:

| Parameter | Example | What it means |
|-----------|---------|--------------|
| **Margin** | 4 øre/kWh | Added on top of the Nord Pool spot price for every kWh consumed |
| **Supplement** | 0 øre/kWh (optional) | Extra per-kWh charge (e.g., "green energy" surcharge) |
| **Subscription** | 39 DKK/month | Fixed monthly fee, independent of consumption |
| **Energy model** | Spot | Customer pays the hourly Nord Pool price + margin. Alternative: fixed price per kWh |

Note: binding periods are not allowed for electricity supply contracts in Denmark. Customers can switch supplier at any time with 15 business days notice.

Grid tariffs, system tariffs, transmission, and electricity tax are set by the grid company, Energinet, and the state respectively. The customer can't negotiate these — they're the same regardless of supplier. There are ~40 grid companies in Denmark, each with different rates, but which one applies is determined by the customer's address, not their choice of supplier.

This matters for the onboarding API: **the sales channel only needs to show our own pricing** (margin + subscription). No need to estimate grid tariffs at signup — the customer can't compare those between suppliers anyway.

```
Invoice line               Who sets the price      We control?
─────────────────────────  ─────────────────────── ─────────────
Energy (spot + margin)     Nord Pool + us          ✓ margin
Grid tariff                Grid company            ✗ pass-through
System tariff              Energinet               ✗ pass-through
Transmission               Energinet               ✗ pass-through
Electricity tax (elafgift) The state               ✗ pass-through
Grid subscription          Grid company            ✗ pass-through
Supplier subscription      Us                      ✓ subscription
VAT (25%)                  The state               ✗ calculated
```

---

## 2. Onboarding

A customer provides their address, name, CPR/CVR, and selects a product. The system then orchestrates a multi-week process with DataHub to become the supplier for that customer's metering point.

```
Customer provides: name, address (DAR ID), CPR/CVR, product choice
                        │
                        ▼
System resolves address → GSRN (metering point ID)
                        │
                        ▼
System sends request to DataHub:
  • Supplier switch (BRS-001) — taking over from another supplier
    OR
  • Move-in (BRS-009) — no current supplier at the address
                        │
                        ▼
DataHub validates:
  • Does the GSRN exist?
  • Does the CPR/CVR match the registered occupant?
  • Is there a conflicting process already running?
                        │
            ┌───────────┴───────────┐
            ▼                       ▼
       Accepted                  Rejected
       (RSM-009)                 (RSM-009 with reason)
            │                       │
            ▼                       ▼
    Wait for effective date    Status → rejected
    (15 business days for      (CPR mismatch, conflict, etc.)
     switch, immediate for
     move-in)
            │
            ▼
    DataHub sends RSM-007 (master data):
      • Grid area → determines tariffs
      • Meter type → E17 (consumption) or E18 (production/solar)
      • Settlement method → flex (hourly readings)
            │
            ▼
    Metering point is now active
    RSM-012 (hourly consumption data) starts arriving daily
            │
            ▼
    Status → active
```


### Why it takes 15 business days

A supplier switch (BRS-001) requires a minimum notice period of 15 business days. This gives the current supplier time to prepare final settlement and the customer time to change their mind. A move-in (BRS-009) has no notice period — there's no current supplier to notify.

### What can go wrong at onboarding

| Problem | What happens | How it's handled |
|---------|-------------|-----------------|
| CPR/CVR doesn't match DataHub's records | Rejected | Status shows "rejected" with reason. Customer service corrects and resubmits |
| Another supplier switch is already in progress (E16) | Rejected | Can auto-retry after the conflicting process completes |
| Customer changes their mind before effective date | Cancel | BRS-003 sent to DataHub, process cancelled |
| Multiple metering points at the address | Ambiguous GSRN | Sales channel asks customer to clarify which meter |
| Address doesn't resolve to any GSRN | No metering point found | Signup fails, customer contacts support |

### The process state machine

Every onboarding request moves through these states:

```
pending → sent_to_datahub → acknowledged → effectuation_pending → completed
    │            │
    └→ cancelled └→ rejected
```

The sales channel sees a simplified version: **registered → processing → active** (or rejected/cancelled). The internal states are hidden.

---

## 3. Settlement

### How an invoice is calculated

Settlement runs per hour for the entire billing period. For a standard January (744 hours), the engine processes each hour individually:

```
For each hour:
  energy         = kWh × (spot price + margin + supplement)
  grid tariff    = kWh × grid rate for this hour (day/night/peak)
  system tariff  = kWh × Energinet's rate
  transmission   = kWh × Energinet's rate
  electricity tax = kWh × statutory rate
  subscription   = monthly fees prorated to the period

Subtotal = sum of all hours, grouped by charge type
VAT      = 25% of subtotal
Total    = subtotal + VAT
```

### Why hourly matters

Grid tariffs are **time-differentiated**. A customer using electricity during peak hours (17:00-20:00) pays significantly more in grid tariff than one using it at night:

| Time | Grid rate | Spot price (example) |
|------|----------|---------------------|
| Night (21-06) | 0.06 DKK/kWh | ~45 øre/kWh |
| Day (06-17) | 0.18 DKK/kWh | ~85 øre/kWh |
| Peak (17-20) | 0.54 DKK/kWh | ~125 øre/kWh |

The spot price also varies hour by hour. This is why settlement must happen per hour — a flat monthly average would be wrong.

### Reference invoice (Golden Master #1)

A standard spot customer consuming 409.2 kWh in January:

| Charge | Amount (DKK) |
|--------|-------------|
| Energy (spot + 4 øre margin) | 386.51 |
| Grid tariff (time-differentiated) | 114.58 |
| System tariff | 22.10 |
| Transmission | 20.05 |
| Electricity tax | 3.27 |
| Grid subscription | 49.00 |
| Supplier subscription | 39.00 |
| **Subtotal** | **634.51** |
| VAT (25%) | 158.63 |
| **Total** | **793.14** |

This amount is hand-calculated and serves as a regression test. The settlement engine must reproduce it exactly.

### Partial periods

When a customer starts or stops mid-month, subscriptions are prorated by day. A customer starting January 16 pays 16/31 of the monthly subscription. Energy, tariffs, and tax are only calculated for hours with actual consumption data.

---

## 4. Billing & Payment

### Two payment models

| | **Arrears (bagudbetaling)** | **Aconto** |
|---|---|---|
| What the customer pays | Actual consumption for the past month | Fixed estimated amount per quarter |
| Billing frequency | Monthly (12 invoices/year) | Quarterly (4 invoices/year) |
| Reconciliation | None needed — each invoice is final | Each quarter: actual vs. paid |
| Industry trend | Preferred — transparent, no surprises | Being phased out by many suppliers |

### How aconto works

The customer pays a fixed estimated amount each quarter. Behind the scenes, the settlement engine runs exactly as normal — calculating actual consumption per hour. At the end of each quarter, the system compares:

```
Actual settlement total for the quarter: 2,441.06 DKK
Aconto paid during the quarter:         -1,950.00 DKK
──────────────────────────────────────────────────────
Difference (underpaid):                  +  491.06 DKK
```

This difference appears on the **combined quarterly invoice**, which has two parts:

1. **Settlement** — actual cost for the past quarter ± difference from aconto
2. **New aconto** — estimated payment for the upcoming quarter

The customer pays one net amount. No separate credit notes are issued — over/underpayment is netted on the combined invoice.

### Aconto estimation

| Customer type | How the estimate is made |
|--------------|------------------------|
| New (no history) | Standard assumption: 4,000 kWh/year (house) or 2,500 kWh/year (apartment) × expected price levels |
| Existing | Last 12 months actual consumption × current price levels |

Recalculated automatically at each quarterly settlement, or on customer request.

---

## 5. Offboarding

### Why a customer leaves

| Scenario | Trigger | BRS process |
|----------|---------|-------------|
| **Switches to another supplier** | The new supplier sends BRS-001 for our metering point | We receive notification, don't initiate |
| **Terminates contract** | Customer calls to cancel | We send BRS-002 (end of supply) |
| **Moves out** | Customer moves to a new address | We or grid company sends BRS-010 |
| **Non-payment** | Collections process exhausted | We send BRS-002 with non-payment reason |

### What happens when a customer leaves

Regardless of the reason, the closing process is the same:

1. Mark metering point as inactive, record supply period end date
2. Receive final RSM-012 from DataHub (metering data up to the end date)
3. Run settlement for the partial period (start of billing period → departure date)
4. For aconto customers: reconcile actual consumption vs. aconto paid
5. Generate final invoice
6. Send within **4 weeks** (legal requirement — elleveringsbekendtgørelsen §17)

### Final settlement for aconto customers

```
Actual settlement (quarter start → departure date): 409.36 DKK
Aconto paid so far this quarter:                   -300.00 DKK
──────────────────────────────────────────────────────────────
Amount due:                                        +109.36 DKK
```

No new aconto estimate — the customer is leaving. If the customer overpaid, they get a refund.

### Cancellation before departure

If a customer changes their mind before the effective date:
- **Before effective date reached:** Send BRS-003 (cancel switch) or BRS-044 (cancel termination). Process cancelled, supply continues.
- **After effective date:** Too late for cancellation — use BRS-042 (erroneous switch) if it was a mistake.

---

## 6. Corrections & Edge Cases

### Metering data corrections

The grid company can submit **corrected measurements** for an already-settled period. There is no explicit "this is a correction" flag — a new RSM-012 arrives for the same GSRN and period, and the system must detect the delta by comparing against stored data.

```
Original data for Jan 15, 10:00: 0.500 kWh
Corrected data arrives:          0.750 kWh
Delta:                          +0.250 kWh

The system calculates only the financial impact of the delta:
  energy delta     = 0.250 × (spot + margin)
  grid tariff delta = 0.250 × grid rate
  ... etc.

Result: credit or debit note for the difference
```

Subscriptions are NOT adjusted — they're fixed regardless of consumption. The original data is preserved in a history table for audit.

**Corrections can arrive up to 3 years after the original reading.**

### Erroneous switch (BRS-042)

A supplier switch happened by mistake — wrong metering point, or the customer didn't actually consent. Everything must be reversed:

1. Send BRS-042 to DataHub (within 20 business days of effective date)
2. DataHub reinstates the old supplier
3. All issued invoices for the erroneous period are credited
4. Metering data for the period is marked as reversed

For a 2-month erroneous period, the credit can be substantial — **1,520 DKK** in the reference test case.

### Tariff change mid-billing-period

The grid company changes their tariff rates on the 16th of the month. The system splits the billing period at the change date and calculates each half with the correct rates:

```
Jan 1-15:  old grid tariff rates
Jan 16-31: new grid tariff rates (e.g. 50% increase)

Both halves are calculated separately, then combined into one invoice.
```

The customer sees one invoice with the blended rates — the split is internal.

### Electrical heating (elvarme)

Customers registered for electrical heating get a **reduced electricity tax rate** on consumption above **4,000 kWh/year**. The system tracks cumulative annual consumption per metering point.

```
Customer starts the year with 3,800 kWh cumulative.
January consumption: 409 kWh.

First ~15 kWh: standard tax rate (up to 4,000 threshold)
Remaining ~394 kWh: reduced tax rate

The threshold can be crossed mid-billing-period — the system splits
the tax calculation at the exact hour where the threshold is crossed.
```

The elvarme flag comes from RSM-007 master data. The threshold resets on January 1 each year.

### Solar / production metering (E18)

Customers with solar panels have two metering points:
- **E17** — consumption (what they take from the grid)
- **E18** — production (what their panels generate)

Settlement is netted **per hour**:

```
For each hour:
  net = consumption - production

  If net > 0 (consumed more than produced):
    → Customer pays normally for the net amount

  If net < 0 (produced more than consumed):
    → Customer is credited at spot price ONLY
    → No margin, no tariffs, no tax on excess production

  If net = 0:
    → No charge, no credit
```

A solar customer's invoice has a **production credit line** — a negative amount that reduces the total. In the reference test case, a customer with 13.2 kWh consumption and 3.3 kWh production pays **20.10 DKK** instead of the ~29 DKK they'd pay without solar.

---

## 7. Reconciliation

DataHub calculates its own aggregated settlement per grid area (RSM-014). We calculate ours independently. They should match. When they don't, we need to find out why.

```
DataHub says:  Grid area 344, January, total 12,500 kWh
We calculated: Grid area 344, January, total 12,450 kWh
Discrepancy:   50 kWh
```

The system compares per hour with a tolerance of 0.001 kWh. If the discrepancy exceeds tolerance:

1. Identify which hours deviate
2. Request detailed data from DataHub (RSM-016) for the grid area
3. Identify which metering points are causing the difference
4. Request historical validated data (RSM-015) for those metering points
5. Root cause: missing data? incorrect tariff? calculation error?
6. Correct and recalculate

| Cause | Resolution |
|-------|-----------|
| Missing RSM-012 for a metering point | Request historical data, recalculate |
| Correction received after settlement | Recalculate with corrected data |
| Outdated tariff rates used | Update rates, recalculate |
| Rounding differences | Adjust calculation logic |

---

## 8. DataHub Communication

DataHub uses a **queue-based** integration model. We don't request data on demand — messages appear on queues when DataHub has something for us.

```
DataHub                                    Our system
  │                                            │
  │  Timeseries queue (RSM-012)                │
  ├───────────────────────────────────────────►│  Hourly metering data
  │                                            │
  │  MasterData queue (RSM-007, RSM-004)       │
  ├───────────────────────────────────────────►│  Metering point info, grid changes
  │                                            │
  │  Charges queue                             │
  ├───────────────────────────────────────────►│  Tariff updates from grid companies
  │                                            │
  │  Aggregations queue (RSM-014)              │
  ├───────────────────────────────────────────►│  DataHub's own settlement totals
  │                                            │
  │              BRS requests                  │
  │◄───────────────────────────────────────────┤  Supplier switch, move-in, etc.
  │                                            │
```

The system polls all 4 queues every 5 seconds. Each message goes through:

1. **Duplicate check** — same message may appear twice (at-least-once delivery)
2. **Parse** — CIM JSON format → domain objects
3. **Store** — metering data, master data, tariffs, etc.
4. **Dequeue** — remove from DataHub queue

### Message types we receive

| Message | Queue | What it contains | When it arrives |
|---------|-------|-----------------|----------------|
| RSM-012 | Timeseries | Hourly kWh consumption per GSRN | Daily |
| RSM-007 | MasterData | Metering point master data (grid area, type, settlement method) | On activation |
| RSM-004 | MasterData | Grid area change notification | On change |
| RSM-009 | MasterData | Accept/reject receipt for our BRS requests | After submission |
| RSM-014 | Aggregations | DataHub's aggregated settlement per grid area | Monthly |
| Charges | Charges | Tariff updates from grid companies | 1-2× per year |

### Requests we send

| Request | Purpose | Notice period |
|---------|---------|--------------|
| BRS-001 | Supplier switch | 15 business days |
| BRS-002 | End of supply (we terminate) | Varies |
| BRS-003 | Cancel pending switch | Before effective date |
| BRS-009 | Move-in | Immediate |
| BRS-010 | Move-out | Varies |
| BRS-042 | Erroneous switch reversal | Within 20 business days |
| BRS-043 | Short notice switch | 1 business day |
| BRS-044 | Cancel termination | Before effective date |

### Authentication

DataHub requires OAuth2 tokens from Azure AD. The system fetches a token, caches it, and proactively renews it 5 minutes before expiry. If a request gets a 401, the token is invalidated and retried with a fresh one.

### Resilience

| Error | Behavior |
|-------|----------|
| **401 Unauthorized** (token expired) | Invalidate cached token, get new one, retry once |
| **503 Service Unavailable** | Exponential backoff (1s, 2s, 4s), up to 3 retries |
| **Parse error** (malformed message) | Store in dead-letter table, dequeue to unblock the queue |
| **Database error** | Do NOT dequeue — message stays in queue, retried on next poll |

### CIM JSON format

All DataHub messages use CIM (Common Information Model) JSON format — a standardized energy industry format defined by Energinet. Each message contains a market document header, one or more series (each identified by GSRN), and data points with resolution (PT1H = hourly). The system parses these into domain objects and stores the relevant data.

---

## Verified Reference Invoices

10 hand-calculated invoices that the settlement engine must reproduce exactly. If any of these break, something is wrong.

| # | Scenario | Total (DKK) | What it proves |
|---|----------|-------------|---------------|
| 1 | Full January, standard spot customer | 793.14 | Basic settlement is correct |
| 2 | Partial January (mid-month start) | 409.36 | Pro-rata subscriptions work |
| 3 | Aconto quarterly reconciliation | 893.14 | Aconto difference + new estimate correct |
| 4 | Final settlement at offboarding | 109.36 | Partial period + aconto credit correct |
| 5 | Metering correction (3 hours revised) | 0.91 | Delta-only settlement correct |
| 6 | Erroneous switch (2 months reversed) | 1,520.16 | Full credit calculation correct |
| 7 | Elvarme crossing 4,000 kWh threshold | 792.36 | Split-rate tax correct |
| 8 | Solar customer (1 day) | 20.10 | Net settlement + production credit correct |
| 9 | Correction filtered to supply period | -0.44 | Only our supply period's delta is settled |
| 10 | Tariff change mid-period | 830.08 | Period split at tariff change date correct |

---

## Further Reading

| Document | What it covers |
|----------|---------------|
| [Next phase plan](docs/next-phase-plan.md) | Onboarding API design, MVP 3 gaps, MVP 4 roadmap |
| [Customer lifecycle](docs/datahub3-customer-lifecycle.md) | 6-phase lifecycle from onboarding to closing |
| [Product and billing](docs/datahub3-product-and-billing.md) | Invoice structure, aconto, payment models |
| [Edge cases](docs/datahub3-edge-cases.md) | Corrections, erroneous processes, reconciliation |
| [Implementation plan](docs/datahub3-implementation-plan.md) | Original MVP roadmap |
