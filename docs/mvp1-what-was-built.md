# MVP 1: Sunshine Scenario — What Was Built

One customer, one correct invoice. The entire sunshine path end-to-end: from customer signup, through supplier switch, metering data reception, to a verifiable settlement result.

**Delivered:** Run the sunshine scenario — a customer is signed up, the supplier switch is accepted by DataHub (simulator), master data and metering data arrive, and the settlement run produces an invoice that matches a hand-calculated reference (793.14 DKK incl. VAT).

---

## Solution Structure

Seven .NET 9 projects organized in clean architecture:

```
DataHub.Settlement/
├── src/
│   ├── DataHub.Settlement.Domain/          34 lines — IClock, parsed data structures
│   ├── DataHub.Settlement.Application/     792 lines — interfaces, models, business logic
│   ├── DataHub.Settlement.Infrastructure/  3,500+ lines — repositories, clients, parsers, migrations
│   ├── DataHub.Settlement.Worker/          129 lines — background services (queue polling, settlement)
│   ├── DataHub.Settlement.Api/             10 lines — placeholder REST API (/health only)
│   ├── DataHub.Settlement.Web/             Blazor Server dashboard (7 pages)
│   └── DataHub.Settlement.Simulator/       213 lines — mock DataHub HTTP API
├── tests/
│   ├── DataHub.Settlement.UnitTests/       20 test classes
│   └── DataHub.Settlement.IntegrationTests/ 17 test classes
├── fixtures/                               6 CIM JSON test fixtures
└── docker-compose.yml                      TimescaleDB + Simulator + Aspire Dashboard
```

**Dependency flow:** Domain → Application → Infrastructure → Worker/Web/Api/Simulator

---

## Infrastructure Foundation

### Docker Compose Stack

| Service | Image | Port | Purpose |
|---------|-------|------|---------|
| **timescaledb** | `timescale/timescaledb:latest-pg16` | 5432 | PostgreSQL 16 + TimescaleDB for time-series metering data |
| **datahub-simulator** | Custom ASP.NET 9 Minimal API | 5100 | Mock DataHub B2B API endpoints |
| **aspire-dashboard** | `mcr.microsoft.com/dotnet/aspire-dashboard:9.0` | 18888 | OpenTelemetry observability (logs, traces, metrics) |

`docker compose up` starts everything. Database health-checked with `pg_isready`.

### OpenTelemetry Observability

Configured in `Worker/Program.cs`:

| Signal | What's captured |
|--------|----------------|
| **Logging** | All service logs with scopes and formatted messages → OTLP exporter |
| **Tracing** | End-to-end flows (queue poll → parse → store → settle), Npgsql instrumentation |
| **Metrics** | Runtime instrumentation (GC, memory, CPU) |

Service name: `DataHub.Settlement.Worker`. All data flows to Aspire Dashboard on port 18888.

### CI/CD Pipeline

`.github/workflows/ci.yml`:

```
Push/PR → Checkout → .NET 9 → Restore → Build (Release)
  → Unit tests → Integration tests (against TimescaleDB service container)
```

Integration tests run against a real PostgreSQL/TimescaleDB service container in GitHub Actions — no Docker Compose needed in CI.

---

## Database Schema

**21 tables across 6 schemas**, managed by 20 DbUp migrations (V001–V020) that auto-run at Worker startup.

### Portfolio Schema — who the customer is

| Table | Purpose | Key columns |
|-------|---------|-------------|
| `portfolio.grid_area` | Danish grid areas (~40) | code (PK), grid_operator_gln, price_area (DK1/DK2) |
| `portfolio.customer` | End customers | id, name, cpr_cvr, contact_type (private/business), status |
| `portfolio.metering_point` | GSRN metering points | gsrn (PK), type (E17/E18), settlement_method, grid_area_code, connection_status |
| `portfolio.product` | Electricity products | id, name, energy_model (spot/fixed), margin_ore_per_kwh, subscription_kr_per_month |
| `portfolio.supply_period` | When we supply a GSRN | gsrn, start_date, end_date, end_reason |
| `portfolio.contract` | Customer ↔ GSRN ↔ Product | customer_id, gsrn, product_id, billing_frequency, payment_model |

**Unique constraint:** Only one active supply period per GSRN (V013). Only one active process per GSRN (V014).

### Metering Schema — consumption data

| Table | Purpose | Key columns |
|-------|---------|-------------|
| `metering.metering_data` | Hourly consumption (TimescaleDB hypertable) | gsrn, timestamp, resolution (PT1H), quantity_kwh, quality_code |
| `metering.spot_price` | Nord Pool hourly prices | price_area (DK1/DK2), hour, price_dkk_per_kwh |

**TimescaleDB configuration:**
- Hypertable partitioned monthly (V009)
- Automatic compression after 3 months (V010)
- 5-year retention policy (V010)

### Tariff Schema — what the grid company charges

| Table | Purpose | Key columns |
|-------|---------|-------------|
| `tariff.grid_tariff` | Grid company tariff schedules | grid_area_code, valid_from, valid_to |
| `tariff.tariff_rate` | Hourly rates within a tariff | grid_tariff_id, hour (1-24), price_dkk_per_kwh |
| `tariff.subscription` | Fixed monthly fees | type (grid/supplier), amount_dkk_per_month |
| `tariff.electricity_tax` | Elafgift rate | rate_dkk_per_kwh, valid_from |

Grid tariffs are **time-differentiated** — different rates for day (06-21), night (21-06), and peak (17-20).

### Settlement Schema — calculated invoices

| Table | Purpose | Key columns |
|-------|---------|-------------|
| `settlement.billing_period` | Monthly/quarterly periods | start_date, end_date, frequency |
| `settlement.settlement_run` | One run per GSRN per period | gsrn, billing_period_id, grid_area, version |
| `settlement.settlement_line` | Individual charge lines | charge_type (energy/grid_tariff/system_tariff/...), quantity_kwh, rate, amount_dkk |

### DataHub Schema — message tracking

| Table | Purpose | Key columns |
|-------|---------|-------------|
| `datahub.inbound_message` | All received messages | queue, message_type, correlation_id, status |
| `datahub.processed_message_id` | Idempotency log | message_id (unique) |
| `datahub.dead_letter` | Parse failures | error_reason, raw_payload (JSONB) |
| `datahub.outbound_request` | BRS requests sent | process_type, cim_payload, response |

### Lifecycle Schema — process state machine

| Table | Purpose | Key columns |
|-------|---------|-------------|
| `lifecycle.process_request` | BRS process tracking | gsrn, process_type, status, effective_date |
| `lifecycle.process_event` | Audit trail | process_id, from_status, to_status, payload, source |

---

## DataHub Integration Layer

### OAuth2 Token Provider

`OAuth2TokenProvider.cs` — Azure AD client credentials flow.

| Feature | Implementation |
|---------|---------------|
| Token endpoint | `https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token` |
| Caching | Token cached, proactively renewed 5 minutes before expiry |
| Thread safety | `SemaphoreSlim` for concurrent renewal |
| Configuration | TenantId, ClientId, ClientSecret, Scope |

### DataHub Client Interface

```
IDataHubClient
  ├── PeekAsync(queue) → DataHubMessage?         // Look at next message
  ├── DequeueAsync(messageId) → void              // Remove after processing
  └── SendRequestAsync(processType, cimPayload) → DataHubResponse
```

**Three implementations:**
- `HttpDataHubClient` — real HTTP calls to DataHub (or simulator)
- `StubDataHubClient` — in-memory queues for unit/integration tests
- `ResilientDataHubClient` — decorator with 401 token refresh + 503 retry

### BRS Request Builder

`BrsRequestBuilder.cs` — constructs CIM JSON for all outbound requests:

| Method | BRS | Process type | Purpose |
|--------|-----|-------------|---------|
| `BuildBrs001()` | BRS-001 | E65 | Supplier switch |
| `BuildBrs002()` | BRS-002 | E03 | End of supply |
| `BuildBrs003()` | BRS-003 | E65 | Cancel switch |
| `BuildBrs009()` | BRS-009 | E01 | Move-in |
| `BuildBrs010()` | BRS-010 | E01 | Move-out |
| `BuildBrs042()` | BRS-042 | E34 | Erroneous switch reversal |
| `BuildBrs043()` | BRS-043 | E66 | Short notice switch |
| `BuildBrs044()` | BRS-044 | E03 | Cancel termination |

All messages use GLN identifiers (Our: 5790002000000, DataHub: 5790001330552).

### CIM JSON Parser

`CimJsonParser.cs` — parses inbound DataHub messages:

| Parser | Message | What's extracted |
|--------|---------|-----------------|
| `ParseRsm012()` | Metering data | GSRN, period, hourly quantities, quality codes (A01/A02/A03/A06) |
| `ParseRsm007()` | Master data | GSRN, type (E17/E18), settlement method, grid area, supply start |
| `ParseRsm004()` | Grid area change | GSRN, new grid area code |
| `ParseRsm014()` | Aggregation | Grid area, period, total kWh |

Grid area → price area mapping: codes ≤ 550 → DK1, codes > 550 → DK2.

---

## Queue Polling Pipeline

`QueuePollerService.cs` — background service polling 4 DataHub queues every 5 seconds.

```
Poll cycle (every 5 seconds):
  For each queue [Timeseries, MasterData, Charges, Aggregations]:
    1. Peek next message
    2. Check idempotency (processed_message_id table)
    3. Parse CIM JSON → domain objects
    4. Store in database
    5. Mark message ID as processed
    6. Dequeue from DataHub
```

**At-least-once delivery:** Message is marked processed in DB _before_ dequeue. If dequeue fails, the message will be re-peeked but skipped by idempotency check.

**Dead-lettering:** Parse errors (FormatException, JsonException, ArgumentException) → message stored in `datahub.dead_letter` with error reason and raw payload → message dequeued to unblock the queue.

**Database errors:** Propagated (not caught) → message stays in queue → retried on next poll cycle.

---

## Process State Machine

`ProcessStateMachine.cs` — lifecycle management for BRS processes.

```
pending ──────────────→ sent_to_datahub ──→ acknowledged ──→ effectuation_pending
    │                        │                                       │
    └→ cancelled             └→ rejected                             ├→ completed ──→ offboarding ──→ final_settled
                                                                     └→ cancelled
```

**9 states:** pending, sent_to_datahub, acknowledged, effectuation_pending, completed, offboarding, final_settled, rejected, cancelled.

**Terminal states:** rejected, cancelled, final_settled.

**Temporal guard:** `MarkCompleted()` validates that effective date ≤ today (via `IClock`).

**Audit trail:** Every transition creates a `process_event` record with from/to status, payload, and source (datahub/system).

**Process types:** supplier_switch, short_notice_switch, move_in, move_out, end_of_supply, forced_end_of_supply, cancel_switch, cancel_end_of_supply, incorrect_switch, incorrect_move.

---

## Settlement Engine

`SettlementEngine.cs` — the core financial calculation.

**Input:** `SettlementRequest` containing consumption, spot prices, tariff rates, product parameters.

**Calculation per hour:**

```
For each hour in the billing period:
  energy         = kWh × (spot_price_dkk + margin_dkk + supplement_dkk)
  grid_tariff    = kWh × grid_rate[hour_of_day]
  system_tariff  = kWh × system_rate
  transmission   = kWh × transmission_rate
  electricity_tax = kWh × tax_rate
  subscription   = (grid_sub + supplier_sub) × (days / days_in_month) / hours_in_period

Subtotal = Σ all hourly amounts per charge type
VAT      = 25% of subtotal
Total    = subtotal + VAT
```

**Rounding:** Each line rounded to 2 decimals independently. VAT rounded last. Uses `MidpointRounding.ToEven` (banker's rounding per Danish regulations).

**Key design:** The engine is **deterministic and pure** — all inputs as parameters, no implicit state. This makes it trivially testable with golden master data.

---

## Golden Master Tests

`GoldenMasterTests.cs` — 10 hand-calculated reference invoices that the settlement engine must reproduce exactly.

### Test data constants (used across all tests)

| Parameter | Value |
|-----------|-------|
| Consumption: night (00-05) | 0.300 kWh/hour |
| Consumption: day (06-15) | 0.500 kWh/hour |
| Consumption: peak (16-19) | 1.200 kWh/hour |
| Consumption: late (20-23) | 0.400 kWh/hour |
| Daily total | 13.200 kWh |
| Spot price: night | 45 øre/kWh |
| Spot price: day | 85 øre/kWh |
| Spot price: peak | 125 øre/kWh |
| Spot price: late | 55 øre/kWh |
| Grid tariff: night | 0.06 DKK/kWh |
| Grid tariff: day | 0.18 DKK/kWh |
| Grid tariff: peak | 0.54 DKK/kWh |
| System tariff | 0.054 DKK/kWh |
| Transmission tariff | 0.049 DKK/kWh |
| Electricity tax (elafgift) | 0.008 DKK/kWh |
| Grid subscription | 49.00 DKK/month |
| Supplier subscription | 39.00 DKK/month |
| Product margin | 4 øre/kWh (0.04 DKK) |

### GM#1 — Full January (sunshine scenario)

31 days, 744 hours, 409.200 kWh total.

| Charge | Amount (DKK) |
|--------|-------------|
| Energy (spot + margin) | 386.51 |
| Grid tariff | 114.58 |
| System tariff | 22.10 |
| Transmission | 20.05 |
| Electricity tax | 3.27 |
| Grid subscription | 49.00 |
| Supplier subscription | 39.00 |
| **Subtotal** | **634.51** |
| VAT (25%) | 158.63 |
| **Total** | **793.14** |

### GM#2 — Partial January (mid-month start)

January 16–31, 16 days, pro-rata subscriptions. Total: **409.36 DKK**.

---

## Sunshine Scenario End-to-End Test

`SunshineScenarioTests.cs` — proves the entire MVP 1 pipeline works.

```
Step 1:  Seed grid area 344, tariffs, spot prices for January
Step 2:  Create customer "Anders Hansen", product "Spot Standard"
Step 3:  Create metering point, contract, supply period
Step 4:  Submit BRS-001 supplier switch → process state = sent_to_datahub
Step 5:  Simulate RSM-009 accepted → state = acknowledged → effectuation_pending
Step 6:  Poll MasterData queue → RSM-007 → activate metering point
Step 7:  Mark effectuated → state = completed
Step 8:  Poll Timeseries queue → RSM-012 × 31 days → 744 hourly readings stored
Step 9:  Assert: 744 rows in metering_data table
Step 10: Run settlement engine with all tariffs and spot prices
Step 11: Assert: result matches Golden Master #1 (793.14 DKK total)
```

---

## DataHub Simulator

`Simulator/Program.cs` — standalone ASP.NET 9 Minimal API mimicking DataHub.

### Endpoints

| Endpoint | Purpose |
|----------|---------|
| `POST /oauth2/v2.0/token` | Returns fake bearer token |
| `GET /v1.0/cim/{queue}` | Peek next message (204 if empty) |
| `DELETE /v1.0/cim/dequeue/{messageId}` | Remove message from queue |
| `POST /v1.0/cim/request*` | Accept BRS requests, return correlation ID |
| `POST /admin/scenario/{name}` | Load predefined scenario |
| `POST /admin/enqueue` | Manually enqueue a message |
| `POST /admin/reset` | Clear all state |
| `GET /admin/requests` | View outbound request audit trail |

### Predefined Scenarios

| Scenario | Messages enqueued |
|----------|------------------|
| `sunshine` | RSM-007 (master data) + RSM-012 (31 days metering) |
| `rejection` | RSM-007-REJECT (error code E16) |
| `cancellation` | RSM-007 (for cancel flow testing) |
| `full_lifecycle` | RSM-007 + RSM-012 (Jan) + RSM-004 (grid change) + RSM-012 (Feb partial) |
| `move_in` | RSM-007 + RSM-012 (Jan) |
| `move_out` | RSM-007 + RSM-012 (Jan) + RSM-012 (Feb partial) |

### CIM JSON Fixtures

| Fixture | Size | Content |
|---------|------|---------|
| `rsm007-activation.json` | 806 B | Master data with GSRN, grid area, settlement method |
| `rsm012-single-day.json` | 2.5 KB | 24-hour consumption |
| `rsm012-multi-day.json` | 91 KB | 31-day consumption (January, 744 hours) |
| `rsm012-missing-quantity.json` | 1.1 KB | Edge case: missing quantity field |
| `rsm004-grid-area-change.json` | 716 B | Grid area 344 → 391 |
| `brs001-receipt-rejected.json` | 470 B | BRS rejection with error code |

---

## Blazor Dashboard

7-page Blazor Server application for development and operations.

| Page | Route | Features |
|------|-------|----------|
| **Dashboard** | `/` | KPI cards (customers, messages, processes, runs), recent activity, seed demo data |
| **Processes** | `/processes` | BRS process list with state filtering |
| **Messages** | `/messages` | DataHub message queue viewer, raw JSON payloads |
| **Settlement** | `/settlement` | Settlement results with charge breakdown |
| **Simulation** | `/simulation` | Scenario selector with step-by-step execution |
| **CIM Viewer** | `/cim-viewer` | Formatted CIM JSON display |
| **Operations** | `/operations` | Time-travel controls (advance simulated clock day-by-day) |

**Key services:** `DashboardQueryService` (SQL queries), `DemoDataSeeder` (test data), `SimulationService` (2,655 lines — full scenario engine), `SimulatedClock` (controllable time).

---

## Background Services

Three `IHostedService` implementations running in the Worker:

| Service | Interval | Responsibility |
|---------|----------|----------------|
| `QueuePollerService` | 5 seconds | Poll 4 DataHub queues, parse messages, store data |
| `ProcessSchedulerService` | 1 minute | Check effectuation_pending processes, mark completed when effective date reached |
| `SettlementOrchestrationService` | 5 minutes | Check completed processes, verify metering completeness, run settlement engine |

All services start on Worker startup and run until graceful shutdown. Migrations run before any service starts.

---

## Architecture Patterns

| Pattern | Implementation |
|---------|---------------|
| **Clean Architecture** | Domain → Application → Infrastructure → Hosts |
| **Repository pattern** | All database access behind interfaces |
| **Decorator pattern** | `ResilientDataHubClient` wraps `HttpDataHubClient` |
| **State machine** | Process lifecycle with validated transitions |
| **Golden master testing** | Hand-calculated reference invoices |
| **At-least-once delivery** | DB commit before dequeue |
| **Idempotent processing** | `processed_message_id` table |
| **Dead-letter pattern** | Unparseable messages stored for investigation |
| **Interface-driven** | `IDataHubClient`, `IClock`, `ICimParser` — testability by design |
| **Dapper micro-ORM** | Explicit SQL, no ORM magic, fast queries |
| **DbUp migrations** | Sequential SQL scripts, auto-run at startup |

---

## What MVP 1 Delivered

- Full sunshine scenario: customer signup → BRS-001 → RSM-009 → RSM-007 → RSM-012 → settlement → verified invoice
- Settlement engine producing correct invoices (golden master verified)
- OAuth2 token management with caching and renewal
- CIM JSON parsing for RSM-007, RSM-012, RSM-004
- Queue polling with idempotency and dead-lettering
- Process state machine with 9 states and audit trail
- Portfolio model (customer, product, metering point, contract, supply period)
- TimescaleDB time-series storage with compression and retention
- OpenTelemetry observability via Aspire Dashboard
- Blazor Server dashboard for development
- DataHub simulator with scenario engine
- CI/CD pipeline with unit + integration tests
- `docker compose up` starts everything

**~14,500 lines total** (7,417 source + 6,235 tests + 850 migrations)
