# DataHub 3: Sequence Diagrams for Message Flows

The diagrams show the communication between actors in the most important business processes. Used as a supplement to [Customer Lifecycle](datahub3-customer-lifecycle.md).

**Actors:**
- **Supplier (DDQ)** — electricity supplier (elleverandor)
- **DataHub** — Energinet's central data hub
- **GridOp (DDM/MDR)** — grid operator / metered data responsible (netvirksomhed / maledataansvarlig)
- **Old DDQ** — the outgoing supplier (during a switch)
- **New DDQ** — the incoming supplier (during an incoming switch)
- **Settl** — internal settlement system (afregningssystem)
- **ERP** — billing/ERP system (e.g. D365, SAP, e-conomic)

---

## 1. BRS-001: Supplier Switch (we take over a customer)

The most common onboarding flow. The customer has chosen us as their new supplier.

```mermaid
sequenceDiagram
    autonumber
    participant Salg as Sales/CRM
    participant DDQ as Supplier (DDQ)
    participant DH as DataHub
    participant GmlDDQ as Old DDQ
    participant Netvirk as GridOp (DDM)

    Note over Salg,DDQ: Customer signs contract

    Salg->>DDQ: Create customer record + GSRN
    DDQ->>DH: BRS-001 (RSM-001)<br/>GSRN + effective date + CPR/CVR
    DH-->>DDQ: Receipt (RSM-001): accepted/rejected

    alt Rejected
        DH-->>DDQ: Rejection reason (incorrect GSRN, CPR mismatch, conflict)
        DDQ->>Salg: Error — correct data and resubmit
    end

    DH->>GmlDDQ: Notification: metering point changing supplier
    Note over DH: Waiting until effective date

    DH->>DDQ: RSM-022 (MasterData queue)<br/>Master data snapshot: type, settlement method,<br/>grid area, GLN, connection status
    DH->>DDQ: RSM-012 (Timeseries queue)<br/>First meter data (possibly historical)

    DDQ->>DDQ: Assign tariffs (based on grid area)<br/>Activate metering point in portfolio<br/>Set up billing plan + aconto
```

**Deadlines:** Min. 15 business days notice (BRS-001).

**Cancellation:** Customer withdraws before effective date → send **RSM-002** (Annuller start af leverance) within the same BRS-001 process, referencing the original RSM-001 transaction. DataHub responds with RSM-002 accept/reject. Same correlation ID throughout. Must be submitted no later than the day before the effective date. After the cancellation deadline, use BRS-003 (fejlagtigt leverandørskift) instead.

> **Source:** [Energinet BRS-forretningsprocesser](https://energinet.dk/media/2nqdysv3/brs-forretningsprocesser-for-det-danske-elmarked.pdf), §4.1.9–4.1.10, §4.1.14.

### 1b. BRS-001 Cancellation (before effective date)

```mermaid
sequenceDiagram
    autonumber
    participant DDQ as Supplier (DDQ)
    participant DH as DataHub
    participant GmlDDQ as Old DDQ

    Note over DDQ: Customer withdraws

    DDQ->>DH: RSM-002 (Annuller start af leverance)<br/>Reference = original RSM-001 transaction ID<br/>Same correlation ID as BRS-001
    DH-->>DDQ: RSM-002: accepted/rejected

    alt Accepted
        DH->>GmlDDQ: Cancellation notification
        Note over DDQ: Mark process cancelled<br/>Clean up billing plans
    else Rejected (E17: deadline exceeded)
        Note over DDQ: Use BRS-003 (fejlagtigt<br/>leverandørskift) instead
    end
```

**Validation rules** (§4.1.10):
| Code | Rule |
|------|------|
| E10 | Metering point must be identifiable |
| D05 | Metering point must match original RSM-001 |
| E16 | Supplier must be the same as in original request |
| E17 | Must be within deadline (day before effective date) |
| D06 | Reference must match original transaction ID |
| D19 | Function code must be "Annullering" (cancellation) |

**Note:** BRS-003 (Håndtering af fejlagtigt leverandørskift) is a completely separate process for reversing a switch **after** the effective date. It uses RSM-003 and is initiated by the old/current DDQ — not covered in our current implementation.

---

## 2. RSM-012: Daily Meter Data Flow (operations)

The daily heartbeat — the grid operator reads meters, DataHub validates and forwards.

```mermaid
sequenceDiagram
    autonumber
    participant Netvirk as GridOp (MDR)
    participant DH as DataHub
    participant DDQ as Supplier (DDQ)
    participant Settl as Settlement Engine

    loop Daily (for each metering point)
        Netvirk->>DH: BRS-021: Validated meter data<br/>(kWh per hour/quarter)
        DH->>DH: Validation + schema check
        DH->>DDQ: RSM-012 (Timeseries queue, E66)<br/>ProcessType: E23 (periodic) or D42 (flex)
    end

    DDQ->>DDQ: GET /cim/Timeseries → peek message
    DDQ->>DDQ: Parse CIM JSON:<br/>MeteringPointId, period, resolution,<br/>Point[] (position + quantity + quality)
    DDQ->>DDQ: Store in time series storage
    DDQ->>DH: DELETE /cim/dequeue/{MessageId}

    Note over DDQ,Settl: At billing period end (faktureringsperiode)

    Settl->>Settl: Settlement run:<br/>energy = kWh × (spot + margin)<br/>grid tariff (nettarif) = kWh × tariff rate<br/>product margin = kWh × product rate<br/>+ subscription + charges + VAT
```

**Important fields in RSM-012:**
- `Series/MarketEvaluationPoint/mRID` = GSRN (18 digits)
- `Series/Period/resolution` = PT15M, PT1H or P1M
- `Series/Period/Point/quantity` = kWh (max 3 decimals)
- `Series/Period/Point/quality` = A01/A02/A03/A06

---

## 3. BRS-002: End of Supply (we terminate)

The customer cancels or moves out. We initiate the termination.

```mermaid
sequenceDiagram
    autonumber
    participant DDQ as Supplier (DDQ)
    participant DH as DataHub
    participant Netvirk as GridOp (DDM)
    participant Settl as Settlement Engine
    participant ERP as ERP (billing)

    Note over DDQ: Decision: end of supply<br/>(customer cancellation / non-payment / move-out)

    DDQ->>DH: BRS-002 (RSM-005)<br/>GSRN + effective date + reason
    DH-->>DDQ: Receipt (RSM-001)

    alt Customer withdraws / pays
        DDQ->>DH: BRS-044: Cancel end of supply
        DH-->>DDQ: Receipt: termination cancelled
        Note over DDQ: Supply continues
    end

    Note over DH: Effective date reached

    DH->>DDQ: RSM-012 (Timeseries queue)<br/>Final meter data up to end date

    DDQ->>DDQ: Mark metering point inactive<br/>Register supply period end date

    Settl->>Settl: Final settlement: partial period<br/>energy + tariff + subscription (pro-rated)

    alt Aconto customer
        Settl->>Settl: Aconto settlement (acontoopgorelse):<br/>actual consumption vs. aconto payments
        Settl->>ERP: Credit (overpaid) or<br/>debit (underpaid)
    end

    Settl->>ERP: Final invoice
    ERP->>ERP: Send to customer (e-Boks/email)

    Note over DDQ: Archive customer record (5 years)<br/>Retain meter data (3+ years)
```

**Offboarding scenarios:**
- **Scenario A:** Another supplier sends BRS-001 for our metering point → we receive, not initiate
- **Scenario B/D:** We send BRS-002 (cancellation / non-payment)
- **Scenario C:** Move-out → BRS-010

---

## 4. BRS-001 Incoming: Supplier Switch (we lose a customer)

Another supplier takes over our customer. We are the passive party.

```mermaid
sequenceDiagram
    autonumber
    participant NyDDQ as New DDQ (other supplier)
    participant DH as DataHub
    participant DDQ as Supplier (DDQ)
    participant Settl as Settlement Engine
    participant ERP as ERP (billing)

    NyDDQ->>DH: BRS-001 (RSM-001)<br/>Request our metering point
    DH->>DDQ: Notification: metering point switching<br/>Effective date: DD-MM-YYYY

    Note over DDQ: We cannot block the switch

    Note over DH: Effective date reached

    DH->>DDQ: RSM-012: Final meter data<br/>up to switch date

    DDQ->>DDQ: Mark metering point inactive

    Settl->>Settl: Final settlement (partial period)
    Settl->>ERP: Final invoice + aconto settlement (acontoopgorelse)
    ERP->>ERP: Send final invoice to customer
```

---

## 5. Wholesale Settlement and Reconciliation (BRS-027)

Monthly reconciliation of our own settlement calculations against DataHub's wholesale settlement (engrosopgorelse).

```mermaid
sequenceDiagram
    autonumber
    participant DH as DataHub
    participant DDQ as Supplier (DDQ)
    participant Settl as Settlement Engine

    Note over DH: Monthly wholesale settlement runs

    DH->>DDQ: RSM-014 (Aggregations queue, E31)<br/>Aggregated data per grid area

    DDQ->>DDQ: Peek + parse RSM-014

    Settl->>Settl: Compare:<br/>Own settlement vs. DataHub aggregation

    alt Deviation found
        Settl->>Settl: Identify deviating metering points
        DDQ->>DH: RSM-016: Request detailed<br/>aggregated data for the period
        DH->>DDQ: RSM-014 (response with<br/>OriginalTransactionReference)
        Settl->>Settl: Analyze deviation:<br/>missing meter data? incorrect rates?

        alt Missing meter data
            DDQ->>DH: RSM-015: Request historical<br/>validated data for metering point
            DH->>DDQ: RSM-012 (ProcessType E30)<br/>Historical data
            Settl->>Settl: Recalculate affected periods
        end
    else No deviation
        Note over Settl: ✓ Reconciliation OK
    end

    DDQ->>DH: DELETE /cim/dequeue/{MessageId}
```

**Aggregation types (Warning: VERIFY codes):**
- D03 = Preliminary aggregation (forelobig)
- D04 = Corrected aggregation (korrigeret)
- D05 = Final aggregation (endelig)

---

## 6. Tariff Update (Charges Queue)

The grid operator changes tariff rates — affects future settlement calculations.

```mermaid
sequenceDiagram
    autonumber
    participant Netvirk as GridOp (DDM)
    participant DH as DataHub
    participant DDQ as Supplier (DDQ)
    participant Settl as Settlement Engine

    Netvirk->>DH: Updated tariff rates<br/>(typically annually, 1 Jan / 1 Apr / 1 Oct)
    DH->>DDQ: Charges queue: New tariff rates<br/>Grid area + validity period + rates

    DDQ->>DDQ: Peek + parse Charges message
    DDQ->>DDQ: Update tariff rates:<br/>Rate per hour (hours 1-24)<br/>+ validity dates

    Note over DDQ,Settl: From validity date

    Settl->>Settl: New settlement runs use<br/>updated rates automatically

    Note over Settl: Existing invoices<br/>are NOT affected (unless<br/>a correction is received)

    DDQ->>DH: DELETE /cim/dequeue/{MessageId}
```

---

## 7. Overview: Queue Routing

Overview of which messages arrive in which queues:

```mermaid
flowchart LR
    DH[DataHub B2B API]

    DH -->|GET /cim/Timeseries| TS[Timeseries queue]
    DH -->|GET /cim/Aggregations| AG[Aggregations queue]
    DH -->|GET /cim/MasterData| MD[MasterData queue]
    DH -->|GET /cim/Charges| CH[Charges queue]

    TS --> RSM012[RSM-012: Meter data<br/>E66 / E23,D42,E30]
    TS --> RSM014a[RSM-014: Aggregated data<br/>E31]

    AG --> RSM014b[RSM-014: Aggregated data<br/>E31]

    MD --> RSM022[RSM-022: Master data snapshot]
    MD --> RSM004[RSM-004: Master data change]

    CH --> Tarif[Tariff/price updates]
```

---

## Sources

- [Customer Lifecycle: Onboarding to Offboarding](datahub3-customer-lifecycle.md)
- [Edge Cases and Error Handling](datahub3-edge-cases.md)
- [DataHub 3 DDQ Business Process Reference](datahub3-ddq-business-processes.md)
- [RSM-012 Meter Data Reference](rsm-012-datahub3-measure-data.md)
- [Proposed System Architecture](datahub3-proposed-architecture.md)
- [Authentication and Security](datahub3-authentication-security.md)
- [Energinet BRS-forretningsprocesser for det danske elmarked](https://energinet.dk/media/2nqdysv3/brs-forretningsprocesser-for-det-danske-elmarked.pdf) (Doc. 15/00718-195, primary reference for all BRS/RSM flows)
- CIM Webservice Interface (Doc. 22/03077-1)
- CIM EDI Guide (Doc. 15/00718-191)
