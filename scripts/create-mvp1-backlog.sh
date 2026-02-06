#!/usr/bin/env bash
#
# Creates the MVP 1 backlog as GitHub Issues.
#
# Prerequisites:
#   - gh CLI installed and authenticated (gh auth login)
#   - Run from the repository root
#
# Usage:
#   chmod +x scripts/create-mvp1-backlog.sh
#   ./scripts/create-mvp1-backlog.sh
#
# What it does:
#   1. Creates the "mvp-1" milestone
#   2. Creates labels (mvp-1, foundation, integration, settlement, infrastructure, portfolio)
#   3. Creates 17 issues — one per task from docs/mvp1-implementation-plan.md
#
set -euo pipefail

REPO=$(gh repo view --json nameWithOwner -q .nameWithOwner)
echo "Creating MVP 1 backlog in: $REPO"
echo ""

# ── 1. Create milestone ──────────────────────────────────────────────────────

echo "Creating milestone..."
gh api repos/"$REPO"/milestones \
  --method POST \
  --field title="MVP 1: Sunshine Scenario" \
  --field description="Prove the entire sunshine path works end-to-end — from customer signup, through supplier switch (BRS-001), metering data reception, to a verifiable settlement result. Happy path only: no rejections, no cancellations, no offboarding. Delivered outcome: run the sunshine scenario and confirm the settlement matches a hand-calculated reference." \
  --field state=open \
  --silent 2>/dev/null || echo "  (milestone may already exist)"

MILESTONE_NUMBER=$(gh api repos/"$REPO"/milestones --jq '.[] | select(.title=="MVP 1: Sunshine Scenario") | .number')
echo "  Milestone number: $MILESTONE_NUMBER"
echo ""

# ── 2. Create labels ─────────────────────────────────────────────────────────

echo "Creating labels..."
for label_def in \
  "mvp-1:#0E8A16:MVP 1 scope" \
  "foundation:#1D76DB:Project setup, CI/CD, Docker" \
  "integration:#D93F0B:DataHub communication, queues, parsing" \
  "settlement:#7057FF:Settlement engine, calculation, golden master" \
  "data-storage:#FBCA04:Database, repositories, time series" \
  "infrastructure:#BFD4F2:Auth, config, cross-cutting" \
  "portfolio:#0052CC:Customer, metering point, contract, supply period"
do
  IFS=: read -r name color description <<< "$label_def"
  gh label create "$name" --color "${color#\#}" --description "$description" --force 2>/dev/null
  echo "  $name"
done
echo ""

# ── 3. Create issues ─────────────────────────────────────────────────────────

echo "Creating issues..."
echo ""

# --- Issue 1 ---
ISSUE_1=$(gh issue create \
  --title "Task 1: Solution structure + Docker Compose" \
  --milestone "MVP 1: Sunshine Scenario" \
  --label "mvp-1,foundation" \
  --body "$(cat <<'BODY'
## What

Create the .NET solution, project structure, and `docker-compose.yml` for local development.

## Project structure

```
DataHub.Settlement/
├── src/
│   ├── DataHub.Settlement.Domain/          # Domain entities, value objects, enums
│   ├── DataHub.Settlement.Application/     # Use cases, interfaces (IDataHubClient, ISettlementEngine)
│   ├── DataHub.Settlement.Infrastructure/  # DB access, HTTP clients, CIM parsing
│   ├── DataHub.Settlement.Worker/          # Background services (Queue Poller, Settlement Runner)
│   └── DataHub.Settlement.Api/             # REST API (future — minimal in MVP 1)
├── tests/
│   ├── DataHub.Settlement.UnitTests/       # Domain + application tests
│   └── DataHub.Settlement.IntegrationTests/ # DB + full pipeline tests
├── fixtures/                               # CIM JSON fixture files
├── docker-compose.yml                      # PostgreSQL + TimescaleDB
├── Directory.Build.props                   # Shared build configuration
└── DataHub.Settlement.sln
```

## Docker Compose

- **TimescaleDB** (PostgreSQL 16 + TimescaleDB extension) for both time series and relational data
- **.NET Aspire Dashboard** (`mcr.microsoft.com/dotnet/aspire-dashboard`) for runtime monitoring — structured logs, distributed traces, and metrics via OpenTelemetry

## OpenTelemetry

- Add `OpenTelemetry.Extensions.Hosting` + OTLP exporter NuGet packages to the Worker project
- Configure `OTEL_EXPORTER_OTLP_ENDPOINT` pointing at the Aspire Dashboard container
- Dashboard accessible at `http://localhost:18888`

## Dependencies

None — this is the first task.

## Acceptance criteria

- [ ] `dotnet build` succeeds
- [ ] `docker compose up` starts TimescaleDB and it accepts connections
- [ ] `docker compose up` starts Aspire Dashboard accessible at `http://localhost:18888`
- [ ] Worker service logs and traces visible in the Aspire Dashboard
- [ ] All projects reference each other correctly (Domain has no dependencies, Application references Domain, Infrastructure references Application)
- [ ] `.gitignore` for .NET projects

## Estimated effort

Small (1.5 days)

## Reference

[MVP 1 implementation plan — Task 1](../docs/mvp1-implementation-plan.md#task-1-solution-structure--docker-compose)
BODY
)" 2>&1 | tail -1)
echo "  #1: Solution structure + Docker Compose → $ISSUE_1"

# --- Issue 2 ---
ISSUE_2=$(gh issue create \
  --title "Task 2: Database schema (MVP 1 subset)" \
  --milestone "MVP 1: Sunshine Scenario" \
  --label "mvp-1,data-storage" \
  --body "$(cat <<'BODY'
## What

Create database migration scripts for the tables needed in MVP 1. Not the full schema — only what we need to produce one correct invoice.

## Tables needed (21 tables across 6 schemas)

| Schema | Table | Why |
|--------|-------|-----|
| `portfolio` | `grid_area` | Grid area → price area mapping (DK1/DK2) |
| `portfolio` | `metering_point` | GSRN, type, settlement method, grid area |
| `portfolio` | `product` | Margin, subscription, energy model |
| `portfolio` | `customer` | Minimal — name, CPR/CVR |
| `portfolio` | `contract` | Binds customer ↔ metering point ↔ product |
| `metering` | `metering_data` | **Hypertable** — kWh per hour per metering point |
| `metering` | `spot_price` | Nordpool hourly spot prices (DK1/DK2) |
| `tariff` | `grid_tariff` | Grid/system/transmission tariff headers |
| `tariff` | `tariff_rate` | Hourly rates (1-24) per tariff |
| `tariff` | `subscription` | Grid + supplier subscriptions |
| `tariff` | `electricity_tax` | Elafgift rate |
| `settlement` | `billing_period` | Period start/end, frequency |
| `settlement` | `settlement_run` | Run metadata (status, version, executed_at) |
| `settlement` | `settlement_line` | Result per metering point per charge type |
| `datahub` | `inbound_message` | Message log |
| `datahub` | `processed_message_id` | Idempotency |
| `datahub` | `dead_letter` | Failed messages |
| `portfolio` | `supply_period` | When we are active supplier for a GSRN |
| `lifecycle` | `process_request` | BRS-001 process tracking |
| `lifecycle` | `process_event` | Event sourcing for state transitions |
| `datahub` | `outbound_request` | Track BRS-001 requests sent |

## Deferred tables (MVP 2+)

`aconto_payment`, `aconto_settlement`, `invoice`, `invoice_line`, `daily_summary`

## Migration approach

Plain SQL migration files, run in order at container startup. Use DbUp or FluentMigrator — not EF Core Migrations for MVP 1.

## Dependencies

- Depends on: #1 (Solution structure + Docker Compose)

## Acceptance criteria

- [ ] `docker compose up` creates all schemas and tables
- [ ] TimescaleDB extension is enabled
- [ ] `metering_data` is a hypertable partitioned by month
- [ ] Can INSERT and SELECT from all tables
- [ ] DDL matches [database model doc](../docs/datahub3-database-model.md)

## Estimated effort

Small (1 day)

## Reference

[MVP 1 implementation plan — Task 2](../docs/mvp1-implementation-plan.md#task-2-database-schema-mvp-1-subset) | [Database model](../docs/datahub3-database-model.md)
BODY
)" 2>&1 | tail -1)
echo "  #2: Database schema → $ISSUE_2"

# --- Issue 3 ---
ISSUE_3=$(gh issue create \
  --title "Task 3: IDataHubClient interface + FakeDataHubClient" \
  --milestone "MVP 1: Sunshine Scenario" \
  --label "mvp-1,integration" \
  --body "$(cat <<'BODY'
## What

Define the abstraction boundary for all DataHub communication. Implement an in-process fake for unit and integration tests.

## Interface

```csharp
public interface IDataHubClient
{
    Task<DataHubMessage?> PeekAsync(QueueName queue, CancellationToken ct);
    Task DequeueAsync(string messageId, CancellationToken ct);
    Task<DataHubResponse> SendRequestAsync(string processType, string cimPayload, CancellationToken ct);
}

public record DataHubMessage(
    string MessageId,
    string MessageType,
    string? CorrelationId,
    string RawPayload
);

public record DataHubResponse(string CorrelationId, bool Accepted, string? RejectionReason);

public enum QueueName { Timeseries, MasterData, Charges, Aggregations }
```

## Three implementations over time

| Implementation | MVP | Description |
|---------------|-----|-------------|
| `FakeDataHubClient` | **1** | In-process, loads CIM JSON fixtures. No HTTP. Fast. |
| `SimulatorDataHubClient` | 2 | HTTP client pointing at standalone Docker simulator |
| `RealDataHubClient` | 3 | HTTP client pointing at DataHub B2B API with OAuth2 |

Switching between implementations is **configuration**, not code change.

## Dependencies

- Depends on: #1 (Solution structure)

## Acceptance criteria

- [ ] `IDataHubClient` interface defined in Application project (including `SendRequestAsync`)
- [ ] `FakeDataHubClient` in test project, can enqueue and peek fixture messages
- [ ] `FakeDataHubClient` handles `SendRequestAsync` (returns accepted response)
- [ ] Unit test: enqueue a message → peek returns it → dequeue removes it → peek returns null
- [ ] Unit test: send BRS-001 → get accepted DataHubResponse
- [ ] Can load CIM JSON fixture files from disk

## Estimated effort

Small (0.5 day)

## Reference

[MVP 1 implementation plan — Task 3](../docs/mvp1-implementation-plan.md#task-3-idatahubclient-interface--fakedatahubclient)
BODY
)" 2>&1 | tail -1)
echo "  #3: IDataHubClient + Fake → $ISSUE_3"

# --- Issue 4 ---
ISSUE_4=$(gh issue create \
  --title "Task 4: CIM JSON parser (RSM-012)" \
  --milestone "MVP 1: Sunshine Scenario" \
  --label "mvp-1,integration" \
  --body "$(cat <<'BODY'
## What

Parse CIM JSON messages (RSM-012 / NotifyValidatedMeasureData) into domain objects.

## Key parsing rules

- Period start/end are UTC — convert positions to timestamps: `start + (position - 1) × resolution`
- For PT1H: 24 points per day. For PT15M: 96 points per day
- If `quality = A02` and quantity is missing, record as 0 kWh with quality A02
- Validate: GSRN is 18 digits, resolution is known, positions are sequential

## Fixture files to create

| File | Content | Tests |
|------|---------|-------|
| `rsm012-single-day.json` | One GSRN, PT1H, 24 hours, 1 Jan 2025 | Basic parsing |
| `rsm012-multi-day.json` | One GSRN, PT1H, 31 days (January) | Full monthly ingestion |
| `rsm012-missing-quantity.json` | One point has quality A02 (missing) | Missing data handling |
| `charges-tariff-update.json` | Grid tariff rates for grid area 344 | Tariff parsing |

Fixture source: Energinet CIM EDI Guide examples + [opengeh-edi](https://github.com/Energinet-DataHub/opengeh-edi) test data.

## Dependencies

- Depends on: #3 (IDataHubClient — needs `DataHubMessage` record)

## Acceptance criteria

- [ ] `CimJsonParser.ParseRsm012(string json)` returns `ParsedTimeSeries`
- [ ] All fixture files parse without error
- [ ] Unit tests cover: normal data, missing quantities, multiple series in one message
- [ ] CIM JSON structure matches [RSM-012 reference](../docs/rsm-012-datahub3-measure-data.md)

## Estimated effort

Medium (2 days)

## Reference

[MVP 1 implementation plan — Task 4](../docs/mvp1-implementation-plan.md#task-4-cim-json-parser-rsm-012) | [RSM-012 reference](../docs/rsm-012-datahub3-measure-data.md)
BODY
)" 2>&1 | tail -1)
echo "  #4: CIM JSON parser → $ISSUE_4"

# --- Issue 5 ---
ISSUE_5=$(gh issue create \
  --title "Task 5: Metering data storage (TimescaleDB)" \
  --milestone "MVP 1: Sunshine Scenario" \
  --label "mvp-1,data-storage" \
  --body "$(cat <<'BODY'
## What

Persist parsed RSM-012 data into the `metering.metering_data` hypertable. Handle upsert semantics (for future corrections).

## Key design decisions

- Use `INSERT ... ON CONFLICT (metering_point_id, timestamp) DO UPDATE` — correction handling is automatic
- Use Dapper or raw `NpgsqlCommand` for bulk inserts (not EF Core) — performance matters
- Consider `COPY` protocol for bulk loading (Npgsql supports binary COPY)

## Interface

```csharp
public interface IMeteringDataRepository
{
    Task StoreTimeSeriesAsync(string meteringPointId, IReadOnlyList<MeteringDataRow> rows, CancellationToken ct);
    Task<IReadOnlyList<MeteringDataRow>> GetConsumptionAsync(string meteringPointId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
```

## Dependencies

- Depends on: #2 (Database schema), #4 (CIM parser — needs parsed data)

## Acceptance criteria

- [ ] Parse RSM-012 fixture → store → query → data matches
- [ ] Upsert works: storing data for same hour replaces old data
- [ ] Performance: 720 rows (one month PT1H) inserts in < 100ms
- [ ] Integration test with real TimescaleDB (Docker)

## Estimated effort

Small (1 day)

## Reference

[MVP 1 implementation plan — Task 5](../docs/mvp1-implementation-plan.md#task-5-metering-data-storage)
BODY
)" 2>&1 | tail -1)
echo "  #5: Metering data storage → $ISSUE_5"

# --- Issue 6 ---
ISSUE_6=$(gh issue create \
  --title "Task 6: OAuth2 Auth Manager" \
  --milestone "MVP 1: Sunshine Scenario" \
  --label "mvp-1,infrastructure" \
  --body "$(cat <<'BODY'
## What

Implement OAuth2 Client Credentials flow for DataHub B2B API authentication. Can run in parallel with Tasks 4-5 — not strictly needed for MVP 1 (the fake client doesn't authenticate), but implementing early validates the pattern.

## Implementation

```csharp
public interface IAuthTokenProvider
{
    Task<string> GetTokenAsync(CancellationToken ct);
}
```

- POST to Azure AD: `https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token`
- `grant_type=client_credentials&client_id=...&client_secret=...&scope=.../.default`
- Token expires after ~3600 seconds
- Cache token, refresh proactively 5 minutes before expiry
- Single retry on 401

## Dependencies

- Independent — can run in parallel with other tasks
- Depends on: #1 (Solution structure) only

## Acceptance criteria

- [ ] Token is fetched and cached
- [ ] Proactive renewal before expiry
- [ ] Single retry on 401 (token expired during call)
- [ ] All tested against mocked HTTP endpoint — no real Azure AD required

## Estimated effort

Small (1 day)

## Reference

[MVP 1 implementation plan — Task 6](../docs/mvp1-implementation-plan.md#task-6-oauth2-auth-manager) | [Auth and security doc](../docs/datahub3-authentication-security.md)
BODY
)" 2>&1 | tail -1)
echo "  #6: OAuth2 Auth Manager → $ISSUE_6"

# --- Issue 7 ---
ISSUE_7=$(gh issue create \
  --title "Task 7: Spot price fetcher + storage" \
  --milestone "MVP 1: Sunshine Scenario" \
  --label "mvp-1,data-storage" \
  --body "$(cat <<'BODY'
## What

Fetch and store Nordpool hourly spot prices for DK1 and DK2.

## Data source

Energi Data Service (open API, no auth required):

```
GET https://api.energidataservice.dk/dataset/Elspotprices
    ?offset=0
    &start=2025-01-01T00:00
    &end=2025-02-01T00:00
    &filter={"PriceArea":["DK1","DK2"]}
    &sort=HourUTC asc
    &columns=HourUTC,SpotPriceDKK,PriceArea
```

Response contains `SpotPriceDKK` per **MWh** — convert to DKK/kWh (÷ 1000).

## Interface

```csharp
public interface ISpotPriceRepository
{
    Task StorePricesAsync(IReadOnlyList<SpotPriceRow> prices, CancellationToken ct);
    Task<decimal> GetPriceAsync(string priceArea, DateTimeOffset hour, CancellationToken ct);
    Task<IReadOnlyList<SpotPriceRow>> GetPricesAsync(string priceArea, DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
```

**For MVP 1:** Seed the database with known spot prices for the golden master test period. Live API fetch is a bonus.

## Dependencies

- Depends on: #2 (Database schema)

## Acceptance criteria

- [ ] Spot prices stored in `metering.spot_price` table
- [ ] `GetPriceAsync("DK1", hour)` returns correct price
- [ ] Golden master test data includes hardcoded spot prices (deterministic tests)
- [ ] MWh → kWh conversion is correct (÷ 1000)

## Estimated effort

Small (0.5 day)

## Reference

[MVP 1 implementation plan — Task 7](../docs/mvp1-implementation-plan.md#task-7-spot-price-fetcher)
BODY
)" 2>&1 | tail -1)
echo "  #7: Spot price fetcher → $ISSUE_7"

# --- Issue 8 ---
ISSUE_8=$(gh issue create \
  --title "Task 8: Charges parser + tariff storage" \
  --milestone "MVP 1: Sunshine Scenario" \
  --label "mvp-1,data-storage,integration" \
  --body "$(cat <<'BODY'
## What

Parse tariff/charge updates from the Charges queue and store them. For MVP 1, seed the database with known tariffs for the golden master test period.

## Tariff types needed

- Grid tariff (nettarif) — time-differentiated, 24 hourly rates
- System tariff (systemtarif) — flat rate per kWh
- Transmission tariff (transmissionstarif) — flat rate per kWh
- Grid subscription (netabonnement) — fixed DKK/month
- Electricity tax (elafgift) — flat rate per kWh (maintained manually, not from queue)

## Interface

```csharp
public interface ITariffRepository
{
    Task<IReadOnlyList<TariffRateRow>> GetRatesAsync(
        string gridAreaCode, string tariffType, DateOnly date, CancellationToken ct);
    Task<decimal> GetSubscriptionAsync(
        string gridAreaCode, string subscriptionType, DateOnly date, CancellationToken ct);
    Task<decimal> GetElectricityTaxAsync(DateOnly date, CancellationToken ct);
}
```

## Dependencies

- Depends on: #2 (Database schema)

## Acceptance criteria

- [ ] Tariff tables populated with golden master test data
- [ ] `GetRatesAsync("344", "grid", date)` returns 24 hourly rates
- [ ] `GetRatesAsync("344", "system", date)` returns flat rate (same for all 24 hours)
- [ ] `GetSubscriptionAsync("344", "grid", date)` returns monthly amount
- [ ] `GetElectricityTaxAsync(date)` returns elafgift rate
- [ ] Charges queue CIM JSON parsing (bonus for MVP 1)

## Estimated effort

Medium (1.5 days)

## Reference

[MVP 1 implementation plan — Task 8](../docs/mvp1-implementation-plan.md#task-8-charges-parser--tariff-storage) | [Database model — tariff schema](../docs/datahub3-database-model.md)
BODY
)" 2>&1 | tail -1)
echo "  #8: Charges + tariff storage → $ISSUE_8"

# --- Issue 9 ---
ISSUE_9=$(gh issue create \
  --title "Task 9: Queue Poller + idempotency" \
  --milestone "MVP 1: Sunshine Scenario" \
  --label "mvp-1,integration" \
  --body "$(cat <<'BODY'
## What

Background service that continuously polls DataHub queues, routes messages to the correct handler, tracks processed messages, and handles failures.

## Design

```
Poll loop per queue:
1. Peek next message
2. Check processed_message_id table — skip if already seen
3. Route by MessageType:
   - "NotifyValidatedMeasureData" → CIM parser → metering data storage
   - (future: RSM-007, RSM-014, etc.)
4. If success: record in processed_message_id + inbound_message, dequeue
5. If parse error: dead-letter, dequeue (to free the queue)
6. If DB error: do NOT dequeue (retry on next poll)
7. If queue empty (204): wait interval, retry
```

## Idempotency guarantee

- Check `datahub.processed_message_id` before processing
- If found → skip → dequeue
- If not found → process → INSERT → dequeue
- INSERT must happen before dequeue (at-least-once delivery)

## Dependencies

- Depends on: #3 (IDataHubClient), #5 (Metering data storage)
- Related: #6 (OAuth2 Auth Manager — parallel track, used by RealDataHubClient in MVP 3)

## Acceptance criteria

- [ ] Poller runs as `BackgroundService` / `IHostedService`
- [ ] Processes RSM-012 messages end-to-end (peek → parse → store → dequeue)
- [ ] Idempotent: duplicate MessageId → skipped
- [ ] Dead-letter: malformed message → `dead_letter` table + dequeued
- [ ] Inbound message log: all messages recorded in `datahub.inbound_message`
- [ ] Integration test with `FakeDataHubClient`

## Estimated effort

Medium (2 days)

## Reference

[MVP 1 implementation plan — Task 9](../docs/mvp1-implementation-plan.md#task-9-queue-poller--idempotency)
BODY
)" 2>&1 | tail -1)
echo "  #9: Queue Poller + idempotency → $ISSUE_9"

# --- Issue 10 ---
ISSUE_10=$(gh issue create \
  --title "Task 10: Settlement engine" \
  --milestone "MVP 1: Sunshine Scenario" \
  --label "mvp-1,settlement" \
  --body "$(cat <<'BODY'
## What

The core calculation — compute what a customer owes for a billing period, producing 7 charge type lines + VAT.

## Charge types

| # | Charge type | Calculation |
|---|-------------|-------------|
| 1 | **Energy** | `Σ (kWh[hour] × (spot[hour] + margin + supplement))` per hour |
| 2 | **Grid tariff** | `Σ (kWh[hour] × grid_rate[hour_of_day])` per hour (time-differentiated) |
| 3 | **System tariff** | `total_kWh × system_rate` (flat) |
| 4 | **Transmission tariff** | `total_kWh × transmission_rate` (flat) |
| 5 | **Electricity tax** | `total_kWh × elafgift_rate` (flat) |
| 6 | **Grid subscription** | `DKK/month × months` (pro rata for partial) |
| 7 | **Supplier subscription** | `DKK/month × months` (pro rata for partial) |
| 8 | **VAT** | `25% × subtotal` (on sum of lines 1-7) |

## Rounding rules

- Line amounts: **2 decimal places** (DKK)
- VAT: calculated on the **subtotal** (not per line)
- kWh quantities: **3 decimal places** (per CIM spec)

## Pro rata subscriptions (partial periods)

```
subscription × (days_in_period / days_in_month)
```

## Interface

```csharp
public interface ISettlementEngine
{
    Task<SettlementResult> CalculateAsync(SettlementRequest request, CancellationToken ct);
}
```

## Dependencies

- Depends on: #5 (Metering data), #7 (Spot prices), #8 (Tariffs)

## Acceptance criteria

- [ ] Given seeded test data, `CalculateAsync` produces correct `SettlementResult`
- [ ] Each charge type calculated separately as a `SettlementLineResult`
- [ ] Results stored in `settlement.settlement_run` + `settlement.settlement_line`
- [ ] Golden master tests pass (#11)

## Estimated effort

Large (3 days)

## Reference

[MVP 1 implementation plan — Task 10](../docs/mvp1-implementation-plan.md#task-10-settlement-engine) | [Product and billing](../docs/datahub3-product-and-billing.md)
BODY
)" 2>&1 | tail -1)
echo "  #10: Settlement engine → $ISSUE_10"

# --- Issue 11 ---
ISSUE_11=$(gh issue create \
  --title "Task 11: Golden master tests" \
  --milestone "MVP 1: Sunshine Scenario" \
  --label "mvp-1,settlement" \
  --body "$(cat <<'BODY'
## What

Hand-calculated reference invoices that the settlement engine must reproduce **exactly**. These are the most important tests in the system.

## Golden Master #1: Full month (January 2025)

- GSRN: `571313100000012345`, grid area `344` (DK1)
- Product: Spot Standard, margin 4 øre/kWh, subscription 39 DKK/month
- 744 hours, 412.300 kWh total

**Expected result:**

| Charge type | Amount (DKK) |
|-------------|-------------|
| Energy | 392.99 |
| Grid tariff | 116.62 |
| System tariff | 22.26 |
| Transmission tariff | 20.20 |
| Electricity tax | 3.30 |
| Grid subscription | 49.00 |
| Supplier subscription | 39.00 |
| **Subtotal** | **643.37** |
| **VAT (25%)** | **160.84** |
| **Total** | **804.21** |

## Golden Master #2: Partial period (16 days)

Same setup, period 16-31 January. 384 hours, 212.800 kWh.

**Expected result:**

| Charge type | Amount (DKK) |
|-------------|-------------|
| Energy | 202.83 |
| Grid tariff | 60.19 |
| System tariff | 11.49 |
| Transmission tariff | 10.43 |
| Electricity tax | 1.70 |
| Grid subscription (pro rata) | 25.29 |
| Supplier subscription (pro rata) | 20.13 |
| **Subtotal** | **332.06** |
| **VAT (25%)** | **83.02** |
| **Total** | **415.08** |

## Dependencies

- Depends on: #10 (Settlement engine)

## Acceptance criteria

- [ ] Golden Master #1 passes with exact amounts (± 0.00 DKK)
- [ ] Golden Master #2 passes with exact amounts
- [ ] Tests are deterministic — same input always produces same output
- [ ] Test data committed as fixtures in the repository
- [ ] Full hand calculations documented in [mvp1-implementation-plan.md](../docs/mvp1-implementation-plan.md#task-11-golden-master-tests)

## Estimated effort

Medium (2 days)

## Reference

[MVP 1 implementation plan — Task 11](../docs/mvp1-implementation-plan.md#task-11-golden-master-tests)
BODY
)" 2>&1 | tail -1)
echo "  #11: Golden master tests → $ISSUE_11"

# --- Issue 12 ---
ISSUE_12=$(gh issue create \
  --title "Task 12: CI/CD pipeline (GitHub Actions)" \
  --milestone "MVP 1: Sunshine Scenario" \
  --label "mvp-1,foundation" \
  --body "$(cat <<'BODY'
## What

Automated build, test, and (optional) container image push on every commit.

## Pipeline steps

```yaml
# .github/workflows/ci.yml
name: CI

on: [push, pull_request]

jobs:
  build-and-test:
    runs-on: ubuntu-latest
    services:
      postgres:
        image: timescale/timescaledb:latest-pg16
        env:
          POSTGRES_DB: datahub_settlement_test
          POSTGRES_USER: test
          POSTGRES_PASSWORD: test
        ports:
          - 5432:5432
        options: >-
          --health-cmd pg_isready
          --health-interval 10s
          --health-timeout 5s
          --health-retries 5

    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '9.0.x'
      - run: dotnet build --configuration Release
      - run: dotnet test tests/DataHub.Settlement.UnitTests/ --configuration Release
      - run: dotnet test tests/DataHub.Settlement.IntegrationTests/ --configuration Release
```

## Dependencies

- Depends on: All other tasks (this is the last task)

## Acceptance criteria

- [ ] CI runs on every push to any branch
- [ ] CI runs on every pull request
- [ ] Build + unit tests + integration tests all pass
- [ ] Integration tests use a service container (TimescaleDB) — no external dependencies
- [ ] Badge in README showing build status

## Estimated effort

Small (0.5 day)

## Reference

[MVP 1 implementation plan — Task 12](../docs/mvp1-implementation-plan.md#task-12-cicd-pipeline)
BODY
)" 2>&1 | tail -1)
echo "  #12: CI/CD pipeline → $ISSUE_12"

# --- Issue 13 ---
ISSUE_13=$(gh issue create \
  --title "Task 13: Portfolio management" \
  --milestone "MVP 1: Sunshine Scenario" \
  --label "mvp-1,portfolio,data-storage" \
  --body "$(cat <<'BODY'
## What

Create and link the domain entities needed to represent a customer in the system: customer, metering point, contract, supply period. This is the "customer" side that settlement reads from.

## Entities

```csharp
public class Customer
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string CprCvr { get; set; }          // CPR (10) or CVR (8)
    public string ContactType { get; set; }      // "private" or "business"
    public string Status { get; set; }           // "active"
}

public class MeteringPoint
{
    public string Gsrn { get; set; }             // 18 digits
    public string Type { get; set; }             // "E17" (consumption)
    public string SettlementMethod { get; set; } // "flex"
    public string GridAreaCode { get; set; }
    public string PriceArea { get; set; }        // "DK1" or "DK2"
    public string ConnectionStatus { get; set; } // "connected"
}

public class Contract
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public string Gsrn { get; set; }
    public Guid ProductId { get; set; }
    public string BillingFrequency { get; set; } // "monthly"
    public string PaymentModel { get; set; }     // "post_payment"
    public DateOnly StartDate { get; set; }
}

public class SupplyPeriod
{
    public Guid Id { get; set; }
    public string Gsrn { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }       // NULL = active
}
```

## Interface

```csharp
public interface IPortfolioRepository
{
    Task<Customer> CreateCustomerAsync(Customer customer, CancellationToken ct);
    Task<MeteringPoint> CreateMeteringPointAsync(MeteringPoint mp, CancellationToken ct);
    Task<Contract> CreateContractAsync(Contract contract, CancellationToken ct);
    Task<SupplyPeriod> CreateSupplyPeriodAsync(SupplyPeriod period, CancellationToken ct);
    Task<Contract?> GetActiveContractAsync(string gsrn, CancellationToken ct);
    Task ActivateMeteringPointAsync(string gsrn, DateTimeOffset activatedAt, CancellationToken ct);
}
```

For MVP 1 sunshine scenario: CRM creates the customer + contract + GSRN before BRS-001 is sent. When RSM-007 arrives, we set `activated_at` and create the supply period.

## Dependencies

- Depends on: #2 (Database schema)

## Acceptance criteria

- [ ] Can create customer, metering point, contract, supply period
- [ ] Can query active contract for a GSRN
- [ ] Can activate metering point (set `activated_at`, create supply period)
- [ ] Integration test: create full portfolio → query back → data correct

## Estimated effort

Small (1 day)

## Reference

[MVP 1 implementation plan — Task 13](../docs/mvp1-implementation-plan.md#task-13-portfolio-management)
BODY
)" 2>&1 | tail -1)
echo "  #13: Portfolio management → $ISSUE_13"

# --- Issue 14 ---
ISSUE_14=$(gh issue create \
  --title "Task 14: BRS-001 request builder" \
  --milestone "MVP 1: Sunshine Scenario" \
  --label "mvp-1,integration" \
  --body "$(cat <<'BODY'
## What

Build and send a BRS-001 (supplier switch) request to DataHub in CIM JSON format.

## CIM JSON structure (outbound)

```json
{
  "RequestChangeOfSupplier_MarketDocument": {
    "mRID": "request-uuid",
    "process.processType": { "value": "E65" },
    "sender_MarketParticipant.mRID": { "value": "our-gln", "codingScheme": "A10" },
    "sender_MarketParticipant.marketRole.type": { "value": "DDQ" },
    "receiver_MarketParticipant.mRID": { "value": "datahub-gln", "codingScheme": "A10" },
    "receiver_MarketParticipant.marketRole.type": { "value": "DGL" },
    "createdDateTime": "2025-01-15T10:00:00Z",
    "MktActivityRecord": {
      "mRID": "activity-uuid",
      "marketEvaluationPoint.mRID": { "value": "571313100000012345", "codingScheme": "A10" },
      "start_DateAndOrTime.dateTime": "2025-02-01T00:00:00Z",
      "balanceResponsibleParty_MarketParticipant.mRID": { "value": "brp-gln" },
      "customer_MarketParticipant.mRID": { "value": "cpr-or-cvr" }
    }
  }
}
```

## Interface

```csharp
public interface IBrsRequestBuilder
{
    string BuildBrs001(string gsrn, string cprCvr, DateOnly effectiveDate);
}
```

Uses `IDataHubClient.SendRequestAsync` to submit and receive the synchronous acknowledgement.

## Dependencies

- Depends on: #3 (IDataHubClient — needs `SendRequestAsync`)

## Acceptance criteria

- [ ] `BuildBrs001` produces valid CIM JSON
- [ ] Unit test: built JSON has correct structure, GSRN, CPR, effective date
- [ ] Integration test: send to FakeDataHubClient → get accepted response

## Estimated effort

Small (1 day)

## Reference

[MVP 1 implementation plan — Task 14](../docs/mvp1-implementation-plan.md#task-14-brs-001-request-builder)
BODY
)" 2>&1 | tail -1)
echo "  #14: BRS-001 request builder → $ISSUE_14"

# --- Issue 15 ---
ISSUE_15=$(gh issue create \
  --title "Task 15: RSM-009 + RSM-007 parsers" \
  --milestone "MVP 1: Sunshine Scenario" \
  --label "mvp-1,integration" \
  --body "$(cat <<'BODY'
## What

Parse the two new message types needed for the sunshine flow.

**RSM-009 (BRS-001 response):** Already handled as the synchronous response from `SendRequestAsync`. The `DataHubResponse` record captures accepted/rejected + reason.

**RSM-007 (NotifyMasterData):** Arrives on the MasterData queue after activation. Contains metering point details.

## Key fields from RSM-007

| Field | CIM path | Our use |
|-------|----------|---------|
| GSRN | `MarketEvaluationPoint.mRID` | Match to our metering point |
| Grid area | `MarketEvaluationPoint.linkedMarketEvaluationPoint.mRID` | Update grid area assignment |
| Type | `MarketEvaluationPoint.type` | E17 = consumption |
| Settlement method | `MarketEvaluationPoint.settlementMethod` | flex / non-profiled |
| Supply start | `Period.timeInterval.start` | Our supply period start date |
| Grid operator GLN | `MarketEvaluationPoint.inDomain.mRID` | Grid company identity |

## Domain object

```csharp
public record ParsedMasterData(
    string MessageId,
    string MeteringPointId,      // GSRN
    string Type,                 // "E17"
    string SettlementMethod,     // "D01" (flex) or "E02" (non-profiled)
    string GridAreaCode,
    string GridOperatorGln,
    string PriceArea,            // derived from grid area
    DateTimeOffset SupplyStart
);
```

## Fixture files

| File | Content |
|------|---------|
| `rsm007-activation.json` | GSRN 571313100000012345, grid area 344, flex settlement, supply start 2025-01-01 |
| `rsm009-accepted.json` | BRS-001 accepted (if needed as separate message) |

## Dependencies

- Depends on: #3 (IDataHubClient), #4 (CIM parser foundation)

## Acceptance criteria

- [ ] `CimJsonParser.ParseRsm007(string json)` returns `ParsedMasterData`
- [ ] Fixture `rsm007-activation.json` parses correctly
- [ ] Unit tests: all fields extracted, unknown fields tolerated
- [ ] Queue poller routes MasterData messages to RSM-007 parser

## Estimated effort

Medium (1.5 days)

## Reference

[MVP 1 implementation plan — Task 15](../docs/mvp1-implementation-plan.md#task-15-rsm-009-response-parser--rsm-007-master-data-parser)
BODY
)" 2>&1 | tail -1)
echo "  #15: RSM-009 + RSM-007 parsers → $ISSUE_15"

# --- Issue 16 ---
ISSUE_16=$(gh issue create \
  --title "Task 16: Process state machine (BRS-001)" \
  --milestone "MVP 1: Sunshine Scenario" \
  --label "mvp-1,integration,portfolio" \
  --body "$(cat <<'BODY'
## What

Track the lifecycle of a BRS-001 request through the system. Sunshine path only — no rejections or cancellations.

## State transitions

```
[*] → Pending (CRM creates request)
Pending → SentToDataHub (BRS-001 sent)
SentToDataHub → Acknowledged (RSM-009 accepted)
Acknowledged → EffectuationPending (Awaiting activation)
EffectuationPending → Completed (RSM-007 received)
```

## Implementation

```csharp
public class ProcessStateMachine
{
    public ProcessRequest CreateRequest(string gsrn, string processType, DateOnly effectiveDate);
    public void MarkSent(ProcessRequest request, string correlationId);
    public void MarkAcknowledged(ProcessRequest request);
    public void MarkCompleted(ProcessRequest request);
    // MVP 2 adds: MarkRejected, MarkCancelled
}
```

Each state transition creates a `ProcessEvent` (event sourcing for audit trail).

## Integration with other tasks

- Task 14 (BRS-001 builder) calls `MarkSent` after successful send
- Task 15 (RSM-009 parser) calls `MarkAcknowledged` on acceptance
- Task 15 (RSM-007 parser) calls `MarkCompleted` when master data arrives

## Dependencies

- Depends on: #13 (Portfolio), #14 (BRS-001 builder), #15 (RSM parsers)

## Acceptance criteria

- [ ] State machine enforces valid transitions (Pending → Sent → Acknowledged → EffectuationPending → Completed)
- [ ] Invalid transition throws exception
- [ ] Each transition creates an immutable `ProcessEvent`
- [ ] Unit test: full sunshine path transitions
- [ ] Integration test: process request stored + events recorded in DB

## Estimated effort

Small (1 day)

## Reference

[MVP 1 implementation plan — Task 16](../docs/mvp1-implementation-plan.md#task-16-process-state-machine-brs-001)
BODY
)" 2>&1 | tail -1)
echo "  #16: Process state machine → $ISSUE_16"

# --- Issue 17 ---
ISSUE_17=$(gh issue create \
  --title "Task 17: Sunshine scenario end-to-end test" \
  --milestone "MVP 1: Sunshine Scenario" \
  --label "mvp-1,settlement" \
  --body "$(cat <<'BODY'
## What

The capstone test — runs the entire sunshine scenario using `FakeDataHubClient` and verifies the result matches the golden master.

## Test flow

```
1. Seed: product, tariffs, spot prices, electricity tax in DB
2. Arrange: load FakeDataHubClient with RSM-009 (accepted), RSM-007, 31 × RSM-012 fixtures
3. Act: create customer + contract via portfolio service
4. Act: submit BRS-001 → FakeDataHubClient returns accepted
5. Assert: process state = Acknowledged
6. Act: poll MasterData queue → RSM-007 → activate metering point
7. Assert: metering point activated, supply period created, process state = Completed
8. Act: poll Timeseries queue → 31 × RSM-012 → store metering data
9. Assert: 744 rows in metering_data (31 days × 24 hours for January)
10. Act: run settlement engine for the period
11. Assert: settlement lines match Golden Master #1 exactly
```

## This test validates the full chain

CRM → portfolio → BRS-001 → FakeDataHubClient → RSM-009 → RSM-007 → RSM-012 → settlement → golden master

## Dependencies

- Depends on: #11 (Golden master tests), #16 (Process state machine)

## Acceptance criteria

- [ ] End-to-end test passes with Golden Master #1 amounts
- [ ] Test uses `FakeDataHubClient` with fixture files (no HTTP, no Docker dependency)
- [ ] Test is deterministic and fast (< 5 seconds)
- [ ] Test is part of the integration test suite in CI/CD

## Estimated effort

Small (1 day)

## Reference

[MVP 1 implementation plan — Task 17](../docs/mvp1-implementation-plan.md#task-17-sunshine-scenario-end-to-end-test)
BODY
)" 2>&1 | tail -1)
echo "  #17: Sunshine scenario E2E test → $ISSUE_17"

echo ""
echo "Done! Created 17 issues in milestone 'MVP 1: Sunshine Scenario'."
echo ""
echo "View the backlog:"
echo "  gh issue list --milestone 'MVP 1: Sunshine Scenario'"
echo ""
echo "View the milestone:"
echo "  gh issue list --milestone 'MVP 1: Sunshine Scenario' --state all"
