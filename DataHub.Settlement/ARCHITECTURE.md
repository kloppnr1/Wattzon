# DataHub.Settlement — Architecture & Design Rationale

## Overview

DataHub.Settlement is an electricity settlement platform for the Danish energy market. It integrates with Energinet's DataHub — the central hub that coordinates all electricity suppliers, grid companies, and metering operators in Denmark — to handle customer onboarding, metering data collection, and billing.

It handles the full customer lifecycle: a customer signs up with a supplier, DataHub confirms the switch, hourly metering data arrives daily, and the system calculates what the customer owes based on spot prices, grid tariffs, taxes, and subscriptions.

**Tech stack**: .NET 9, TimescaleDB (PostgreSQL 16), Dapper, DbUp, Blazor Server, xUnit

---

## Architecture

The solution follows **Clean Architecture** with strict dependency direction: outer layers depend inward, never the reverse.

```
┌─────────────────────────────────────────────────────┐
│  Web (Blazor)  │  Worker  │  Api  │  Simulator      │  ← Hosts
├─────────────────────────────────────────────────────┤
│              Infrastructure                          │  ← Implementations
├─────────────────────────────────────────────────────┤
│              Application                             │  ← Interfaces & orchestration
├─────────────────────────────────────────────────────┤
│              Domain                                  │  ← Entities & rules
└─────────────────────────────────────────────────────┘
         Dependencies point inward (↑)
```

### Projects

| Project | Role |
|---------|------|
| **Domain** | Entities, value objects, enums, `IClock`. Zero external dependencies. |
| **Application** | Interfaces (`IDataHubClient`, repositories), service contracts, `ProcessStateMachine`. |
| **Infrastructure** | Dapper repositories, Npgsql, DbUp migrations, HTTP DataHub client, dashboard services. |
| **Worker** | Background service host — queue polling, settlement orchestration, process scheduling. |
| **Api** | Minimal API for external access. |
| **Web** | Blazor Server dashboard for visualization, simulation, and operations. |
| **Simulator** | Standalone mock DataHub API for testing without real credentials. |

**Why Clean Architecture?** The domain logic is genuinely complex — settlement math, multi-step state machines, tax threshold rules, regulatory rounding. Isolating this from infrastructure concerns (HTTP, SQL, message queues) keeps the core testable and makes it possible to verify correctness through unit tests without touching a database or network.

---

## Domain Model

### Core Entities

- **Customer**: A household or business receiving electricity supply.
- **MeteringPoint**: The physical meter (identified by GSRN — 18-digit Global Service Relation Number). Can be consumption (E17) or production/solar (E18).
- **SupplyPeriod**: The time range during which we are the active supplier for a metering point.
- **ProcessRequest**: A single DataHub workflow instance (e.g., a change-of-supplier request) tracked through its lifecycle.
- **BillingPeriod**: A date range for which settlement has been calculated.
- **SettlementRun**: One execution of the settlement engine for a billing period.
- **SettlementLine**: Individual charge line items (energy, tariffs, tax) within a settlement run.

### ProcessStateMachine

Every DataHub interaction is an asynchronous multi-step workflow. The state machine enforces valid transitions:

```
pending ──→ sent_to_datahub ──→ acknowledged ──→ effectuation_pending ──→ completed ──→ offboarding ──→ final_settled
                  │                                       │
                  ↓                                       ↓
               rejected                              cancelled
```

- `pending → sent_to_datahub`: BRS message sent to DataHub.
- `sent_to_datahub → acknowledged`: DataHub confirms receipt (RSM-007).
- `acknowledged → effectuation_pending`: Auto-transition; awaiting the effective date.
- `effectuation_pending → completed`: Effective date reached, supply begins. **Temporal guard**: the state machine checks `IClock.Today >= effectiveDate` and blocks early effectuation.
- `completed → offboarding → final_settled`: Customer leaves, final bill calculated.
- `rejected` / `cancelled`: Terminal states for failed or withdrawn requests.

**Why a state machine?** DataHub interactions are inherently asynchronous — you send a BRS request, wait hours or days for acknowledgement, then wait more days until the effective date. Explicit states make every process's current status queryable, prevent invalid transitions (e.g., you can't settle a customer who hasn't been effectuated), and give the UI clear status indicators.

### IClock Abstraction

```csharp
public interface IClock
{
    DateOnly Today { get; }
    DateTime UtcNow { get; }
}
```

Implementations: `SystemClock` (production), `SimulatedClock` (dashboard time-travel).

**Why abstract time?** Business logic depends heavily on "today" — effective dates, metering delivery windows, aconto billing cycles. Without abstraction, testing temporal logic requires waiting real time or fragile date mocking. The `SimulatedClock` also powers the Operations page, where users can step through multi-day business processes day-by-day for demos and verification.

---

## DataHub Integration

### Outbound: BRS (Business Request Services)

We send CIM/XML messages to DataHub to initiate business processes:

| BRS | Purpose |
|-----|---------|
| BRS-001 | Change of Supplier — take over supply for a metering point |
| BRS-002 / BRS-003 | End of Supply — stop supplying a metering point |
| BRS-009 | Move In — new customer at an address |
| BRS-010 | Move Out — customer leaves an address |
| BRS-043 / BRS-044 | Cancel Request — withdraw a pending request |

### Inbound: RSM (Response Messages)

DataHub sends responses via message queues that we poll:

| RSM | Purpose |
|-----|---------|
| RSM-007 | Process acknowledgement or rejection |
| RSM-012 | Metering data delivery — hourly kWh readings for the previous 24 hours |
| RSM-004 | Grid area change notification |
| RSM-014 | Reconciliation data from the grid company |

### Queue Polling

DataHub exposes CIM/XML message queues with a **peek → process → dequeue** pattern. There are no webhooks or push notifications — the only way to receive messages is to poll.

**Why polling?** This isn't a design choice we made; it's the DataHub API contract. The Worker project runs a background service that polls on configurable intervals, peeks at available messages, processes them, and dequeues on success.

### ResilientDataHubClient

Wraps `IDataHubClient` with two resilience behaviors:

1. **401 retry**: On authentication failure, invalidates the cached token via `IAuthTokenProvider.InvalidateToken()`, obtains a fresh token, and retries once.
2. **503 backoff**: On throttling/service unavailable, applies exponential backoff with retry.

**Why a wrapper rather than inline retry?** Token expiry and rate limiting are infrastructure concerns — they're expected in production but irrelevant to business logic. The decorator pattern keeps retry logic in one place and makes the inner client easy to test without resilience noise.

---

## Settlement Engine

### Core Calculation

For each hour `h` in a billing period:

```
energy_cost[h] = consumption_kWh[h] × spot_price_DKK_per_kWh[h]
tariff_cost[h] = consumption_kWh[h] × (grid_tariff[h] + system_tariff + transmission_tariff + electricity_tax)
```

Plus pro-rated monthly subscriptions (grid and supplier).

### Charge Types

| Charge Type | Basis | Source |
|-------------|-------|--------|
| `energy` | Hourly spot price × consumption | Nord Pool via Energi Data Service |
| `grid_tariff` | Hourly rate × consumption | Grid company tariff schedule |
| `system_tariff` | Flat rate × consumption | Energinet |
| `transmission_tariff` | Flat rate × consumption | Energinet |
| `electricity_tax` | Flat rate × consumption (split for elvarme) | Danish tax regulation |
| `grid_subscription` | Monthly fee, pro-rated | Grid company |
| `supplier_subscription` | Monthly fee, pro-rated | Supplier |
| `production_credit` | Negative charge for solar production | Net settlement calculation |

### Special Settlement Scenarios

**Elvarme (electric heating)**: Danish tax regulation provides a reduced electricity tax rate for households with electric heating, but only above 4,000 kWh/year. The `AnnualConsumptionTracker` monitors cumulative yearly consumption per metering point; once the threshold is crossed, subsequent hours are taxed at the lower rate. This split can occur mid-billing-period, requiring per-hour tracking.

**Solar / E18 net settlement**: Prosumer metering points (type E18) have both consumption and production. Settlement calculates net consumption per hour — if production exceeds consumption, the hour generates a `production_credit` (negative charge). This is a regulatory requirement for net-settled solar customers.

**Correction settlement**: Metering companies can retroactively revise historical readings. When revised data arrives, the `CorrectionEngine` computes the delta between old and new readings and produces a correction settlement covering only the difference. The `metering_data_history` table preserves all versions.

**Erroneous switch**: If DataHub determines a customer was wrongly switched away from our supply, `ErroneousSwitchService` issues a credit for the entire period the customer was incorrectly with another supplier.

**Period splitting**: Tariff rates can change on any date. When a tariff change falls within a billing period, `PeriodSplitter` divides the calculation at the change date, applying the correct rate to each sub-period.

**Aconto (pre-payment)**: Quarterly estimated bills based on projected annual consumption (default 4,000 kWh/year). When the actual settlement is calculated, the difference between actual charges and aconto payments already made determines the final amount due.

**Final settlement**: Triggered when a customer leaves (offboarding). Covers the remaining unbilled period from the last settlement through the end of supply.

### Rounding

All financial calculations use `MidpointRounding.ToEven` (banker's rounding). This means 0.425 rounds to 0.42, not 0.43. This is the Danish regulatory standard for financial calculations in the energy sector.

### Golden Master Tests

Ten hand-calculated reference cases (GM#1 through GM#10) cover all settlement scenarios:

| Test | Scenario | Expected Total (DKK) |
|------|----------|---------------------|
| GM#1 | Full January, standard customer | 793.14 |
| GM#2 | Partial period | 409.36 |
| GM#3 | Aconto reconciliation | 893.14 |
| GM#4 | Final settlement on offboarding | 109.36 |
| GM#5 | Correction (delta +0.350 kWh) | 0.91 |
| GM#6 | Erroneous switch (2 months credited) | 1,520.16 |
| GM#7 | Elvarme split-rate tax | 792.36 |
| GM#8 | Solar net settlement (1 day) | 20.10 |
| GM#9 | Correction filtered to supply period | -0.44 (credit) |
| GM#10 | Tariff change mid-period | 830.08 |

These values were calculated by hand using the actual tariff rates, spot prices, and tax rules. They serve as a regression safety net — any change to settlement logic that shifts these values indicates either a bug or a deliberate rule change.

---

## Database

### Why TimescaleDB?

Metering data is hourly time-series — one row per metering point per hour. For a portfolio of thousands of customers, this means millions of rows per month. TimescaleDB's hypertable extension optimizes exactly this pattern: automatic partitioning by time, efficient range queries for billing periods, and built-in compression for historical data. It's PostgreSQL underneath, so we get full SQL, transactions, and ecosystem compatibility.

### Why DbUp?

Migrations are sequential SQL scripts (V001__ through V021__), embedded as assembly resources. DbUp runs them in alphabetical order and tracks which have been applied.

The alternative would be EF Core migrations, but for a system where the schema is designed around time-series queries and PostgreSQL-specific features (hypertables, advisory locks, `jsonb`), hand-written SQL gives full control. There's no risk of an ORM generating unexpected DDL, and migration files are reviewable SQL that matches exactly what runs in production.

### Why Dapper?

Dapper is a micro-ORM — it maps SQL results to C# objects and parameterizes queries, but doesn't generate SQL or track entity state. This matches the DbUp philosophy: we write the SQL, we know what queries run, there are no hidden N+1 problems or unexpected lazy loading.

For a settlement system where query correctness directly affects billing accuracy, explicit SQL is a feature, not a limitation.

### Schema Overview

| Schema | Purpose |
|--------|---------|
| `portfolio` | Customers, metering points, supply periods |
| `metering` | Hourly consumption/production data and revision history |
| `tariff` | Grid tariffs, system tariffs, spot prices |
| `settlement` | Settlement runs and line items |
| `lifecycle` | Process requests and state transition events |
| `datahub` | Message queue state and dead letters |
| `billing` | Aconto payments, erroneous switch corrections |

### Conventions

- **snake_case** for all column and table names (PostgreSQL convention).
- **`timestamptz`** for all timestamps — stored as UTC, no timezone ambiguity.
- **`DateOnly`** mapped via a custom Dapper `TypeHandler` (`DapperTypeHandlers.Register()`) because Dapper doesn't natively handle .NET's `DateOnly`.
- **Npgsql 9** returns `DateTime` (not `DateTimeOffset`) for `timestamptz` columns — DTOs use `DateTime` with `DateTimeKind.Utc`.

### Migrations

21 migration files from `V001__create_extensions.sql` through `V021__message_audit.sql`, covering schema creation, table definitions, hypertable setup, compression policies, and incremental feature additions (aconto, correction settlement, elvarme, solar, erroneous switch).

Notable: `V009__create_hypertable.sql` uses `WithoutTransaction()` because TimescaleDB's `create_hypertable` cannot run inside a transaction.

---

## Testing Strategy

### Unit Tests (~146)

- Settlement engine calculations for all charge types
- Golden master tests (GM#1–GM#10)
- BRS XML builders for all message types
- ProcessStateMachine transition validation
- PeriodSplitter, CorrectionEngine, AnnualConsumptionTracker
- RSM parsers (007, 004, 012, 014)

### Integration Tests (~108)

- Database round-trips for all repository operations
- Full pipeline flows: message receipt → parsing → portfolio update → settlement
- Orchestration services: auto-settlement and auto-effectuation
- Schema validation tests

### Test Isolation

Integration tests that touch the database use `[Collection("Database")]` to prevent parallel execution (shared DB state). The `TestDatabase` helper truncates all tables across all 7 schemas between test classes, ensuring each test starts with a clean database.

### Simulator

The `DataHub.Settlement.Simulator` project is a standalone ASP.NET Minimal API that mocks all DataHub HTTP endpoints. It supports four pre-built scenarios (sunshine, rejection, cancellation, full lifecycle) and is used by both automated tests and the interactive Simulation page.

**Why standalone?** The real DataHub requires production credentials, VPN access, and operates on real customer data. A local mock enables CI testing, local development, and demos without any external dependencies. `WebApplicationFactory` integration tests verify the simulator itself behaves correctly.

---

## Web Dashboard

### Technology

Blazor Server with **Tailwind CSS via CDN** — interactive server-rendered pages without a JavaScript build pipeline. No npm, no webpack, no node_modules.

### Pages

| Page | Purpose |
|------|---------|
| **Dashboard** | Overview of portfolio, processes, and recent activity |
| **Processes** | List and detail view of all DataHub process requests |
| **Messages** | DataHub message queue viewer with raw CIM/XML content |
| **Settlement** | Settlement results with line-item breakdown |
| **Simulation** | Step-by-step scenario runner against the real database |
| **CIM Viewer** | Formatted display of CIM/XML messages |
| **Operations** | Time-travel controls with simulated clock for day-by-day process progression |

### SimulatedClock and Operations

Settlement processes span weeks in real time: a BRS request is sent, acknowledged days later, the effective date may be weeks away, metering data arrives the day after each day ends, and settlement runs after the billing period closes.

The Operations page uses `SimulatedClock` to compress this timeline — users can advance "today" day by day, triggering the same orchestration logic that would run in production. Each day-step processes scheduled effectuations, delivers metering data for the previous day, and runs settlement when periods complete.

**Why?** Without time simulation, demonstrating or testing a full customer lifecycle would require waiting weeks. The simulated clock makes multi-day business processes explorable in minutes.

---

## Key Design Decisions

| Decision | Choice | Reasoning |
|----------|--------|-----------|
| Architecture | Clean Architecture | Domain logic is complex (settlement math, state machines, tax rules); isolating from infrastructure keeps it testable |
| ORM | Dapper | Explicit SQL, no hidden queries, full control over what runs against the database |
| Migrations | DbUp | Sequential SQL scripts with no model drift; PostgreSQL-specific features need hand-written DDL |
| Database | TimescaleDB | Hourly metering data is time-series; hypertables optimize the exact query patterns settlement needs |
| Time | `IClock` abstraction | Business logic depends on "today"; abstraction enables deterministic testing and time-travel UI |
| State tracking | `ProcessStateMachine` | DataHub workflows are async multi-step; explicit states prevent invalid transitions |
| Integration | Queue polling | DataHub's API contract — there are no webhooks |
| Resilience | `ResilientDataHubClient` | Token expiry and throttling are expected; transparent retry keeps business logic clean |
| Rounding | Banker's rounding (`MidpointRounding.ToEven`) | Danish regulatory standard for energy sector financial calculations |
| Test strategy | Golden master tests | Hand-calculated reference values catch rounding and logic regressions immediately |
| Web framework | Blazor Server + Tailwind CDN | Interactive dashboard without a JS build pipeline |
| Mock DataHub | Standalone Simulator project | CI-friendly testing and demos without production credentials |
