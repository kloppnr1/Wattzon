# DataHub.Exploration

Open-source reference implementation for integrating with the Danish electricity market's DataHub 3 platform. Built for electricity suppliers (DDQ) who need to handle customer onboarding, metering data, and settlement.

## What This Is

A working system that:
- Onboards customers via supplier switch (BRS-001) through DataHub
- Receives and stores metering data (RSM-012) and master data (RSM-007)
- Calculates settlement — energy costs, grid tariffs, government charges, subscriptions, and VAT
- Verifies results against hand-calculated golden master invoices

## Repository Structure

```
docs/                          Documentation (Danish, markdown)
scripts/                       Backlog and utility scripts
DataHub.Settlement/            .NET 9 solution
├── src/
│   ├── DataHub.Settlement.Domain/          Domain entities and value objects
│   ├── DataHub.Settlement.Application/     Use cases and interfaces (IDataHubClient)
│   ├── DataHub.Settlement.Infrastructure/  DB migrations, CIM parsing, HTTP clients
│   ├── DataHub.Settlement.Worker/          Background services (queue polling, settlement)
│   ├── DataHub.Settlement.Api/             REST API (minimal in MVP 1)
│   └── DataHub.Settlement.Web/             Blazor Server dashboard
├── tests/
│   ├── DataHub.Settlement.UnitTests/       Unit tests + FakeDataHubClient
│   └── DataHub.Settlement.IntegrationTests/
├── fixtures/                  CIM JSON test fixtures (planned)
└── docker-compose.yml         TimescaleDB + Aspire Dashboard
```

## Getting Started

### Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/)

### Build and Run

```bash
cd DataHub.Settlement

# Start infrastructure (TimescaleDB + Aspire Dashboard)
docker compose up -d

# Build and run tests
dotnet build
dotnet test

# Run the worker service
dotnet run --project src/DataHub.Settlement.Worker
```

### Aspire Dashboard

Once `docker compose up -d` is running and the worker is started, open **http://localhost:18888** to see:

- **Structured Logs** — all service logs, filterable and searchable
- **Traces** — follow messages through the pipeline (queue poll → parse → store → settle)
- **Metrics** — processing rates, durations, error counts

## Technology

| Component | Technology |
|-----------|-----------|
| Language | C# / .NET 9 |
| Database | PostgreSQL 16 + TimescaleDB |
| Observability | OpenTelemetry → Aspire Dashboard |
| Testing | xunit + FluentAssertions |
| CI/CD | GitHub Actions |
| Containerization | Docker Compose |

## Database

21 tables across 6 schemas, managed by [DbUp](https://dbup.readthedocs.io/) migrations that run automatically at Worker startup.

| Schema | Tables |
|--------|--------|
| `portfolio` | `grid_area`, `customer`, `metering_point`, `product`, `supply_period`, `contract` |
| `metering` | `metering_data` (TimescaleDB hypertable), `spot_price` |
| `tariff` | `grid_tariff`, `tariff_rate`, `subscription`, `electricity_tax` |
| `settlement` | `billing_period`, `settlement_run`, `settlement_line` |
| `datahub` | `inbound_message`, `processed_message_id`, `dead_letter`, `outbound_request` |
| `lifecycle` | `process_request`, `process_event` |

`metering_data` is a TimescaleDB hypertable with monthly partitioning, automatic compression after 3 months, and 5-year retention. SQL migration files live in `src/DataHub.Settlement.Infrastructure/Migrations/`.

## Documentation

All documentation is in the `docs/` folder:

- [Implementation plan](docs/datahub3-implementation-plan.md) — MVP overview and phasing
- [MVP 1 plan](docs/mvp1-implementation-plan.md) — Sunshine scenario tasks and golden master
- [Settlement overview](docs/datahub3-settlement-overview.md) — The three data streams and calculation
- [Database model](docs/datahub3-database-model.md) — Full PostgreSQL/TimescaleDB schema
- [Product and billing](docs/datahub3-product-and-billing.md) — Invoice lines, energy models, aconto
- [Proposed architecture](docs/datahub3-proposed-architecture.md) — Technology choices and services
- [Business processes](docs/datahub3-ddq-business-processes.md) — BRS/RSM message flows

## MVP Roadmap

| MVP | Scope |
|-----|-------|
| **1** | Sunshine scenario: onboard customer → receive data → settlement → golden master |
| **2** | Offboarding, rejections, aconto, HTTP DataHub simulator, invoice document |
| **3** | Corrections, reconciliation, real DataHub (Actor Test), elvarme/solar |
| **4** | ERP integration, customer portal, performance at scale |

## License

This project is open source.
