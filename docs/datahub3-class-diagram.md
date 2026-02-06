# DataHub 3: Klassediagram (v1)

Domænemodellen for afregningssystemet, opdelt i bounded contexts. Diagrammerne viser de centrale entiteter og deres relationer — ikke den endelige databasemodel, men en konceptuel oversigt der kan drive det første design.

---

## Samlet overblik

```mermaid
classDiagram
    direction LR

    namespace Portefølje {
        class Customer
        class MeteringPoint
        class SupplyPeriod
        class Contract
        class GridArea
    }

    namespace Produkt {
        class Product
        class EnergyModel
    }

    namespace Måledata {
        class MeteringData
        class DailySummary
    }

    namespace Satser {
        class GridTariff
        class TariffRate
        class Subscription
        class ElectricityTax
        class SpotPrice
    }

    namespace Afregning {
        class SettlementRun
        class SettlementLine
        class BillingPeriod
        class AcontoPayment
        class AcontoSettlement
    }

    namespace Fakturering {
        class Invoice
        class InvoiceLine
    }

    namespace DataHubIntegration {
        class InboundMessage
        class OutboundRequest
        class ProcessedMessageId
    }

    namespace Livscyklus {
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

## Portefølje og kunde

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

**Nøglerelationer:**
- En **Customer** har én eller flere **Contracts** (typisk én pr. målepunkt)
- En **Contract** binder en kunde til et **MeteringPoint** og et **Product**
- Et **MeteringPoint** har en historik af **SupplyPeriods** (vi er kun leverandør i den aktive periode)
- Et **MeteringPoint** tilhører et **GridArea** — det bestemmer hvilke tariffer der gælder

---

## Produkt

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

**Produktet bestemmer:**
- **EnergyModel** — hvordan spotprisen håndteres (spot / fast / blanding)
- **MarginOrePerKwh** — leverandørens margin oven på spotprisen
- **SupplementOrePerKwh** — evt. produkttillæg (f.eks. grøn energi)
- **SubscriptionKrPerMonth** — leverandørabonnementet

**Contract** binder et produkt til den specifikke kunde og tilføjer individuelle parametre (betalingsmodel, faktureringsfrekvens, betalingsfrist).

---

## Måledata (tidsserie)

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

    note for MeteringData "Partitioneret pr. måned (PARTITION BY RANGE timestamp)\nSammensat indeks: (metering_point_id, timestamp)\n~230M rækker/måned ved PT15M"
    note for DailySummary "Præ-aggregeret fra rå måledata\nBruges af afregningsmotor\n80K × 30 = 2,4M rækker/måned"
```

**MeteringData** er systemets største tabel. Partitioneret månedligt på `timestamp`. **DailySummary** er en præ-aggregering der reducerer afregningsforespørgsler fra 230M til 2,4M rækker.

---

## Satser og priser

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

    note for TariffRate "HourNumber 1-24 (DB-konvention)\nTidsdifferentierede satser:\ndag/nat/spids"
    note for SpotPrice "Hentes fra ekstern markedsdata-feed\nÉn pris pr. time pr. prisområde (DK1/DK2)"
```

**Priskilder:**
- **GridTariff + TariffRate** — tidsdifferentierede satser fra netvirksomheden (via Charges-kø)
- **Subscription** — faste månedsgebyrer (net + leverandør)
- **ElectricityTax** — lovbestemt elafgift (opdateres årligt)
- **SpotPrice** — Nordpool-timepris (ekstern markedsdata)

---

## Afregning

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

    note for SettlementRun "Uforanderligt snapshot — hver kørsel\nproducerer ny version.\nPartitioneret pr. netområde."
    note for AcontoSettlement "Difference = ActualCost - AcontoPaid\nPositiv = kunde skylder\nNegativ = kunde tilgode"
```

**Afregningsflow:**
1. **SettlementRun** kører for en **BillingPeriod** (pr. netområde for parallelisering)
2. Producerer **SettlementLines** pr. målepunkt pr. ChargeType
3. For acontokunder: **AcontoSettlement** sammenligner faktisk afregning mod **AcontoPayments**
4. Hver kørsel er et **versioneret, uforanderligt snapshot** — genberegninger skaber nye versioner

---

## Fakturering

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

    note for Invoice "Standard = bagudbetaling\nAcontoCombined = opgørelse + nyt aconto\nFinalSettlement = slutfaktura ved offboarding"
```

---

## Livscyklus (tilstandsmaskine)

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

    note for ProcessEvent "Event sourcing — hver\ntilstandsændring er en\nuforanderlig hændelse"
    note for ProcessRequest "Tilstandsmaskine pr. målepunkt:\nPending → SentToDataHub → Acknowledged\n→ EffectuationPending → Completed"
```

**Tilstandsmaskine for leverandørskifte (BRS-001):**

```mermaid
stateDiagram-v2
    [*] --> Pending : Opret anmodning
    Pending --> SentToDataHub : Send BRS-001
    SentToDataHub --> Acknowledged : RSM-009 accepteret
    SentToDataHub --> Rejected : RSM-009 afvist
    Acknowledged --> EffectuationPending : Venter på ikrafttrædelse
    EffectuationPending --> Completed : RSM-007 + RSM-012 modtaget
    Pending --> Cancelled : BRS-003 annullering
    Acknowledged --> Cancelled : BRS-003 annullering
    Rejected --> [*]
    Completed --> [*]
    Cancelled --> [*]
```

---

## DataHub-integration

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

    note for ProcessedMessageId "Idempotenstabel — sikrer at\nsame DataHub-meddelelse\nikke behandles to gange"
    note for DeadLetterMessage "Meddelelser der fejler parsing\neller validering — til manuel\ngennemgang og genforsøg"
```

---

## Relationsoversigt (alle domæner)

```
Customer ──1:*── Contract ──1:1── MeteringPoint ──*:1── GridArea
                    │                    │
                    │ 1:1                │ 1:*
                    ▼                    ▼
                 Product            MeteringData
                                         │
                                         │ aggregeres til
                                         ▼
                                    DailySummary
                                         │
                                         │ bruges af
                                         ▼
GridArea ──1:*── GridTariff ──1:*── TariffRate
    │                                    │
    │ 1:*                                │ afregningsberegning
    ▼                                    ▼
Subscription                      SettlementRun ──1:*── SettlementLine
                                         │
                                         │ driver
                                         ▼
ElectricityTax                      Invoice ──1:*── InvoiceLine
SpotPrice                                │
                                         │ for acontokunder
                                         ▼
                              AcontoPayment / AcontoSettlement
```

---

## Enums samlet

| Enum | Værdier | Bruges af |
|------|---------|-----------|
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

## Kilder

- [Foreslået systemarkitektur](datahub3-proposed-architecture.md) — services, datamodel, teknologivalg
- [Produktopbygning og fakturering](datahub3-product-and-billing.md) — alle fakturaparametre
- [Kundelivscyklus](datahub3-customer-lifecycle.md) — faser og tilstandsovergange
- [Sekvensdiagrammer](datahub3-sequence-diagrams.md) — meddelelsesflows
- [Afregningsoverblik](datahub3-settlement-overview.md) — afregningsberegning
- [Databasemodel](datahub3-database-model.md) — fysisk PostgreSQL/TimescaleDB-skema
