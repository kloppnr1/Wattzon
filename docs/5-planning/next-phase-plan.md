# Current State & Remaining Work

> **Updated:** 2026-02-17. Replaces previous "Next Development Phase" plan which was outdated.

---

## What's Built

The system is significantly more complete than earlier planning documents anticipated. Here's what exists and works:

### Settlement Engine (Complete)
- Hourly spot + margin calculation, grid tariffs (time-differentiated), system/transmission tariffs, elafgift, subscriptions (pro-rata), VAT (25%)
- Banker's rounding (MidpointRounding.ToEven) per Danish regulatory standard
- 10 golden master tests (GM#1–GM#10) covering: simple spot, partial period, aconto, corrections, elvarme threshold, solar/E18, mid-period tariff split, reconciliation
- `PeriodSplitter` for tariff changes mid-billing period
- `SpotPriceValidator` for missing price detection
- `AnnualConsumptionTracker` for elvarme 4,000 kWh threshold

### DataHub Integration (Mostly Complete)
- 4 queue pollers: Timeseries, MasterData, Charges, Aggregations
- RSM-012 parsing + storage with history tracking and correction detection
- RSM-022 effectuation (master data → metering point creation + activation)
- RSM-004 master data change notifications
- RSM-034 tariff price parsing (grid, system, transmission)
- RSM-031 tariff attachment parsing
- `ResilientDataHubClient` with 401/503 retry + exponential backoff
- Dead-letter handling with retry capability
- Message idempotency via `processed_message_id`

### Onboarding Pipeline (Complete)
- 4 signup API endpoints: `POST /api/signup`, `GET /api/signup/{id}/status`, `POST /api/signup/{id}/cancel`, `GET /api/products`
- 6+ back-office API endpoints: signups list/detail/events, address lookup, customers list/detail
- `OnboardingService`, `SignupRepository`, `BusinessDayCalculator`, `GsrnValidator`
- `ProcessSchedulerService` — picks up pending requests, sends BRS-001/009 to DataHub
- `ProcessStateMachine` — full lifecycle: pending → sent_to_datahub → acknowledged → effectuation_pending → completed
- `EffectuationService` — transactional RSM-022 activation handler
- RSM-001 acceptance/rejection handling in MasterData queue poller
- Portfolio creation from signup data on RSM-022 arrival (contract + supply period)
- BRS-001 (supplier switch) and BRS-009 (move-in) request builders
- BRS-002 (end of supply) and BRS-010 (move-out) process initiation endpoints
- BRS-003 (erroneous switch) — `ErroneousSwitchService`

### Billing & Invoicing (Complete)
- `billing.invoice`, `billing.invoice_line`, `billing.payment`, `billing.payment_allocation` tables
- `InvoicingService` (background) — polls for uninvoiced completed settlement runs
- `InvoiceService` — create, send, cancel, credit note generation
- `PaymentMatchingService` — auto-matching payments to invoices
- `PaymentAllocator` — manual allocation support
- Bank file import endpoint
- `OverdueCheckService` — background overdue detection
- `AcontoEstimator` — aconto pre-payment calculation
- Outstanding/overdue/balance/ledger API endpoints

### Corrections (Complete)
- `CorrectionEngine` — delta calculation between original and corrected settlement
- `CorrectionService` — manual and auto-correction triggers
- `CorrectionRepository` — batch storage with line-level detail
- Auto-correction on corrected timeseries data (Worker DI wiring fixed 2026-02-17)
- Back-office correction UI (list, detail, trigger)

### Simulator (Complete for current needs)
- Standalone HTTP simulator mimicking DataHub B2B API
- Scenario engine with fixture loading
- Admin API: enqueue, reset, inspect requests
- Supports: RSM-012, RSM-022, RSM-004, RSM-034, RSM-031, BRS-001/009 acceptance
- Correction data endpoint for testing auto-corrections
- System + transmission tariff seeding (added 2026-02-17)

### Back Office UI (Extensive)
- 25 React pages: Dashboard, SignupNew/List/Detail, CustomerList/Detail, Settlement, SettlementRunDetail, BillingPeriods, Corrections, CorrectionDetail, InvoiceList/Detail, PaymentList/Detail, OutstandingOverview, Messages, DeadLetters, Processes, ProcessDetail, Products, SpotPrices, Simulator
- Vite + React + Tailwind CSS
- Calls API over HTTP (port 5173 → port 5001)

### Testing
- 474 tests (314 unit + 160 integration), all passing
- Golden master tests GM#1–GM#10
- Message handler tests (36 tests for all 4 handlers)
- State machine, settlement engine, parsers, billing, onboarding tests

### Infrastructure
- Clean Architecture: Domain → Application → Infrastructure → Hosts
- 7 DB schemas: portfolio, metering, tariff, settlement, billing, lifecycle, datahub
- 14 DbUp migrations (V001–V014)
- TimescaleDB hypertable for metering data
- Docker Compose: TimescaleDB + Aspire Dashboard
- OpenTelemetry (logs, traces, metrics) → Aspire Dashboard
- GitHub Actions CI/CD → Azure Container Apps

---

## What's Missing

### P1 — High Priority

#### Aggregations Persistence & Reconciliation
- `AggregationsMessageHandler` is a **stub** — logs RSM-014 but doesn't persist
- No `datahub.aggregation_data` table
- `ReconciliationService` exists in Application layer but has no stored data to reconcile against
- **Impact:** Can't verify own settlement against DataHub's wholesale calculations
- **Effort:** Small — table + repository + wire existing handler

#### Health Checks & Monitoring
- `/health` endpoint returns static OK — no actual checks
- No DB connectivity check, no DataHub reachability check
- `SettlementMetrics` class exists but limited counters
- No alerting thresholds defined
- **Effort:** Small

#### Simulator Error Injection
- No `/admin/inject-error` endpoint for 401/503/malformed scenarios
- `ResilientDataHubClient` has unit tests but no end-to-end resilience proof
- **Effort:** Small-medium

### P2 — Medium Priority

#### BRS-011 (Erroneous Move)
- Zero code — no request builder, no process type, no date correction logic
- Needed for correcting move-in/move-out dates after the fact
- **Effort:** Medium

#### RSM-015/016 (Historical Data Requests)
- No ability to request historical validated data or aggregated data from DataHub
- Needed for reconciliation dispute resolution
- **Effort:** Medium

#### Concurrent Edge-Case Tests
- No integration tests for: correction during active switch, mid-quarter move-out with aconto, tariff change + correction overlap
- Calculation logic likely works (golden masters cover the math) but pipeline isn't tested for concurrent scenarios
- **Effort:** Small — tests only, no new production code expected

### P3 — Lower Priority / Future

#### Customer Portal Data Layer
- No `/api/portal/` endpoints for customer-facing consumption/invoice queries
- TimescaleDB `time_bucket()` aggregation not yet exposed via API
- **Effort:** Medium

#### Performance Baseline
- No load testing at 80K metering point scale
- No baseline measurements for ingestion, settlement, or query performance
- TimescaleDB compression/retention policies not validated at scale
- **Effort:** Large

#### Real DataHub Validation (Actor Test)
- System has not been validated against Energinet's real test environment
- All testing is against the local simulator
- **Impact:** Real CIM JSON may differ from fixtures in unexpected ways

---

## Database Migration Status

Current: V001–V014. The previous plan referenced V021–V023 — those numbers were speculative and never created. Needed migrations:

| Migration | Purpose | Priority |
|-----------|---------|----------|
| V015 | `datahub.aggregation_data` + `datahub.reconciliation_result` tables | P1 |

The `billing.invoice`, `billing.invoice_line`, `billing.payment` tables already exist (created in earlier migrations). The `portfolio.signup` table already exists. No V021–V023 needed.

---

## DataHub Protocol Coverage

| Protocol | Status | Notes |
|----------|--------|-------|
| RSM-012 (metering data) | ✅ Complete | Parse, store, history, correction detection, auto-correction |
| RSM-014 (aggregated data) | 🟡 Parse only | Parsed but not persisted or reconciled |
| RSM-022 (master data activation) | ✅ Complete | Creates metering point, triggers effectuation, creates portfolio |
| RSM-004 (master data notifications) | ✅ Complete | Grid area, connection status changes |
| RSM-034 (tariff prices) | ✅ Complete | Grid, system, transmission tariffs stored |
| RSM-031 (tariff attachments) | ✅ Complete | Metering point ↔ tariff linking |
| RSM-001 (acknowledgement) | ✅ Complete | Acceptance/rejection handling in MasterData poller |
| BRS-001 (supplier switch) | ✅ Complete | Request builder + full state machine flow |
| BRS-002 (end of supply) | ✅ Complete | Process initiation endpoint |
| BRS-003 (erroneous switch) | ✅ Complete | `ErroneousSwitchService` |
| BRS-009 (move-in) | ✅ Complete | Via onboarding pipeline |
| BRS-010 (move-out) | ✅ Complete | Process initiation endpoint |
| BRS-011 (erroneous move) | 🔴 Missing | Zero code |
| RSM-015 (request historical data) | 🔴 Missing | Zero code |
| RSM-016 (request aggregated data) | 🔴 Missing | Zero code |

---

## Suggested Execution Order

1. **Aggregations persistence** (V015 migration + repository + wire handler) — biggest functional gap, small effort
2. **Health checks** — `/health/live` and `/health/ready` with DB + DataHub checks
3. **Concurrent edge-case integration tests** — no new code, high confidence ROI
4. **Simulator error injection** — prove resilience end-to-end
5. **BRS-011 erroneous move** — new DataHub protocol support
6. **RSM-015/016 historical data requests** — enables reconciliation dispute resolution
7. **Customer portal data layer** — when needed for customer-facing features
8. **Performance baseline** — before scaling to production load
