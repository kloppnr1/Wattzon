# DataHub 3: Edge Cases and Error Handling

All edge cases, error scenarios, and recovery procedures collected in one place. Covers metering data corrections, erroneous processes, cancellations, reconciliation discrepancies, system errors, and customer disputes.

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

## 3. Cancellations

### 3.1 Cancel supplier switch (leverandørskifte) (BRS-003)

The customer withdraws from a supplier switch **before** the effective date.

**Prerequisite:** The switch has been requested (BRS-001) but the effective date has not yet been reached.

| Step | Action |
|------|--------|
| 1 | DDQ -> DataHub: **BRS-003** cancellation request |
| 2 | DataHub -> DDQ: Receipt (accepted/rejected) |
| 3 | DataHub -> old DDQ: Notification of cancellation |
| 4 | Internal: Mark ProcessRequest as `cancelled` |
| 5 | Internal: Remove prepared billing plans, aconto estimates, etc. |

**Cannot be cancelled after the effective date** — use BRS-042 instead.

### 3.2 Cancel supply termination (leveranceophør) (BRS-044)

The customer withdraws a termination notice or pays an outstanding amount **before** the effective date.

| Step | Action |
|------|--------|
| 1 | DDQ -> DataHub: **BRS-044** cancel termination |
| 2 | DataHub -> DDQ: Receipt (termination cancelled) |
| 3 | Internal: Supply continues normally |
| 4 | Internal: Cancel any prepared final settlements |

**Typical scenario:** The customer has received a BRS-002 due to non-payment and then pays.

---

## 4. Final Settlement at Offboarding

Regardless of the offboarding reason (supplier switch, move-out, non-payment), the final settlement is the same.

### Offboarding scenarios

| Scenario | Trigger | BRS process | Who initiates |
|----------|---------|-------------|---------------|
| **A** | Customer switches to another supplier | Incoming BRS-001 | New supplier |
| **B** | Customer terminates | BRS-002 | Us |
| **C** | Customer moves out (fraflytning) | BRS-010 | Us or grid company |
| **D** | Non-payment | BRS-002 | Us |

### Final settlement procedure

1. Receive final RSM-012 metering data from DataHub (up to the end date)
2. Run settlement for the partial billing period (faktureringsperiode) (period start -> supply end date)
3. Calculate all components: energy, grid tariff (nettarif), subscriptions (**pro rata**), taxes and charges

### Aconto customer: final settlement (slutopgørelse)

Aconto is a prepayment scheme where customers pay estimated amounts periodically rather than per actual consumption, serving as a cash flow tool for the supplier.

1. Calculate actual consumption from the start of the quarter to the supply end date (partial period)
2. Compare with aconto payments received for this period
3. Generate final invoice with reconciliation:
   - Overpaid -> credit note / refund
   - Underpaid -> final invoice with remaining balance

### Final invoice line items

| Line | Description |
|------|-------------|
| Energy + margin | Actual consumption x rates for the partial period |
| Grid tariff (nettarif) | Actual consumption x tariff rates, pro rata |
| Subscription (own) | Pro rata to the supply end date |
| Subscription (grid) | Pro rata to the supply end date |
| Taxes and charges (afgifter og gebyrer) | Per kWh on actual consumption |
| Aconto settlement (acontoopgørelse) (if applicable) | Difference between paid estimates and actual total |
| Outstanding balance | Any unpaid previous invoices |
| **Amount owed / credit balance** | Net amount |

### After the final invoice

| Action | Deadline |
|--------|----------|
| Send final invoice to the customer | Within 4 weeks (cf. the Electricity Supply Order (elleveringsbekendtgørelsen) section 17) |
| If credit balance: refund to customer's bank account | Without undue delay |
| If debit balance: normal payment terms | Net 14-30 days |
| Unpaid debit balance -> debt collection (inkasso) | After payment deadline |
| Archive customer records | Retain for 5 years (WARNING: VERIFY) |
| Retain metering data | Per retention policy (3+ years WARNING: VERIFY) |
| Deactivate customer portal access | After final payment |

---

## 5. Reconciliation Discrepancies (BRS-027)

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

## 6. Customer Disputes Invoice

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

## 7. System Errors and Recovery

### DataHub Communication Errors

| Scenario | Action |
|----------|--------|
| DataHub 5xx / timeout | Retry with exponential backoff, do **not** dequeue |
| 401 Unauthorized | Fetch new OAuth2 token, retry |
| 403 Forbidden | Check credentials and GLN in the actor portal (aktørportalen) |
| 429 Too Many Requests | Wait and retry with backoff |

### Message Processing Errors

| Scenario | Action |
|----------|--------|
| Message parsing error (invalid JSON/XML) | Dead-letter, dequeue to free the queue |
| Unknown MessageType | Dead-letter, dequeue, alert operator |
| Business validation error (unknown GSRN etc.) | Log + save for review, dequeue |
| Database error during persistence | Retry, do **not** dequeue (at-least-once guarantee) |

### Settlement Errors

| Scenario | Action |
|----------|--------|
| Settlement calculation error | Fail the run, alert, preserve partial results for debugging |
| Missing spot prices for the period | Stop settlement run, alert — cannot calculate without prices |
| Missing tariff rates for grid area | Stop for affected metering points, alert |
| Inconsistent metering data (gaps in time series) | Mark affected metering points, recalculate when data is complete |

### Dead-letter handling

Messages that fail parsing or validation end up in the dead-letter table.

**Operator procedure:**
1. Monitor `datahub.dead_letter` for unresolved entries (alert on growth)
2. Analyze `error_reason` and `raw_payload`
3. Fix the root cause (parsing error, missing data, etc.)
4. Reprocess the message manually or via replay
5. Mark as `resolved`

```sql
-- Unprocessed dead letters
SELECT id, queue_name, error_reason, failed_at
FROM datahub.dead_letter
WHERE NOT resolved
ORDER BY failed_at DESC;
```

---

## 8. Concurrent Processes

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
- [System architecture](datahub3-proposed-architecture.md) — error handling and dead-letter
- [Database model](datahub3-database-model.md) — dead_letter table
