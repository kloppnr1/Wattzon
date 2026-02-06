# Implementation Plan

An agile, MVP-driven plan for building the DataHub settlement system. Each MVP delivers a working, demonstrable result — not a layer of infrastructure. Testing against DataHub is the biggest technical risk, so the DataHub simulator grows incrementally alongside the MVPs.

---

## The Core Problem: Testing Against DataHub

DataHub is a **black-box external system** owned by Energinet. We cannot:
- Create arbitrary test data in DataHub
- Control when or what messages appear on the queues
- Reset state between test runs
- Run DataHub locally

Energinet provides test environments (Actor Test, Preprod), but:
- Access requires formal registration and approval
- Credentials must be created in the actor portal (aktørportalen) with MitID
- Test data is limited and shared with other actors
- Queue messages arrive on Energinet's schedule, not ours
- There is no way to "replay" a message once dequeued

**This means:** Most development and testing must happen against a **local DataHub simulator** that we build and control. Real DataHub environments are for validation, not daily development.

---

## Approach: MVPs, Not Phases

Instead of building the system in horizontal layers (first all integration, then all portfolio, then all settlement), we build **vertical slices** that each deliver a working end-to-end result.

```
Traditional (waterfall)              Our approach (MVP)
──────────────────────               ──────────────────
Phase 1: Integration                 MVP 1: One correct invoice
Phase 2: Portfolio                     (happy path: OAuth → RSM-012 → settlement)
Phase 3: Settlement                  MVP 2: Full customer lifecycle
Phase 4: Lifecycle                     (happy path: onboarding → offboarding)
Phase 5: Reconciliation              MVP 3: DataHub integration + edge cases
Phase 6: Validation                    (Actor Test, corrections, reconciliation, elvarme, solar)
                                     MVP 4: Production
                                       (real customers, ERP, portal, scale)
```

**Why this matters:**
- Each MVP is **demo-able** — you can show a stakeholder a calculated invoice after MVP 1, not after 20 weeks
- **Feedback loops are short** — if the settlement calculation is wrong, you discover it in MVP 1
- **Risk is front-loaded** — the biggest unknowns (DataHub communication + correct settlement) are resolved first
- The simulator and test suite **grow with each MVP** — build what you need, when you need it

---

## Testing Strategy: The DataHub Simulator

### What the simulator must replicate

The simulator is a lightweight HTTP server that mimics the DataHub B2B API — just enough for our system to believe it is talking to the real DataHub. It starts small (MVP 1: one queue, one message type) and grows with each MVP.

```
┌─────────────────────────────────────────────────────────────┐
│  DataHub Simulator                                           │
│                                                              │
│  OAuth2 token endpoint                                       │
│    POST /oauth2/v2.0/token → returns a fake JWT              │
│                                                              │
│  Queue endpoints (4 queues)                                  │
│    GET  /v1.0/cim/Timeseries   → next RSM-012 or RSM-014    │
│    GET  /v1.0/cim/MasterData   → next RSM-007 or RSM-004    │
│    GET  /v1.0/cim/Charges      → next tariff update          │
│    GET  /v1.0/cim/Aggregations → next RSM-014 aggregation    │
│    DELETE /v1.0/cim/dequeue/{id} → acknowledge               │
│                                                              │
│  Outbound request endpoints                                  │
│    POST /v1.0/cim/requestchangeofsupplier → accept BRS-001   │
│    POST /v1.0/cim/requestendofsupply → accept BRS-002        │
│    ... (other BRS requests)                                  │
│                                                              │
│  Scenario engine                                             │
│    Load test fixtures (CIM JSON files)                       │
│    Queue them in order                                       │
│    Simulate timing (delays, empty queues, errors)            │
│    Validate outbound requests                                │
│                                                              │
│  Admin API                                                   │
│    POST /admin/enqueue → add a message to any queue          │
│    POST /admin/scenario → load a named scenario              │
│    GET  /admin/requests → inspect outbound requests received │
│    POST /admin/reset → clear all queues and state            │
└─────────────────────────────────────────────────────────────┘
```

### Simulator grows with each MVP

| MVP | Simulator capabilities |
|-----|----------------------|
| **MVP 1** | OAuth2 token endpoint. Timeseries queue (RSM-012 only). Charges queue. Dequeue. In-process fake (`FakeDataHubClient`) for unit tests |
| **MVP 2** | + MasterData queue (RSM-007, RSM-004). + BRS-001/002/003/009/010/044 request endpoints. + Scenario engine ("full onboarding", "offboarding", "rejection"). Standalone HTTP simulator (Docker) |
| **MVP 3** | + **Real DataHub (Actor Test) in parallel.** + Correction scenarios (original → correction on same queue). + BRS-042/011 endpoints. + Aggregations queue (RSM-014). + RSM-015/016 response endpoints. + Error injection (401, 503, malformed messages). + Elvarme/solar fixtures |
| **MVP 4** | + Performance scenarios (80K metering points). + Realistic timing. + Preprod validation |

### Test fixture library

The simulator is powered by **CIM JSON fixture files** — real-format messages that exercise specific scenarios. Fixtures are added as each MVP needs them:

| Fixture set | Introduced in | Tests |
|-------------|---------------|-------|
| `rsm012-single-day.json` | MVP 1 | Basic ingestion pipeline |
| `rsm012-multi-day.json` | MVP 1 | Full monthly settlement |
| `charges-tariff-update.json` | MVP 1 | Rate table update |
| `rsm007-activation.json` | MVP 2 | Metering point activation |
| `rsm004-grid-area-change.json` | MVP 2 | Tariff reassignment |
| `brs001-receipt-accepted.json` | MVP 2 | Onboarding flow |
| `brs001-receipt-rejected.json` | MVP 2 | Error handling |
| `rsm012-correction.json` | MVP 3 | Correction detection + delta calculation |
| `rsm012-production-e18.json` | MVP 3 | E18 handling, net settlement |
| `rsm012-missing-hours.json` | MVP 3 | Incomplete data handling |
| `rsm007-electrical-heating.json` | MVP 3 | Elvarme threshold tracking |
| `rsm014-aggregation.json` | MVP 3 | Reconciliation |

**Where do fixtures come from?**

1. **Energinet documentation** — the CIM EDI Guide (Dok. 15/00718-191) and RSM Guide contain example messages
2. **Energinet's open source repos** — [opengeh-edi](https://github.com/Energinet-DataHub/opengeh-edi) contains test data
3. **Actor Test captures** — once we have access, capture real messages and anonymize them
4. **Hand-crafted** — for edge cases not covered by the above

### Simulator implementation

| Approach | Effort | Fidelity | Recommendation |
|----------|--------|----------|----------------|
| **In-process fake (MVP 1)** | Low | Medium | Implement `IDataHubClient` interface with an in-memory fake. No HTTP, no simulator process. Tests run fast. |
| **Standalone HTTP server (MVP 2+)** | Medium | High | ASP.NET Minimal API that serves fixture files from disk. Docker container. Closest to real DataHub behavior. |
| **WireMock / Mountebank** | Low | Medium | Record/replay HTTP stubs. Good for contract tests. Less flexible for scenario-based testing. |
| **Energinet's own test tools** | Unknown | Unknown | Check if Energinet provides a simulator or sandbox. (WARNING: VERIFY — as of 2025, no public simulator is known) |

---

## Test Pyramid

```
                    ╱╲
                   ╱  ╲
                  ╱ E2E╲          Actor Test / Preprod
                 ╱ (few)╲         Real DataHub, real messages
                ╱────────╲
               ╱          ╲
              ╱ Integration╲      Simulator (Docker)
             ╱  (moderate)  ╲     Full HTTP, CIM JSON, queue behavior
            ╱────────────────╲
           ╱                  ╲
          ╱   Unit / Domain    ╲  In-process, no I/O
         ╱    (many, fast)      ╲ Settlement calc, CIM parsing, correction detection
        ╱────────────────────────╲
```

### Unit / Domain tests (hundreds, milliseconds)

No HTTP, no database, no simulator. Pure logic.

| What to test | Example |
|-------------|---------|
| **CIM JSON parsing** | Parse an RSM-012 fixture → assert GSRN, period, quantities |
| **Correction detection** | Given stored data + new RSM-012, detect delta per interval |
| **Settlement calculation** | Given kWh[] + spot prices[] + tariff rates[] → assert amounts per line |
| **Tariff lookup** | Given a timestamp + grid area → correct rate (day/night/peak) |
| **Aconto settlement** | Given actual total + aconto payments → correct difference |
| **Elvarme threshold** | Given cumulative kWh crossing 4,000 → split rate |
| **Solar net settlement** | Given E17 consumption + E18 production per hour → net amounts |
| **Pro rata subscription** | Given partial period → correct daily proration |
| **Invoice line aggregation** | Given hourly settlement results → correct invoice totals |

**These are the most important tests.** If the settlement calculation is wrong, no amount of integration testing will save us.

### Integration tests (tens, seconds)

Test the full pipeline with the HTTP simulator or a real database.

| What to test | How |
|-------------|-----|
| **Queue polling loop** | Simulator has messages → our poller picks them up → persisted in DB |
| **OAuth2 token flow** | Simulator token endpoint → our auth manager caches and renews |
| **Peek → parse → store → dequeue** | Full RSM-012 lifecycle against simulator |
| **Correction in pipeline** | Enqueue original RSM-012, then correction → verify delta stored |
| **BRS-001 request/response** | Send supplier switch → simulator validates format → returns acceptance |
| **Idempotent processing** | Enqueue same MessageId twice → only processed once |
| **Dead-letter on invalid message** | Enqueue malformed CIM JSON → verify dead-letter entry |
| **Token expiry mid-poll** | Simulator returns 401 → our system renews → retry succeeds |
| **Empty queue behavior** | Simulator returns 204 → our poller backs off correctly |
| **Settlement run end-to-end** | Seed DB with metering data + rates → run settlement → verify invoice lines |

### E2E tests against real DataHub (few, minutes)

Run against Energinet's Actor Test or Preprod with real credentials. These tests are **slow, flaky, and expensive** — keep them minimal.

| What to test | Why |
|-------------|-----|
| **OAuth2 authentication** | Verify our token request works against real Azure AD |
| **Peek from an empty queue** | Verify 204 response format matches our parser |
| **Peek a real RSM-012** | Verify CIM JSON structure matches our parser (the most important E2E test) |
| **Dequeue a message** | Verify DELETE works and the message does not reappear |
| **Submit BRS-001** | Verify our request format is accepted by DataHub |
| **Receive RSM-009 response** | Verify we can parse the acceptance/rejection |

**These tests answer one question:** Does the real DataHub produce messages that our parser actually understands?

---

## MVP 1: One Correct Invoice

**Goal:** Prove the entire chain works end-to-end — from DataHub connection to a verifiable settlement result for one metering point. Happy path only: initial data in, correct invoice out.

**Delivered outcome:** A calculated invoice you can put next to a hand-calculated reference and confirm they match.

### What to build

| Area | Task | Test approach |
|------|------|---------------|
| **Foundation** | .NET solution structure, CI/CD pipeline, Docker Compose (PostgreSQL + TimescaleDB) | Verify container starts, CI runs |
| **Simulator** | In-process `FakeDataHubClient` + first CIM JSON fixtures (`rsm012-single-day`, `rsm012-multi-day`, `charges-tariff-update`) | Unit tests against fake |
| **Auth** | OAuth2 Auth Manager — token fetch, cache, proactive renewal, 401 retry | Unit: mock token endpoint |
| **Ingestion** | Queue Poller (Timeseries), CIM JSON Parser (RSM-012), time series storage (`metering_data` hypertable) | Unit: parse fixtures. Integration: parse → store → query roundtrip |
| **Idempotency** | Track MessageId, skip duplicates, dead-letter on parse failure | Integration: enqueue twice → stored once. Malformed → dead-letter |
| **Spot prices** | Fetch and store Nord Pool prices (DK1/DK2) | Integration: mock market data → stored prices |
| **Charges** | Parse tariff updates from Charges queue | Unit: fixture files |
| **Settlement** | `kWh × (spot + margin)` per hour, grid tariff (time-differentiated), system/transmission tariff, elafgift, subscriptions (pro rata), VAT (25%) | Unit: **golden master tests** |
| **Actor Test access** | Apply for access to Energinet's test environment (can run in parallel with development) | — |

### Golden master tests (introduced here, expanded in later MVPs)

Hand-calculated reference invoices that the settlement engine must reproduce exactly:

```
Golden Master #1: Simple spot customer, one month
  Input: 720 hours consumption + spot prices + grid tariffs + margin 0.04 DKK/kWh
  Expected: hand-calculated energy + tariff + tax + subscription + VAT lines

Golden Master #2: Partial period (mid-month start)
  Input: 15 days of data, pro rata subscriptions
  Expected: correctly prorated amounts
```

### Exit criteria

- `docker compose up` starts the database and services
- Simulator RSM-012 → parsed → stored → settlement run → invoice lines match golden master
- Dead-letter handles malformed messages
- CI/CD runs unit tests on every push
- Actor Test: successfully authenticate and peek at least one message (if access granted)

---

## MVP 2: Full Customer Lifecycle

**Goal:** Handle a customer from onboarding to offboarding — all BRS processes, all state transitions. Happy path lifecycle: a customer can be signed up, activated, operated, and terminated through the system.

**Delivered outcome:** Run the "full lifecycle" simulator scenario — a customer goes through every phase and the system handles each step correctly.

### What to build

| Area | Task | Test approach |
|------|------|---------------|
| **Simulator** | Upgrade to standalone HTTP server (Docker). MasterData queue. BRS request endpoints (001/002/003/009/010/043/044). Scenario engine | Integration: full lifecycle scenario |
| **Master data** | CIM JSON Parser (RSM-007, RSM-004). MasterData queue poller | Unit: fixtures. Integration: simulator |
| **Portfolio** | Metering point CRUD, supply period tracking, grid area assignment, tariff assignment | Unit: domain logic. Integration: DB roundtrip |
| **Customer** | Customer record (CPR/CVR, contact, product association). Eloverblik GSRN lookup at onboarding | Unit + integration |
| **Onboarding** | BRS-001 request builder + RSM-009 response handler. BRS-043 (short notice). BRS-009 (move-in). BRS-015 (customer master data) | Unit: CIM structure. Integration: simulator validates format, returns response |
| **State machine** | ProcessRequest lifecycle: Pending → SentToDataHub → Acknowledged → EffectuationPending → Completed (and Rejected, Cancelled) | Unit: state transition rules |
| **Cancellation** | BRS-003 (cancel switch before effective date) | Unit: state machine. Integration: simulator |
| **Offboarding** | BRS-002 (end of supply). BRS-010 (move-out). BRS-044 (cancel termination). Incoming BRS-001 (we lose customer) | Unit + integration: simulator |
| **Final settlement** | Partial period settlement. Aconto settlement at offboarding (actual vs. paid). Final invoice generation | Unit: golden master tests |
| **Aconto** | Aconto estimation (new customer from Eloverblik data, existing from 12-month history). Quarterly settlement cycle. Combined quarterly invoice | Unit: golden master tests |

### New golden master tests

```
Golden Master #3: Aconto customer, quarterly settlement
  Input: 3 months data, aconto payments, combined invoice
  Expected: settlement part + aconto difference + new aconto estimate

Golden Master #4: Final settlement at offboarding (partial quarter)
  Input: 6 weeks of data (mid-quarter departure), aconto paid
  Expected: prorated settlement, aconto difference, final invoice
```

### Simulator scenarios

**Scenario: Happy path onboarding**
```
1. System sends BRS-001 (supplier switch)
   → Simulator returns RSM-009 (accepted)
2. After "effective date" (immediate in test):
   → Simulator enqueues RSM-007 (master data) on MasterData queue
   → Simulator enqueues RSM-012 (first day of metering data) on Timeseries queue
3. System peeks MasterData → parses RSM-007 → creates metering point
4. System peeks Timeseries → parses RSM-012 → stores metering data
5. Repeat step 4 for 30 days (30 RSM-012 messages)
6. System runs settlement → produces invoice lines
```

**Scenario: Rejection and retry**
```
1. System sends BRS-001 with incorrect CPR
   → Simulator returns RSM-009 (rejected, reason: CPR mismatch)
2. System updates customer record
3. System sends BRS-001 again with correct CPR
   → Simulator returns RSM-009 (accepted)
```

**Scenario: Full lifecycle (onboarding → operation → offboarding)**
```
1. BRS-001 → accepted → RSM-007 → RSM-012 (30 days)
2. Settlement run → invoice
3. Incoming BRS-001 from another DDQ (customer leaving)
4. Final RSM-012 (up to switch date)
5. Mark metering point inactive
6. Final settlement (partial period) + aconto settlement
7. Final invoice generated
```

**Scenario: Cancellation before activation**
```
1. BRS-001 → accepted
2. Customer withdraws → BRS-003 sent
3. Simulator confirms cancellation
4. State machine: Acknowledged → Cancelled
5. Billing plans cleaned up
```

### Exit criteria

- Full lifecycle scenario works end-to-end against simulator (onboarding → operation → offboarding → final invoice)
- All happy path BRS processes can be submitted and responses handled
- State machine handles all transitions correctly (including rejection and cancellation)
- Aconto quarterly cycle works (estimation → payment → settlement → combined invoice)
- Final settlement and aconto settlement produce correct results (golden masters pass)
---

## MVP 3: DataHub Integration + Edge Cases

**Goal:** Connect to the real DataHub (Actor Test), harden the system against real messages, and handle everything that can go wrong. Real DataHub validation comes first — it will reveal edge cases we didn't anticipate. Then build correction handling, erroneous processes, reconciliation, and special metering scenarios.

**Delivered outcome:** The system passes a full lifecycle test against Energinet's Actor Test environment. All edge case scenarios pass against the simulator. Every real CIM JSON message that broke the parser is now a fixture in the test suite.

### What to build

**Start here: DataHub integration**

| Area | Task | Test approach |
|------|------|---------------|
| **Actor Test validation** | Full flow against real DataHub: BRS-001 → RSM-007 → RSM-012 → settlement → BRS-002 | E2E: real messages, real responses |
| **Parser hardening** | Fix any parsing failures discovered with real messages. Add every real CIM JSON to fixture library | Capture → fixture → regression test |
| **Error handling** | Token expiry mid-poll (401 → renew → retry). DataHub unavailable (503 → backoff). Malformed messages (dead-letter → dequeue). Missing spot prices (halt gracefully) | Integration: simulator error injection |

**Then: edge cases (informed by real DataHub behavior)**

| Area | Task | Test approach |
|------|------|---------------|
| **Corrections** | Compare incoming RSM-012 against stored data. Calculate delta per interval. Generate credit/debit notes | Unit: stored + new → delta per interval. Golden master tests |
| **Erroneous processes** | BRS-042 (erroneous switch — reverse supply period, credit all invoices). BRS-011 (erroneous move — adjust dates, recalculate) | Integration: full reversal flow against simulator |
| **Wholesale reconciliation** | RSM-014 parser. Compare own settlement vs. DataHub aggregation per grid area. Identify deviating metering points | Unit: matching + discrepancy scenarios |
| **Historical data requests** | RSM-015 (request validated data for verification). RSM-016 (request detailed aggregated data) | Integration: simulator |
| **Elvarme** | Electrical heating flag from RSM-007. Cumulative annual kWh tracking. Split rate at 4,000 kWh threshold. Year boundary reset | Unit: golden master. Edge: threshold crossed mid-period |
| **Solar / E18** | E18 production metering point ingestion. Link E17↔E18. Hourly net settlement. Production credit at spot price (no tariffs/tax on excess) | Unit: golden master. Edge: negative settlement lines |
| **Concurrent processes** | Correction during supplier switch (filter to our supply period). Move-out mid-quarter (partial aconto settlement). Tariff change mid-billing period (split rate application) | Unit: specific scenarios |
| **Customer disputes** | Request historical data (RSM-015) for verification. Compare against own settlement. Support workflow for disputed invoices | Integration |

### New golden master tests

```
Golden Master #5: Correction (delta settlement)
  Input: original data + corrected data for same period
  Expected: credit/debit note with correct delta amounts

Golden Master #6: Erroneous switch reversal
  Input: 2 months of data for an erroneous period
  Expected: all invoices credited, metering point reversed

Golden Master #7: Elvarme customer crossing 4,000 kWh threshold
  Input: cumulative consumption crossing threshold mid-period
  Expected: split rate (standard below, reduced above)

Golden Master #8: Solar customer with E18 production
  Input: E17 consumption + E18 production per hour
  Expected: net settlement per hour, production credit at spot price

Golden Master #9: Correction during active supply period overlap
  Input: correction spanning period before and after our supply start
  Expected: only delta within our supply period is settled

Golden Master #10: Tariff change mid-billing period
  Input: old rate until 15th, new rate from 16th
  Expected: split calculation with correct rates per hour
```

### Simulator scenarios

**Scenario: Correction**
```
1. Simulator enqueues RSM-012 (original: 1.5 kWh for hour 14:00)
2. System stores data
3. System runs settlement → original invoice
4. Simulator enqueues RSM-012 (correction: 2.0 kWh for hour 14:00)
5. System detects delta (+0.5 kWh), calculates financial impact
6. System generates credit/debit note
```

**Scenario: Erroneous switch reversal**
```
1. System has active metering point with 2 months of data and invoices
2. System sends BRS-042 (erroneous switch reversal)
3. Simulator confirms reversal
4. System: reverse supply period, credit all issued invoices
5. System: delete/mark metering data as reversed
```

**Scenario: Wholesale reconciliation discrepancy**
```
1. System has completed settlement for grid area 344, January
2. Simulator enqueues RSM-014 with aggregated data that differs by 50 kWh
3. System detects discrepancy
4. System sends RSM-016 request for detailed data
5. Simulator returns detailed RSM-014 response
6. System identifies the deviating metering point
7. System sends RSM-015 request for historical data
8. Simulator returns corrected RSM-012
9. System recalculates and the discrepancy resolves
```

**Scenario: Token expiry during polling**
```
1. Simulator issues token with 5-second TTL
2. System starts polling → first peek succeeds
3. Token expires → Simulator returns 401
4. System fetches new token → retries → succeeds
```

**Scenario: DataHub unavailable**
```
1. Simulator configured to return 503 for all requests
2. System retries with exponential backoff
3. After N retries, simulator starts returning 200
4. System resumes normal operation — no messages lost
```

**Scenario: Malformed message**
```
1. Simulator enqueues invalid CIM JSON (missing required fields)
2. System attempts to parse → fails
3. Message goes to dead-letter table
4. System dequeues to free the queue
5. Next message (valid) is processed normally
```

### Exit criteria

- Full flow works against Energinet Actor Test with real messages (BRS-001 → RSM-007 → RSM-012 → settlement → BRS-002)
- Every real CIM JSON that broke the parser is now a fixture in the test suite
- System recovers gracefully from all error scenarios (401, 503, malformed messages, missing data)
- Correction detection works: original + correction → correct delta calculated and credit/debit note generated
- Erroneous switch reversal credits all invoices and reverses supply period
- Wholesale reconciliation detects discrepancies and resolves via RSM-015/016
- Elvarme threshold tracking produces correct split rates (golden master passes)
- Solar net settlement produces correct hourly netting with production credits (golden master passes)
- Concurrent process scenarios handled correctly (correction during switch, mid-quarter move-out, mid-period tariff change)
- All golden master tests (#5-#10) pass

---

## MVP 4: Production

**Goal:** Go live with real customers. ERP integration, payment services, customer portal, and progressive migration from pilot to full portfolio. The system is already validated against real DataHub (MVP 3) — this MVP is about production readiness.

**Delivered outcome:** All customers billed through the system. Real invoices sent. Customer portal live. System validated at scale.

### What to build

| Area | Task | Test approach |
|------|------|---------------|
| **ERP integration** | Settlement results → ERP export (invoice generation, receivables). Domain events: `settlement.completed`, `customer.activated` | Integration: API returns correct data |
| **Payment services** | Betalingsservice (PBS) integration for recurring payments | Integration: payment file generation |
| **Digital post** | e-Boks integration for invoice delivery | Integration: document format + delivery |
| **Customer portal** | Consumption graphs (hourly/daily/monthly), invoice history, contract details, contact info | Integration + E2E |
| **Monitoring** | Health checks, alerting, audit logging (CorrelationId, GDPR compliance) | — |
| **Pilot** | Onboard 10-50 real customers, verify first settlement against manual calculation | Manual verification |
| **Full migration** | Migrate all customers from existing system | Migration scripts + verification |
| **Preprod validation** | Final validation against production-like environment | E2E |
| **Performance** | Load test: 80K metering points, daily ingestion, monthly settlement | Simulator at scale |
| **Security audit** | GDPR compliance review, secret management, ISAE 3402 requirements | — |

### Exit criteria

- ERP receives settlement results and generates invoices
- Pilot customers (10-50) billed and verified against manual calculation
- All customers migrated and billed through the system
- Customer portal live
- Performance validated at full portfolio scale
- Monitoring, alerting, and audit logging in place
- Security audit passed

---

## Risk Register

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| **Real CIM JSON differs from our fixtures** | Parser breaks in Actor Test | High | Capture real messages early. Build parser to tolerate unknown fields. Add every failing message to fixture library |
| **Actor Test access delayed** | Cannot validate against real DataHub | Medium | Simulator covers most cases. Apply for access in MVP 1 |
| **Correction detection misses edge cases** | Incorrect invoices | Medium | Comprehensive unit tests. Golden master tests. Compare against manual calculation |
| **Settlement rounding differs from DataHub** | Reconciliation discrepancies | High | Test with real RSM-014 data. Match DataHub's rounding rules (WARNING: VERIFY) |
| **Queue behavior differs from documentation** | Poller fails in production | Medium | Actor Test validation. Test peek/dequeue sequencing |
| **Rate changes mid-period** | Incorrect tariff application | Medium | Time-differentiated rate lookup with `valid_from`/`valid_to`. Unit tests with rate changes |
| **PT15M transition** | 4x data volume, different resolution handling | Low (future) | Parser already supports PT15M. Load test with simulator |
| **OAuth2 token edge cases** | Authentication failures | Low | Simulator tests for expiry, renewal, concurrent requests |
| **CIM schema version change** | Parser breaks after DataHub update | Medium | Monitor Energinet release notes. Fixture versioning. Parser tolerance for unknown fields |
| **Spot price provider unavailable** | Settlement cannot run | Low | Alert + retry. Settlement engine halts gracefully for missing prices |

---

## Key Design Decisions for Testability

### 1. Interface-driven DataHub client

All DataHub communication goes through an interface:

```
IDataHubClient
  ├── PeekTimeseries() → CimMessage?
  ├── PeekMasterData() → CimMessage?
  ├── PeekCharges() → CimMessage?
  ├── PeekAggregations() → CimMessage?
  ├── Dequeue(messageId) → void
  ├── SubmitRequest(brsRequest) → CimResponse
  └── GetToken() → string
```

Three implementations:
- `FakeDataHubClient` — in-memory, for unit/domain tests (MVP 1)
- `SimulatorDataHubClient` — points to Docker simulator, for integration tests (MVP 2+)
- `RealDataHubClient` — points to DataHub (Actor Test / Preprod / Prod)

Switching between them is a configuration change, not a code change.

### 2. Deterministic settlement engine

The settlement engine takes **all inputs as parameters** — no implicit state:

```
SettlementResult Calculate(
    MeteringData[] consumption,
    SpotPrice[] prices,
    TariffRate[] tariffs,
    ProductPlan plan,
    Period billingPeriod
)
```

This makes it trivially testable: provide known inputs → assert known outputs.

### 3. Fixture-driven test data

No random test data. All tests use deterministic fixtures with pre-calculated expected results. Fixtures are version-controlled alongside the code.

### 4. Contract tests for CIM messages

Every CIM message type has a contract test that:
1. Loads a fixture file
2. Parses it with our parser
3. Re-serializes it
4. Asserts the output matches the input (round-trip)

If Energinet changes the CIM format, these tests break immediately.

---

## CI/CD Pipeline

```
Push to main
    │
    ├── Build (.NET restore + build)
    │
    ├── Unit tests (settlement calc, CIM parsing, domain logic)
    │   └── Fail → block merge
    │
    ├── Integration tests (Docker Compose: simulator + database)
    │   └── Fail → block merge
    │
    ├── Container build (Docker images)
    │
    ├── Deploy to staging
    │
    └── (Manual gate) E2E smoke test against Actor Test
        └── Fail → investigate, add fixture, fix parser
```

---

## MVP Summary

| MVP | Focus | Key deliverable | Depends on |
|-----|-------|----------------|------------|
| **1** | One correct invoice | Happy path: DataHub connection → RSM-012 ingestion → settlement → verified result | — |
| **2** | Full customer lifecycle | Happy path: all BRS processes. Onboarding → operation → offboarding → final settlement | MVP 1 |
| **3** | DataHub integration + edge cases | Real DataHub validation (Actor Test). Then: corrections, erroneous processes, reconciliation, elvarme, solar, error handling | MVP 2 + Actor Test access |
| **4** | Production | ERP + payment + portal. Pilot (10-50 customers) → full migration. Scale | MVP 3 |

**Critical path:** Actor Test access. Apply during MVP 1. If access is delayed, MVP 2 proceeds against the simulator, and DataHub integration shifts to MVP 3 (as soon as access is granted).

---

## Sources

- [Settlement overview](datahub3-settlement-overview.md) — what settlement is and how it works
- [System architecture](datahub3-proposed-architecture.md) — technology choices, cost estimates
- [Customer lifecycle](datahub3-customer-lifecycle.md) — phases from onboarding to closing
- [RSM-012 reference](rsm-012-datahub3-measure-data.md) — CIM message format, API endpoints, correction flow
- [Authentication and security](datahub3-authentication-security.md) — OAuth2, test environments, credentials
- [Edge cases and error handling](datahub3-edge-cases.md) — corrections, reconciliation, concurrent processes
- [Business processes](datahub3-ddq-business-processes.md) — BRS/RSM reference for all processes
- [Product structure and billing](datahub3-product-and-billing.md) — invoice lines, aconto, payment models
- [CIS platform and external systems](datahub3-cis-and-external-systems.md) — ERP, portal, payment integrations
- [Database model](datahub3-database-model.md) — PostgreSQL/TimescaleDB schema
