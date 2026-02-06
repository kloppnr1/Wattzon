# Proposed Architecture: Settlement System for DataHub 3

Overall architecture for an open source settlement system operating as an electricity supplier (DDQ) in Energinet DataHub 3. The examples below are sized for ~80,000 customers.

## Data Volume Estimates

| Metric | PT1H (current) | PT15M (future) |
|--------|----------------|----------------|
| Data points per customer per day | 24 | 96 |
| Data points per day (80K customers) | 1.92M | 7.68M |
| Data points per month | ~58M | ~230M |
| Data points per year | ~700M | ~2.8 billion |
| RSM-012 messages per day (avg. 1 per customer) | ~80K | ~80K |
| Bytes per data point (position + quantity + quality) | ~40 B | ~40 B |
| Raw time series storage per year | ~28 GB | ~112 GB |

These volumes drive key decisions around storage engine, partitioning, and ingestion pipeline.

---

## System Context

```
┌───────────────┐         ┌───────────────┐        ┌──────────────┐
│  Backoffice   │         │  Customer     │        │  Billing     │
│  UI           │         │  Portal       │        │  / ERP       │
└──────┬────────┘         └──────┬────────┘        └──────┬───────┘
       │                         │                        │
       └─────────────────┬───────┘────────────────────────┘
                         │
                  ┌──────┴───────┐
                  │   API        │
                  │   Gateway    │
                  └──────┬───────┘
                         │
       ┌─────────────────┼─────────────────┐
       │                 │                 │
┌──────┴──────┐  ┌───────┴──────┐  ┌───────┴───────┐
│ DataHub     │  │ Settlement   │  │ Customer &    │
│ Integration │  │ Engine       │  │ Portfolio     │
│ Service     │  │              │  │ Service       │
└──────┬──────┘  └───────┬──────┘  └───────┬───────┘
       │                 │                 │
       └─────────────────┼─────────────────┘
                         │
              ┌──────────┴──────────┐
              │  Time Series Store  │
              │  + Relational Store │
              └──────────┬──────────┘
                         │
              ┌──────────┴──────────┐
              │  Energinet          │
              │  DataHub 3          │
              │  (B2B API)          │
              └─────────────────────┘
```

---

## Core Services

### 1. DataHub Integration Service

All communication with the DataHub 3 B2B API.

**Sub-components:**
- **Auth Manager** — OAuth2 token lifecycle (fetch, cache, proactive renewal)
- **Queue Poller** — Polls all four peek endpoints on independent intervals
- **Message Parser** — CIM JSON deserialization, schema validation, routing
- **Request Sender** — Builds and sends outbound CIM requests, tracks pending correlations

**Queues polled:**

| Queue | Endpoint | Messages | Business Processes |
|-------|----------|----------|--------------------|
| Timeseries | `GET /cim/Timeseries` | RSM-012, RSM-014 | BRS-020, BRS-021, BRS-027 |
| Aggregations | `GET /cim/Aggregations` | RSM-014 | BRS-027, BRS-028, BRS-029, BRS-030 |
| MasterData | `GET /cim/MasterData` | RSM-004, RSM-007 | BRS-001, BRS-006, BRS-009, BRS-010 |
| Charges | `GET /cim/Charges` | Price/tariff lists | Tariff/charge updates |

**Outbound requests:**

| Action | BRS/RSM |
|--------|---------|
| Supplier switch (leverandørskifte) | BRS-001, BRS-043 |
| End of supply (leveranceophør) | BRS-002, BRS-005 |
| Cancel switch / reversal (tilbageførsel) | BRS-003, BRS-042 |
| Cancel end of supply | BRS-044 |
| Request historical data | RSM-015 |
| Request aggregated data | RSM-016 |
| Submit customer data | BRS-015 |

**Design decisions:**
- Poll specific queues (not `/cim/all`) — independent throughput and fault isolation per message type
- Dequeue only after confirmed persistence — at-least-once delivery guarantee
- Idempotent processing via stored `MessageId` — safe replay after crashes

**Throughput consideration:**
- At 80K RSM-012 messages/day, the Timeseries queue processes ~55 messages/minute on average
- The peek → parse → persist → dequeue cycle must complete in <1 second on average
- Parallel polling of different queues prevents one slow queue from blocking others

### 2. Settlement Engine (Afregningsmotor)

Core business logic: calculates settlement amounts from metering data, tariffs, and market prices.

**Responsibilities:**
- Calculate energy settlement per metering point per interval
- Apply grid tariffs (time-differentiated rates from Charges)
- Apply product margins and subscription fees
- Produce settlement statements per customer per billing period (faktureringsperiode)
- Reconcile against DataHub wholesale settlement (engrosopgørelse) (BRS-027)
- Support recalculation on demand when updated data or rates arrive

**Business processes:**
- **BRS-020** — Consumption statements for profile-settled metering points (profilafregnede målepunkter)
- **BRS-021** — Validated metering data received → triggers settlement calculation
- **BRS-027** — Wholesale settlement results used for reconciliation
- **BRS-028/029/030** — On-demand aggregated data for verification

**Design decisions:**
- Batch-oriented for scheduled settlement runs (nightly/weekly)
- Event-driven recalculation when new metering data or rate changes arrive
- Partitioned by grid area (netområde) — settlement runs execute independently per grid area for parallelization
- Immutable settlement snapshots — each run produces a versioned result, previous versions are preserved

**Volume consideration:**
- A full monthly settlement run at PT15M: 80K customers × 96 points × 30 days = ~230M rows to read and aggregate
- Partition by grid area + month to keep individual queries manageable
- Pre-aggregate daily totals during ingestion to speed up monthly statements

### 3. Customer & Portfolio Service

Manages the supplier's customer portfolio, metering point associations, and lifecycle.

**Responsibilities:**
- Maintain metering point registry (GSRN, type, settlement method, grid area, connection status)
- Track supply periods per metering point (start/end dates, active supplier)
- Process master data updates from DataHub (RSM-004, RSM-007)
- Manage customer records (CPR/CVR, name, contact)
- Orchestrate supplier switch workflows (state machine per metering point)
- Coordinate move-in/move-out flows (til-/fraflytning)

**Business processes:**
- **BRS-001** — Standard supplier switch (leverandørskifte)
- **BRS-002** — End of supply (leveranceophør)
- **BRS-003** — Cancel pending switch
- **BRS-005** — Forced end of supply
- **BRS-006** — Change of balance responsible party (balanceansvarlig)
- **BRS-009** — Move-in (tilflytning)
- **BRS-010** — Move-out (fraflytning)
- **BRS-011** — Erroneous move
- **BRS-015** — Customer master data submission
- **BRS-042** — Erroneous supplier switch (reversal)
- **BRS-043** — Supplier switch with short notice
- **BRS-044** — Cancel end of supply

**Design decisions:**
- State machine per metering point — each BRS process maps to a state transition
- Event sourcing for audit trail — each state change is an immutable event (who, when, why, which BRS)
- Separate read model for fast portfolio queries (e.g., "list all active metering points in grid area 344")

---

## Data Architecture

### Time Series Store

The dominant data volume is metering data. This requires a storage strategy optimized for:
- High write throughput (7.68M inserts/day at PT15M)
- Range queries by metering point + time period
- Aggregation queries across many metering points (settlement runs)

**Recommendation: partitioned relational table (PostgreSQL/TimescaleDB or SQL Server with partitioning)**

**Schema concept:**

```
metering_data
├── metering_point_id   (GSRN, indexed)
├── timestamp           (UTC, part of partition key)
├── resolution          (PT15M / PT1H / P1M)
├── quantity_kwh        (decimal)
├── quality_code        (A01/A02/A03/A06)
├── source_message_id   (DataHub MessageId, for traceability)
├── received_at         (ingestion timestamp)
└── PARTITION BY RANGE (timestamp), monthly
```

**Partitioning strategy:**
- Monthly partitions on `timestamp` — keeps the active partition small, old partitions can be compressed or archived
- Composite index on `(metering_point_id, timestamp)` for point lookups
- At PT15M with 80K customers: ~230M rows/month, ~40 bytes/row = ~9 GB/month raw (before indexes)

**Retention:**
- Hot: current month + 2 previous (active settlement window)
- Warm: 12 months (recalculation window)
- Cold/archive: 3+ years (legal requirement — VERIFY)

### Relational Store

Standard relational database for structured domain data:

| Domain | Key Entities |
|--------|--------------|
| Portfolio | MeteringPoint, Customer, SupplyPeriod, BalanceResponsible |
| Lifecycle | ProcessRequest, ProcessEvent (event-sourced state transitions) |
| Settlement | SettlementRun, SettlementLine, BillingPeriod |
| Rates | GridTariff, ProductMargin, Subscription, ChargeSchedule |
| DataHub | InboundMessage (log), OutboundRequest, PendingCorrelation |
| System | DeadLetter, ProcessedMessageId (idempotency) |

> Details: [Class Diagram](datahub3-class-diagram.md) | [Database Model](datahub3-database-model.md)

### Pre-aggregation Pipeline

For efficient handling of settlement queries over 230M rows/month:

```
Raw metering data (PT15M)
  → Daily aggregation job
    → daily_summary (metering_point_id, date, total_kwh, peak_kwh, off_peak_kwh)
      → Monthly settlement run reads daily summaries instead of raw data
        → 80K × 30 = 2.4M rows instead of 230M
```

For tariff-differentiated settlement (different rates per hour), the daily aggregation groups by tariff period instead of simply summing the day.

---

## API Gateway

Single entry point for all internal and external consumers.

**Consumers:**
- **Backoffice UI** — Portfolio management, settlement review, manual process initiation
- **Customer Portal** — Consumption overview, billing history
- **Billing/ERP** — Export of settlement results, billing triggers

**Key API domains:**

| Domain | Examples |
|--------|----------|
| Portfolio | List metering points, view customer details, search by grid area |
| Lifecycle | Initiate supplier switch, view process status, cancel pending request |
| Metering Data | Query consumption per metering point + period, compare periods |
| Settlement | View settlement run results, trigger recalculation, export to billing |
| Rates | View current tariffs, upload product margins |
| Admin | System health, queue lag, dead-letter review |

> Details: [CIS platform and external systems](datahub3-cis-and-external-systems.md) — API contracts per consumer, integration patterns, event-driven architecture

---

## Cross-cutting Concerns

### Observability

- Structured logging with `CorrelationId` (DataHub) and internal `TraceId`
- Metrics: messages received/sec per queue, settlement run duration, API latency
- Alerts: queue processing lag, DataHub authentication failures, dead-letter growth

### Error Handling and Recovery

#### DataHub Communication Errors

| Scenario | Action |
|----------|--------|
| DataHub 5xx / timeout | Retry with exponential backoff, do **not** dequeue |
| 401 Unauthorized | Fetch new OAuth2 token, retry |
| 403 Forbidden | Check credentials and GLN in the actor portal (aktørportalen) |
| 429 Too Many Requests | Wait and retry with backoff |

#### Message Processing Errors

| Scenario | Action |
|----------|--------|
| Message parsing error (invalid JSON/XML) | Dead-letter, dequeue to free the queue |
| Unknown MessageType | Dead-letter, dequeue, alert operator |
| Business validation error (unknown GSRN etc.) | Log + save for review, dequeue |
| Database error during persistence | Retry, do **not** dequeue (at-least-once guarantee) |

#### Settlement Errors

| Scenario | Action |
|----------|--------|
| Settlement calculation error | Fail the run, alert, preserve partial results for debugging |
| Missing spot prices for the period | Stop settlement run, alert — cannot calculate without prices |
| Missing tariff rates for grid area | Stop for affected metering points, alert |
| Inconsistent metering data (gaps in time series) | Mark affected metering points, recalculate when data is complete |

#### Dead-letter Handling

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

> See also: [Database Model](datahub3-database-model.md) — dead_letter table schema

### Security

- OAuth2 client credentials stored in vault (Azure Key Vault or similar)
- CPR/CVR data encrypted at rest (GDPR)
- Role-based access control on API endpoints
- Audit log for all state-changing operations

> Details: [Authentication and Security](datahub3-authentication-security.md)

### Configuration

| Setting | Purpose |
|---------|---------|
| `DataHub:Environment` | actor test / preprod / prod |
| `DataHub:TenantId` | Azure AD tenant |
| `DataHub:ClientId` / `ClientSecret` | OAuth2 credentials |
| `DataHub:BaseUrl` | API host |
| `DataHub:PollIntervalMs` | Poll interval per queue |
| `DataHub:ActorGLN` | Actor's GLN |
| `Settlement:DefaultResolution` | PT1H (current) or PT15M (future) |
| `Settlement:RetentionMonthsHot` | Active data window |
| `TimeSeries:PartitionScheme` | Monthly / weekly |

---

## Technology Recommendations

### Application Platform

| Option | Suitability | Notes |
|--------|-------------|-------|
| **.NET 9 (recommended)** | Strong | Natural for the team, mature ecosystem for background services (`IHostedService`), strong SQL Server/PostgreSQL drivers, first-class Azure integration |
| Go | Good for ingestion services | High concurrency, small footprint — applicable to Queue Poller if split out as a standalone service |
| Java/Spring | Viable | Widespread in the energy sector, but heavier runtime and slower iteration if the team is .NET-native |

**.NET is a good fit because:**
- `BackgroundService` / `IHostedService` maps directly to the Queue Poller pattern
- `HttpClientFactory` with `DelegatingHandler` for OAuth2 token injection
- EF Core for relational store, Dapper or raw ADO.NET for high-throughput time series ingestion
- Aspire for local dev orchestration of multiple services

### Time Series Database

The most critical choice — it determines whether 230M rows/month can be queried efficiently for settlement.

| Option | Advantages | Disadvantages |
|--------|------------|---------------|
| **TimescaleDB (recommended)** | Purpose-built for time series on top of PostgreSQL. Automatic partitioning (hypertables), built-in compression (90%+ for metering data), continuous aggregates replace the manual pre-aggregation pipeline, standard SQL | Requires PostgreSQL — new if the team only knows SQL Server |
| PostgreSQL + native partitioning | Full control, no extensions. Declarative partitioning works well at this scale | Manual partition management, no built-in compression or continuous aggregates |
| SQL Server with partitioning | Familiar if the team uses SQL Server. Columnstore indexes compress well for analytics | Partition management is more manual, columnstore updates are slower than row-store inserts, licensing cost at scale |
| ClickHouse | Extremely fast for analytical queries, columnar compression | Overkill for 80K customers, not good for point lookups on metering_point_id, requires separate operational expertise |
| InfluxDB | Purpose-built time series | Weaker SQL support, harder to join with relational data, commercial license for clustering |

**Why TimescaleDB:**
- At ~230M rows/month (PT15M), hypertable compression typically achieves 10-15x — reducing 9 GB/month to under 1 GB
- Continuous aggregates automatically maintain the daily/hourly summaries the settlement engine needs
- Standard PostgreSQL underneath — same driver, same tooling, same backup strategy as the relational store
- One database engine for both time series and relational data simplifies operations

### Relational Database

| Option | Suitability | Notes |
|--------|-------------|-------|
| **PostgreSQL (recommended)** | Strong | Free, proven at scale, natural fit with TimescaleDB for a single-engine stack |
| SQL Server | Strong | Better if the organization already operates SQL Server infrastructure and licensing |

If TimescaleDB is chosen for time series, PostgreSQL for the relational store is the natural choice — one database engine to operate.

### Internal Message Bus

For decoupling the DataHub Integration Service from domain handlers:

| Option | Suitability | Notes |
|--------|-------------|-------|
| **In-process channels (recommended to start)** | Good | .NET `Channel<T>` or MediatR. Simplest possible solution when all services run in a single process. No infrastructure dependency |
| RabbitMQ | Good for scaling out | Necessary if services are deployed independently. Durable queues, built-in dead-letter support |
| Azure Service Bus | Good for Azure hosting | Managed, no operational burden. Good match if already on Azure |
| Kafka | Overkill | Designed for far higher throughput. Adds operational complexity not justified at 80K customers |

**Recommendation:** Start with in-process channels. Extract to RabbitMQ or Azure Service Bus if/when services need independent scaling or deployment.

### Hosting & Deployment

| Option | Suitability | Notes |
|--------|-------------|-------|
| **Containerized (Docker + orchestrator)** | Recommended | Each service as a container. Docker Compose for dev, Kubernetes or Azure Container Apps for production |
| Azure App Service | Simple | Good for API Gateway. Less natural for long-running background services (Queue Poller) |
| Windows Service | Viable | The team knows the pattern. Works but limits portability and scaling |
| VMs | Avoid | Manual scaling, no isolation between services |

**Recommendation:** Docker containers from day one. Use Azure Container Apps or Kubernetes in production — health checks, auto-restart, and scaling are built in. Queue Poller and Settlement Engine benefit from running as always-on containers rather than request-driven services.

### Frontend

| Option | Suitability | Notes |
|--------|-------------|-------|
| **React + TypeScript (recommended)** | Strong | The team already uses it. Rich ecosystem for data tables, charts (consumption views), forms |
| Blazor | Viable | .NET end-to-end. Weaker ecosystem for complex data visualization |

### Observability Stack

| Option | Suitability | Notes |
|--------|-------------|-------|
| **OpenTelemetry + Grafana/Loki/Prometheus** | Recommended | Vendor-neutral, .NET has first-class OTel support. Grafana dashboards for queue lag, settlement run metrics |
| Azure Monitor + Application Insights | Good for Azure | Managed, less operational overhead. Built-in .NET integration |
| ELK (Elasticsearch + Kibana) | Viable | Heavier to operate, but powerful for log searching |

### Technology Stack Overview

```
┌─────────────────────────────────────────────────┐
│  Frontend:  React 19 + TypeScript + Vite        │
├─────────────────────────────────────────────────┤
│  API:       .NET 9 (ASP.NET Core)               │
│  Services:  .NET 9 BackgroundService             │
│  Bus:       In-process channels → RabbitMQ       │
├─────────────────────────────────────────────────┤
│  Time Series:  TimescaleDB (on PostgreSQL)      │
│  Relational:   PostgreSQL                       │
├─────────────────────────────────────────────────┤
│  Hosting:       Docker → Azure Container Apps   │
│  Observability: OpenTelemetry + Grafana         │
│  Secrets:       Azure Key Vault                 │
│  CI/CD:         Azure DevOps Pipelines          │
└─────────────────────────────────────────────────┘
```

---

## Estimated Monthly Operating Costs (Azure, West Europe)

All prices are approximate, based on Azure list prices as of early 2025, converted to DKK at a rate of 6.90 kr./USD. Actual costs depend on reserved instances, enterprise agreements, and consumption patterns.

### Three Scenarios

| | Phase 1-2 (Lean start) | Production PT1H | Production PT15M |
|-|------------------------|-----------------|------------------|
| **Customers** | 80,000 | 80,000 | 80,000 |
| **Resolution** | PT1H | PT1H | PT15M |
| **Data points/month** | 58M | 58M | 230M |

#### Compute — Azure Container Apps

| Service | Profile | Lean start | Prod PT1H | Prod PT15M |
|---------|---------|------------|-----------|------------|
| API Gateway | Always-on, 0.5 vCPU / 1 GB | 275 kr. | 275 kr. | 275 kr. |
| DataHub Queue Poller | Always-on, 0.5 vCPU / 1 GB | 275 kr. | 275 kr. | 275 kr. |
| Settlement Engine | Burst, 2 vCPU / 4 GB, ~2h/day | — | 175 kr. | 345 kr. |
| **Compute subtotal** | | **550 kr.** | **725 kr.** | **895 kr.** |

Calculation basis: Azure Container Apps consumption pricing — approx. 0.000166 kr./vCPU-sec, approx. 0.0000207 kr./GiB-sec. Always-on services run 2,592,000 seconds/month. The settlement engine scales up during runs.

#### Database — Azure Database for PostgreSQL Flexible Server

| Component | Lean start | Prod PT1H | Prod PT15M |
|-----------|------------|-----------|------------|
| Compute (TimescaleDB) | Burstable B2ms (2 vCPU, 8 GB) — 690 kr. | GP D4s (4 vCPU, 16 GB) — 1,930 kr. | GP D8s (8 vCPU, 32 GB) — 3,865 kr. |
| Storage (provisioned) | 128 GB — 105 kr. | 256 GB — 205 kr. | 512 GB — 405 kr. |
| Backup (included) | Up to 128 GB | Up to 256 GB | Up to 512 GB |
| **Database subtotal** | **795 kr.** | **2,135 kr.** | **4,270 kr.** |

Notes:
- TimescaleDB compression typically achieves 10-15x on metering data — 230M rows/month (~9 GB raw) compresses to under 1 GB
- After 12 months at PT15M: ~112 GB raw → ~11 GB compressed. Storage headroom is for indexes, uncompressed hot data, and relational tables
- A single PostgreSQL instance covers both time series (TimescaleDB hypertable) and relational data at this scale

#### Supporting Services

| Service | Monthly price | Notes |
|---------|---------------|-------|
| Azure Key Vault | 20 kr. | Secret storage for OAuth2 credentials, connection strings |
| Azure Container Registry (Basic) | 35 kr. | Docker image storage |
| Azure DevOps | 0 kr. | First 5 users free; already in use |
| Grafana Cloud (free tier) | 0 kr. | Up to 10K metric series, 50 GB logs. Self-hosted Grafana in container if exceeded: ~140 kr. |
| DNS / networking | 35 kr. | Negligible at this traffic volume |
| **Supporting subtotal** | **~90 kr.** | |

#### Monthly Totals

| | Lean start | Prod PT1H | Prod PT15M |
|-|------------|-----------|------------|
| Compute | 550 kr. | 725 kr. | 895 kr. |
| Database | 795 kr. | 2,135 kr. | 4,270 kr. |
| Supporting | 90 kr. | 90 kr. | 90 kr. |
| **Total** | **~1,435 kr./mo.** | **~2,950 kr./mo.** | **~5,255 kr./mo.** |
| **Annually** | **~17,220 kr./yr.** | **~35,400 kr./yr.** | **~63,060 kr./yr.** |

### Cost Reduction Opportunities

| Option | Savings | Notes |
|--------|---------|-------|
| 1-year reserved instance (DB) | 30-35% | Reduces Prod PT15M database from 4,270 kr. → ~2,760 kr. |
| 3-year reserved instance (DB) | 50-55% | Reduces Prod PT15M database from 4,270 kr. → ~1,930 kr. |
| Dev/Test pricing | 40-50% | For actor test and preprod environments |
| Self-hosted PostgreSQL on VM | 20-30% | Trade operational effort for lower price — D4s VM at ~965 kr./mo. vs. managed 1,930 kr./mo. |
| Spot instances for settlement engine | 60-80% | Settlement runs can be interrupted and restarted |

### What Is NOT Included

- **Development environments** (actor test, preprod) — multiply by 0.5-0.7x per environment (smaller instances)
- **Personnel/development costs** — by far the dominant cost
- **Billing/ERP integration** — depends on target system (see [CIS and external systems](datahub3-cis-and-external-systems.md))
- **Customer portal hosting** — if separate from backoffice
- **Data transfer (egress)** — Azure charges for outbound data, but volumes are small (~1 GB/mo. DataHub traffic)
- **Disaster recovery / geo-redundancy** — adds ~50% to database costs with zone-redundant HA

### Price per Customer

| | Lean start | Prod PT1H | Prod PT15M |
|-|------------|-----------|------------|
| Monthly price per customer | 0.02 kr. | 0.04 kr. | 0.07 kr. |
| Annual price per customer | 0.22 kr. | 0.44 kr. | 0.79 kr. |

At these volumes, infrastructure cost is negligible compared to business value. The database is the primary cost driver, and reserved instances reduce it significantly.

---

## Implementation Approach

The system is built in **MVPs** (minimum viable products), each delivering a working end-to-end result:

| MVP | Scope | Business Processes |
|-----|-------|--------------------|
| 1 | Sunshine scenario: one customer, one correct invoice | Auth, BRS-001, RSM-009/007/012, Queue Poller, time series store, settlement engine, Charges, state machine, portfolio |
| 2 | Full customer lifecycle (offboarding, aconto, rejections) | RSM-004, BRS-002/003/005/009/010/043/044, aconto, final settlement |
| 3 | DataHub integration + edge cases | Actor Test validation, parser hardening, corrections, BRS-042/011, RSM-014/015/016, reconciliation, elvarme, solar |
| 4 | Production | ERP, payment services, e-Boks, customer portal, pilot + full migration, performance |

> Details: [Implementation plan](datahub3-implementation-plan.md) — MVP details, DataHub simulator, testing strategy

---

## Sources

- [DataHub 3 DDQ Business Process Reference](datahub3-ddq-business-processes.md)
- [RSM-012 Metering Data Reference](rsm-012-datahub3-measure-data.md)
- [Edge Cases and Error Handling](datahub3-edge-cases.md)
- CIM Webservice Interface (Doc. 22/03077-1)
- CIM EDI Guide (Doc. 15/00718-191)
