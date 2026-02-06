# DataHub 3: Class Diagram (v1)

The domain model for the settlement system, divided into bounded contexts. The diagrams show the central entities and their relationships — not the final database model, but a conceptual overview that can drive the initial design.

---

## Full Overview

```mermaid
classDiagram
    direction LR

    namespace Portfolio {
        class Customer
        class MeteringPoint
        class SupplyPeriod
        class Contract
        class GridArea
    }

    namespace Product {
        class Product
        class EnergyModel
    }

    namespace MeteringData {
        class MeteringData
        class DailySummary
    }

    namespace Rates {
        class GridTariff
        class TariffRate
        class Subscription
        class ElectricityTax
        class SpotPrice
    }

    namespace Settlement {
        class SettlementRun
        class SettlementLine
        class BillingPeriod
        class AcontoPayment
        class AcontoSettlement
    }

    namespace Invoicing {
        class Invoice
        class InvoiceLine
    }

    namespace DataHubIntegration {
        class InboundMessage
        class OutboundRequest
        class ProcessedMessageId
    }

    namespace Lifecycle {
        class ProcessRequest
        class ProcessEvent
    }

    Customer "1" --> "*" Contract
    Contract "1" --> "1" MeteringPoint
    Contract "1" --> "1" Product
    MeteringPoint "1" --> "*" SupplyPeriod
    MeteringPoint "1" --> "*" MeteringData
    MeteringPoint "*" --> "1" GridArea
    GridArea "1" --> "*" GridTariff
    SettlementRun "1" --> "*" SettlementLine
    SettlementLine "*" --> "1" MeteringPoint
    Invoice "1" --> "*" InvoiceLine
    Invoice "*" --> "1" Customer
```

---

## Portfolio and Customer

```mermaid
classDiagram
    class Customer {
        +Guid Id
        +string Name
        +string CprCvr
        +ContactType ContactType
        +string Email
        +string Phone
        +DateTime CreatedAt
        +CustomerStatus Status
    }

    class MeteringPoint {
        +string Gsrn
        +MeteringPointType Type
        +SettlementMethod SettlementMethod
        +ConnectionStatus ConnectionStatus
        +string GridAreaCode
        +string GridOperatorGln
        +DateTime? ActivatedAt
        +DateTime? DeactivatedAt
    }

    class SupplyPeriod {
        +Guid Id
        +string Gsrn
        +DateTime StartDate
        +DateTime? EndDate
        +EndReason? EndReason
        +bool IsActive()
    }

    class Contract {
        +Guid Id
        +Guid CustomerId
        +string Gsrn
        +Guid ProductId
        +BillingFrequency BillingFrequency
        +PaymentModel PaymentModel
        +int PaymentTermDays
        +DateTime StartDate
        +DateTime? EndDate
    }

    class GridArea {
        +string Code
        +string GridOperatorGln
        +string GridOperatorName
    }

    class CustomerStatus {
        <<enumeration>>
        Active
        Inactive
        Archived
    }

    class MeteringPointType {
        <<enumeration>>
        E17_Consumption
        E18_Production
    }

    class SettlementMethod {
        <<enumeration>>
        Flex
        NonProfiled
    }

    class ConnectionStatus {
        <<enumeration>>
        Connected
        Disconnected
        ClosedDown
    }

    class BillingFrequency {
        <<enumeration>>
        Monthly
        Quarterly
    }

    class PaymentModel {
        <<enumeration>>
        Aconto
        PostPayment
    }

    class EndReason {
        <<enumeration>>
        SupplierSwitch
        MoveOut
        NonPayment
    }

    Customer "1" --> "*" Contract
    Contract "1" --> "1" MeteringPoint
    MeteringPoint "1" --> "*" SupplyPeriod
    MeteringPoint "*" --> "1" GridArea
    MeteringPoint --> MeteringPointType
    MeteringPoint --> SettlementMethod
    MeteringPoint --> ConnectionStatus
    Contract --> BillingFrequency
    Contract --> PaymentModel
    Customer --> CustomerStatus
    SupplyPeriod --> EndReason
```

**Key relationships:**
- A **Customer** has one or more **Contracts** (typically one per metering point)
- A **Contract** binds a customer to a **MeteringPoint** and a **Product**
- A **MeteringPoint** has a history of **SupplyPeriods** (we are only the supplier during the active period)
- A **MeteringPoint** belongs to a **GridArea** (netområde) — this determines which tariffs apply

---

## Product

```mermaid
classDiagram
    class Product {
        +Guid Id
        +string Name
        +EnergyModel EnergyModel
        +decimal MarginOrePerKwh
        +decimal? SupplementOrePerKwh
        +decimal SubscriptionKrPerMonth
        +int? BindingPeriodMonths
        +bool IsActive
    }

    class EnergyModel {
        <<enumeration>>
        Spot
        FixedPrice
        Mixed
    }

    class Contract {
        +Guid ProductId
        +BillingFrequency BillingFrequency
        +PaymentModel PaymentModel
        +int PaymentTermDays
    }

    Product --> EnergyModel
    Contract "*" --> "1" Product
```

**The product determines:**
- **EnergyModel** — how the spot price is handled (spot / fixed / mixed)
- **MarginOrePerKwh** — the supplier's margin on top of the spot price
- **SupplementOrePerKwh** — optional product supplement (e.g. green energy)
- **SubscriptionKrPerMonth** — the supplier subscription fee (leverandørabonnement)

**Contract** binds a product to the specific customer and adds individual parameters (payment model, billing frequency, payment terms).

---

## Metering Data (Time Series) (Måledata)

```mermaid
classDiagram
    class MeteringData {
        +string MeteringPointId
        +DateTime Timestamp
        +Resolution Resolution
        +decimal QuantityKwh
        +QualityCode Quality
        +string SourceMessageId
        +DateTime ReceivedAt
    }

    class DailySummary {
        +string MeteringPointId
        +DateOnly Date
        +decimal TotalKwh
        +decimal PeakKwh
        +decimal OffPeakKwh
        +decimal? SuperPeakKwh
    }

    class Resolution {
        <<enumeration>>
        PT15M
        PT1H
        P1M
    }

    class QualityCode {
        <<enumeration>>
        A01_NotValidated
        A02_Estimated
        A03_Validated
        A06_Substituted
    }

    MeteringData --> Resolution
    MeteringData --> QualityCode
    MeteringData "*" --> "1" MeteringPoint
    DailySummary "*" --> "1" MeteringPoint

    class MeteringPoint {
        +string Gsrn
    }

    note for MeteringData "Partitioned by month (PARTITION BY RANGE timestamp)\nComposite index: (metering_point_id, timestamp)\n~230M rows/month at PT15M"
    note for DailySummary "Pre-aggregated from raw metering data\nUsed by the settlement engine\n80K x 30 = 2.4M rows/month"
```

**MeteringData** is the system's largest table. Partitioned monthly on `timestamp`. **DailySummary** is a pre-aggregation that reduces settlement queries from 230M to 2.4M rows.

---

## Rates and Prices (Satser og priser)

```mermaid
classDiagram
    class GridTariff {
        +Guid Id
        +string GridAreaCode
        +string ChargeOwnerId
        +TariffType TariffType
        +DateTime ValidFrom
        +DateTime? ValidTo
    }

    class TariffRate {
        +Guid GridTariffId
        +int HourNumber
        +decimal PricePerKwh
    }

    class Subscription {
        +Guid Id
        +string GridAreaCode
        +SubscriptionType Type
        +decimal AmountKrPerMonth
        +DateTime ValidFrom
        +DateTime? ValidTo
    }

    class ElectricityTax {
        +Guid Id
        +decimal RatePerKwh
        +DateTime ValidFrom
        +DateTime? ValidTo
    }

    class SpotPrice {
        +string PriceArea
        +DateTime Hour
        +decimal PricePerKwh
    }

    class TariffType {
        <<enumeration>>
        GridTariff
        SystemTariff
        TransmissionTariff
    }

    class SubscriptionType {
        <<enumeration>>
        GridSubscription
        SupplierSubscription
    }

    GridTariff "1" --> "1..24" TariffRate
    GridTariff --> TariffType
    Subscription --> SubscriptionType
    GridTariff "*" --> "1" GridArea
    Subscription "*" --> "1" GridArea

    class GridArea {
        +string Code
    }

    note for TariffRate "HourNumber 1-24 (DB convention)\nTime-differentiated rates:\nday/night/peak"
    note for SpotPrice "Fetched from external market data feed\nOne price per hour per price area (DK1/DK2)"
```

**Price sources:**
- **GridTariff + TariffRate** — time-differentiated rates from the grid operator (netvirksomhed) (via the Charges queue)
- **Subscription** — fixed monthly fees (grid + supplier)
- **ElectricityTax** (elafgift) — statutory electricity tax (updated annually)
- **SpotPrice** — Nordpool hourly price (external market data)

---

## Settlement (Afregning)

```mermaid
classDiagram
    class BillingPeriod {
        +Guid Id
        +DateTime PeriodStart
        +DateTime PeriodEnd
        +BillingFrequency Frequency
    }

    class SettlementRun {
        +Guid Id
        +Guid BillingPeriodId
        +DateTime ExecutedAt
        +int Version
        +SettlementRunStatus Status
        +string? GridAreaCode
    }

    class SettlementLine {
        +Guid Id
        +Guid SettlementRunId
        +string MeteringPointId
        +ChargeType ChargeType
        +decimal TotalKwh
        +decimal TotalAmount
        +decimal VatAmount
    }

    class AcontoPayment {
        +Guid Id
        +Guid ContractId
        +Guid BillingPeriodId
        +decimal Amount
        +DateTime PaidAt
    }

    class AcontoSettlement {
        +Guid Id
        +Guid ContractId
        +Guid BillingPeriodId
        +decimal ActualCost
        +decimal AcontoPaid
        +decimal Difference
        +decimal NewAcontoAmount
    }

    class ChargeType {
        <<enumeration>>
        Energy
        GridTariff
        SystemTariff
        TransmissionTariff
        ElectricityTax
        GridSubscription
        SupplierSubscription
    }

    class SettlementRunStatus {
        <<enumeration>>
        Running
        Completed
        Failed
    }

    SettlementRun "1" --> "*" SettlementLine
    SettlementRun "*" --> "1" BillingPeriod
    SettlementLine --> ChargeType
    SettlementRun --> SettlementRunStatus
    AcontoPayment "*" --> "1" Contract
    AcontoSettlement "*" --> "1" Contract
    AcontoPayment "*" --> "1" BillingPeriod
    AcontoSettlement "*" --> "1" BillingPeriod

    class Contract {
        +Guid Id
    }

    note for SettlementRun "Immutable snapshot — each run\nproduces a new version.\nPartitioned by grid area."
    note for AcontoSettlement "Difference = ActualCost - AcontoPaid\nPositive = customer owes\nNegative = customer is owed"
```

**Settlement flow:**
1. **SettlementRun** runs for a **BillingPeriod** (per grid area for parallelization)
2. Produces **SettlementLines** per metering point per ChargeType
3. For aconto customers: **AcontoSettlement** (acontoopgørelse) compares actual settlement against **AcontoPayments**
4. Each run is a **versioned, immutable snapshot** — recalculations create new versions

---

## Invoicing (Fakturering)

```mermaid
classDiagram
    class Invoice {
        +Guid Id
        +Guid CustomerId
        +Guid BillingPeriodId
        +InvoiceType Type
        +DateTime IssuedAt
        +DateTime DueDate
        +decimal TotalExVat
        +decimal VatAmount
        +decimal TotalInclVat
        +InvoiceStatus Status
    }

    class InvoiceLine {
        +Guid Id
        +Guid InvoiceId
        +ChargeType ChargeType
        +string Description
        +decimal Quantity
        +decimal UnitPrice
        +decimal Amount
    }

    class InvoiceType {
        <<enumeration>>
        Standard
        AcontoCombined
        FinalSettlement
        CreditNote
        DebitNote
    }

    class InvoiceStatus {
        <<enumeration>>
        Draft
        Issued
        Sent
        Paid
        Overdue
        Cancelled
    }

    Invoice "1" --> "*" InvoiceLine
    Invoice --> InvoiceType
    Invoice --> InvoiceStatus
    Invoice "*" --> "1" Customer
    InvoiceLine --> ChargeType

    class Customer {
        +Guid Id
    }

    class ChargeType {
        <<enumeration>>
        Energy
        GridTariff
        SystemTariff
        TransmissionTariff
        ElectricityTax
        GridSubscription
        SupplierSubscription
    }

    note for Invoice "Standard = post-payment (bagudbetaling)\nAcontoCombined = settlement + new aconto\nFinalSettlement = final invoice at offboarding"
```

---

## Lifecycle (State Machine) (Livscyklus)

```mermaid
classDiagram
    class ProcessRequest {
        +Guid Id
        +ProcessType Type
        +string Gsrn
        +DateTime RequestedAt
        +DateTime? EffectiveDate
        +ProcessStatus Status
        +string? DataHubCorrelationId
    }

    class ProcessEvent {
        +Guid Id
        +Guid ProcessRequestId
        +DateTime OccurredAt
        +string EventType
        +string Payload
    }

    class ProcessType {
        <<enumeration>>
        SupplierSwitch
        ShortNoticeSwitch
        MoveIn
        EndOfSupply
        ForcedEndOfSupply
        MoveOut
        CancelSwitch
        CancelEndOfSupply
        IncorrectSwitch
        IncorrectMove
    }

    class ProcessStatus {
        <<enumeration>>
        Pending
        SentToDataHub
        Acknowledged
        Rejected
        EffectuationPending
        Completed
        Cancelled
    }

    ProcessRequest "1" --> "*" ProcessEvent
    ProcessRequest --> ProcessType
    ProcessRequest --> ProcessStatus

    note for ProcessEvent "Event sourcing — each\nstate change is an\nimmutable event"
    note for ProcessRequest "State machine per metering point:\nPending -> SentToDataHub -> Acknowledged\n-> EffectuationPending -> Completed"
```

**State machine for supplier switch (leverandørskifte) (BRS-001):**

```mermaid
stateDiagram-v2
    [*] --> Pending : Create request
    Pending --> SentToDataHub : Send BRS-001
    SentToDataHub --> Acknowledged : RSM-009 accepted
    SentToDataHub --> Rejected : RSM-009 rejected
    Acknowledged --> EffectuationPending : Awaiting effectuation
    EffectuationPending --> Completed : RSM-007 + RSM-012 received
    Pending --> Cancelled : BRS-003 cancellation
    Acknowledged --> Cancelled : BRS-003 cancellation
    Rejected --> [*]
    Completed --> [*]
    Cancelled --> [*]
```

---

## DataHub Integration

```mermaid
classDiagram
    class InboundMessage {
        +Guid Id
        +string DataHubMessageId
        +string MessageType
        +string CorrelationId
        +string QueueName
        +DateTime ReceivedAt
        +MessageStatus Status
        +string? ErrorDetails
    }

    class OutboundRequest {
        +Guid Id
        +string ProcessType
        +string Gsrn
        +DateTime SentAt
        +string? CorrelationId
        +OutboundStatus Status
    }

    class ProcessedMessageId {
        +string MessageId
        +DateTime ProcessedAt
    }

    class DeadLetterMessage {
        +Guid Id
        +string OriginalMessageId
        +string QueueName
        +string ErrorReason
        +string RawPayload
        +DateTime FailedAt
        +bool Resolved
    }

    class MessageStatus {
        <<enumeration>>
        Received
        Parsed
        Processed
        DeadLettered
    }

    class OutboundStatus {
        <<enumeration>>
        Sent
        AcknowledgedOk
        AcknowledgedError
        TimedOut
    }

    InboundMessage --> MessageStatus
    OutboundRequest --> OutboundStatus

    note for ProcessedMessageId "Idempotency table — ensures that\nthe same DataHub message\nis not processed twice"
    note for DeadLetterMessage "Messages that fail parsing\nor validation — for manual\nreview and retry"
```

---

## Relationship Overview (All Domains)

```
Customer ──1:*── Contract ──1:1── MeteringPoint ──*:1── GridArea
                    │                    │
                    │ 1:1                │ 1:*
                    ▼                    ▼
                 Product            MeteringData
                                         │
                                         │ aggregated into
                                         ▼
                                    DailySummary
                                         │
                                         │ used by
                                         ▼
GridArea ──1:*── GridTariff ──1:*── TariffRate
    │                                    │
    │ 1:*                                │ settlement calculation
    ▼                                    ▼
Subscription                      SettlementRun ──1:*── SettlementLine
                                         │
                                         │ drives
                                         ▼
ElectricityTax                      Invoice ──1:*── InvoiceLine
SpotPrice                                │
                                         │ for aconto customers
                                         ▼
                              AcontoPayment / AcontoSettlement
```

---

## Enums Summary

| Enum | Values | Used by |
|------|--------|---------|
| **MeteringPointType** | E17_Consumption, E18_Production | MeteringPoint |
| **SettlementMethod** | Flex, NonProfiled | MeteringPoint |
| **ConnectionStatus** | Connected, Disconnected, ClosedDown | MeteringPoint |
| **EnergyModel** | Spot, FixedPrice, Mixed | Product |
| **BillingFrequency** | Monthly, Quarterly | Contract |
| **PaymentModel** | Aconto, PostPayment | Contract |
| **Resolution** | PT15M, PT1H, P1M | MeteringData |
| **QualityCode** | A01, A02, A03, A06 | MeteringData |
| **TariffType** | GridTariff, SystemTariff, TransmissionTariff | GridTariff |
| **ChargeType** | Energy, GridTariff, SystemTariff, TransmissionTariff, ElectricityTax, GridSubscription, SupplierSubscription | SettlementLine, InvoiceLine |
| **InvoiceType** | Standard, AcontoCombined, FinalSettlement, CreditNote, DebitNote | Invoice |
| **ProcessType** | SupplierSwitch, ShortNoticeSwitch, MoveIn, EndOfSupply, ... | ProcessRequest |
| **ProcessStatus** | Pending, SentToDataHub, Acknowledged, Rejected, Completed, Cancelled | ProcessRequest |

---

## Sources

- [Proposed system architecture](datahub3-proposed-architecture.md) — services, data model, technology choices
- [Product structure and billing](datahub3-product-and-billing.md) — all invoice parameters
- [Customer lifecycle](datahub3-customer-lifecycle.md) — phases and state transitions
- [Sequence diagrams](datahub3-sequence-diagrams.md) — message flows
- [Settlement overview](datahub3-settlement-overview.md) — settlement calculation
- [Database model](datahub3-database-model.md) — physical PostgreSQL/TimescaleDB schema
