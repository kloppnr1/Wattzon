# MVP 1 Test Plan: One Correct Invoice

Test plan for MVP 1, with a focus on the DataHub simulator — what to build, in what order, and how to verify each component.

---

## Goal Recap

MVP 1 proves the entire chain works end-to-end: DataHub connection → RSM-012 ingestion → settlement → a verifiable invoice for one metering point. Happy path only.

**The test plan answers:** How do we know each piece works, and how do we build the simulator incrementally to support that?

---

## Simulator Strategy for MVP 1

MVP 1 uses an **in-process fake** (`FakeDataHubClient`) — not a standalone HTTP server. This keeps things fast and simple: no Docker, no network, no process management. The fake implements the same `IDataHubClient` interface that the real client will use later.

The standalone HTTP simulator comes in MVP 2. Building it too early adds infrastructure work before we've validated the core logic.

### What the fake must support in MVP 1

| Capability | Why |
|------------|-----|
| Return a fake JWT token | Auth Manager can call `GetToken()` without hitting Azure AD |
| Serve RSM-012 messages from fixture files (Timeseries queue) | Ingestion pipeline has data to parse and store |
| Serve Charges messages from fixture files | Tariff rates can be loaded |
| Dequeue by MessageId | Poller can acknowledge messages after processing |
| Return 204 (empty queue) after all fixtures are consumed | Poller handles the "no more messages" case |
| Track dequeued MessageIds | Tests can assert that messages were acknowledged |
| Reset state between test runs | Each test starts clean |

### What the fake does NOT need in MVP 1

- MasterData queue (no RSM-007/RSM-004 — we seed portfolio data directly in tests)
- Aggregations queue (no RSM-014 — reconciliation is MVP 3)
- Outbound BRS request endpoints (no supplier switch — that's MVP 2)
- Scenario engine (single-scenario tests are enough)
- Error injection (401, 503, malformed — that's MVP 3)
- HTTP transport (in-process only)

---

## Implementation Order and Test Approach

The components below are listed in build order. Each step has a clear "how to test" before moving to the next.

### Step 1: Solution Structure + Database

**What to build:**
- .NET solution with project structure (Integration, Settlement, Portfolio, SharedKernel)
- Docker Compose with PostgreSQL + TimescaleDB
- `metering_data` hypertable, `dead_letter` table, `processed_messages` table
- CI pipeline that builds and runs tests on push

**How to test:**
- `docker compose up` starts without errors
- A simple integration test connects to the DB, inserts a row into `metering_data`, reads it back
- CI pipeline runs on push and reports green

**Fixtures needed:** None yet.

---

### Step 2: IDataHubClient Interface + FakeDataHubClient

**What to build:**
- `IDataHubClient` interface:
  ```
  PeekTimeseries() → CimMessage?
  PeekCharges() → CimMessage?
  Dequeue(messageId) → void
  GetToken() → string
  ```
- `FakeDataHubClient` implementation:
  - Constructor takes a list of fixture file paths (or pre-loaded CIM JSON strings)
  - `PeekTimeseries()` returns the next undequeued message, or null if all consumed
  - `Dequeue(id)` marks a message as consumed
  - `GetToken()` returns a hardcoded fake JWT
  - `Reset()` clears state for test isolation

**How to test (unit tests):**

| Test | Assertion |
|------|-----------|
| `Peek_WhenMessagesExist_ReturnsFirstMessage` | Returns the first fixture message with correct MessageId and body |
| `Peek_AfterDequeue_ReturnsNextMessage` | After dequeueing message 1, peek returns message 2 |
| `Peek_WhenAllDequeued_ReturnsNull` | Returns null (equivalent to 204 No Content) |
| `Dequeue_UnknownId_Throws` | Throws or returns error for unknown MessageId |
| `GetToken_ReturnsNonEmptyString` | Returns a string that can be used as a Bearer token |
| `Reset_ClearsAllState` | After reset, peek returns the first message again |

**Fixtures needed:** At least one minimal RSM-012 JSON file to load.

---

### Step 3: CIM JSON Fixtures

**What to build:**

Create the first fixture files based on the CIM EDI Guide (Dok. 15/00718-191) and Energinet's open-source repos. These are the test data that power everything downstream.

| Fixture file | Content | Used by |
|-------------|---------|---------|
| `rsm012-single-day.json` | One RSM-012 message: one metering point, one day (24 hourly readings), ProcessType E23, quality A01 | Ingestion pipeline, settlement |
| `rsm012-multi-day.json` | 30 RSM-012 messages: same metering point, 30 days (full month) | Full monthly settlement |
| `charges-grid-tariff.json` | Grid tariff rates for one grid area: day/night/peak rates with validity period | Tariff lookup |
| `charges-system-tariff.json` | Energinet system tariff + transmission tariff | Tariff lookup |

**How to validate fixtures:**

| Test | Assertion |
|------|-----------|
| `Fixture_IsValidJson` | Each fixture file parses as valid JSON |
| `Fixture_HasRequiredFields` | MarketDocument/mRID, Series/MarketEvaluationPoint/mRID, Series/Period/Point[] all present |
| `Fixture_QuantitiesAreRealistic` | Hourly quantities between 0 and 10 kWh (residential range) |
| `Fixture_PositionsCoverFullDay` | For PT1H: exactly 24 positions per day. Positions are 1-indexed and contiguous |
| `Fixture_PeriodAlignment` | Period start/end are UTC midnight boundaries |

**Where do fixture values come from?**

For golden master tests to work, fixture data must have **known, hand-calculable values**. Suggested approach:

```
rsm012-single-day.json:
  GSRN: 571313100000012345
  Date: 2025-01-15
  Resolution: PT1H
  24 hours, all with quantity = 1.000 kWh (simplest possible)
  Quality: A01 for all

rsm012-multi-day.json:
  Same GSRN, 2025-01-01 through 2025-01-31
  Each day: 24 hours × 1.000 kWh = 24 kWh/day
  Total: 744 kWh for January (31 days × 24 hours)

charges-grid-tariff.json:
  Grid area: 344
  Day rate (06-21): 0.15 DKK/kWh
  Night rate (21-06): 0.05 DKK/kWh
  Grid subscription: 49.00 DKK/month
  Valid from: 2025-01-01

charges-system-tariff.json:
  System tariff: 0.054 DKK/kWh
  Transmission tariff: 0.049 DKK/kWh
  Valid from: 2025-01-01
```

This gives us deterministic expected values for golden master tests.

---

### Step 4: CIM JSON Parser (RSM-012)

**What to build:**
- Parser that takes raw CIM JSON string → domain model (`MeteringData`)
- Extracts: MessageId, GSRN, period start/end, resolution, Point[] (position, quantity, quality)
- Validates required fields, rejects messages with missing mandatory data

**How to test (unit tests):**

| Test | Input | Assertion |
|------|-------|-----------|
| `Parse_ValidRsm012_ExtractsAllFields` | `rsm012-single-day.json` | GSRN = `571313100000012345`, 24 points, each quantity = 1.0 |
| `Parse_MultiSeries_ExtractsAll` | Fixture with 2 Series elements | Both series parsed correctly |
| `Parse_QualityA02_QuantityNull` | Point with quality A02, no quantity element | quantity = null, quality = A02 |
| `Parse_InvalidJson_ThrowsParseException` | `{invalid}` | Specific parse exception (not a generic crash) |
| `Parse_MissingGsrn_ThrowsValidationException` | Valid JSON but no MarketEvaluationPoint/mRID | Validation exception with clear message |
| `Parse_MissingPoints_ThrowsValidationException` | Valid JSON but empty Point[] array | Validation exception |
| `Parse_RoundTrip` | Parse fixture → serialize → parse again | Output matches original |

**Contract test:**
- Parse each fixture → re-serialize → assert structure matches. This ensures our parser/serializer stays aligned with the CIM format.

---

### Step 5: OAuth2 Auth Manager

**What to build:**
- `AuthManager` that fetches a token via `IDataHubClient.GetToken()` (or directly from token endpoint)
- Caches the token in memory
- Renews proactively before expiry (e.g., 5-minute margin on the 1-hour TTL)
- Falls back to renewal on 401

**How to test (unit tests):**

| Test | Assertion |
|------|-----------|
| `GetToken_FirstCall_FetchesNewToken` | Calls the underlying token source exactly once |
| `GetToken_SecondCall_ReturnsCached` | Does NOT call the token source again |
| `GetToken_AfterExpiry_FetchesNew` | After simulating clock advance past TTL, fetches a new token |
| `GetToken_ProactiveRenewal_RenewsBeforeExpiry` | With 5-min margin on 60-min TTL, renews at ~55 min |
| `GetToken_ConcurrentCalls_OnlyOneFetch` | 10 concurrent calls → only 1 actual token fetch |

For these tests, use a mock/stub token source (not the `FakeDataHubClient`) so the Auth Manager can be tested in isolation.

---

### Step 6: Ingestion Pipeline (Queue Poller + Store)

**What to build:**
- Queue Poller (`BackgroundService`) that calls `PeekTimeseries()` in a loop
- On message: parse CIM JSON → store in `metering_data` hypertable → dequeue
- Idempotency: check `processed_messages` table by MessageId before processing
- Dead-letter: on parse failure, write to `dead_letter` table and dequeue anyway

**How to test:**

**Unit tests (no DB, no fake client):**

| Test | Assertion |
|------|-----------|
| `Poller_OnMessage_CallsParserAndStore` | Verify the correct sequence of calls |
| `Poller_OnNull_WaitsBeforeRetry` | When peek returns null, poller waits the configured interval |

**Integration tests (FakeDataHubClient + real DB):**

| Test | Assertion |
|------|-----------|
| `Ingest_SingleDay_StoredCorrectly` | Load `rsm012-single-day.json` into fake → run poller once → query `metering_data` → 24 rows for the GSRN + date |
| `Ingest_MultiDay_AllStored` | Load 30 days of fixtures → run poller 30 times → 720 rows total |
| `Ingest_DuplicateMessageId_SkippedSecondTime` | Enqueue same message twice → only 24 rows in DB (not 48) |
| `Ingest_MalformedMessage_DeadLettered` | Enqueue invalid JSON → `dead_letter` table has 1 row, `metering_data` has 0, message is dequeued |
| `Ingest_AllDequeued_PollerStops` | After all messages consumed, fake returns null, poller is idle |
| `Ingest_VerifyDequeueCalledForEach` | After processing N messages, fake reports N dequeue calls with correct MessageIds |

---

### Step 7: Spot Price Ingestion

**What to build:**
- Fetch Nord Pool spot prices (DK1/DK2) from external API or mock
- Store in `spot_prices` table (price_area, hour, price_dkk)

**How to test:**

| Test | Assertion |
|------|-----------|
| `FetchSpotPrices_StoresCorrectly` | Mock API returns known prices → stored in DB → query returns matching values |
| `FetchSpotPrices_Idempotent` | Fetch twice for same date → no duplicates |
| `SpotPriceLookup_CorrectHour` | Given stored prices, lookup for 2025-01-15T14:00 returns the correct price |
| `SpotPriceLookup_MissingHour_ReturnsNull` | Lookup for a gap in data returns null (settlement engine must handle this) |

For MVP 1, the spot price source can be a simple mock/file-based provider. Real Nord Pool integration can follow.

---

### Step 8: Charges Ingestion (Tariff Rates)

**What to build:**
- Parse tariff updates from the Charges queue (via `FakeDataHubClient.PeekCharges()`)
- Store in `tariff_rates` table with grid area, rate type, time-of-day ranges, validity period

**How to test:**

| Test | Assertion |
|------|-----------|
| `ParseCharges_GridTariff_ExtractsRates` | `charges-grid-tariff.json` → day rate = 0.15, night rate = 0.05, subscription = 49.00 |
| `ParseCharges_SystemTariff_ExtractsRates` | `charges-system-tariff.json` → system = 0.054, transmission = 0.049 |
| `TariffLookup_DayHour_ReturnsDayRate` | For hour 14:00 in grid area 344 → 0.15 DKK/kWh |
| `TariffLookup_NightHour_ReturnsNightRate` | For hour 22:00 → 0.05 DKK/kWh |
| `TariffLookup_ValidityBoundary` | Rate valid from 2025-01-01: lookup on 2024-12-31 returns previous rate (or null) |

---

### Step 9: Settlement Engine

**What to build:**
- Deterministic settlement calculation: takes consumption[], spotPrices[], tariffRates[], productPlan, period → SettlementResult
- Invoice line types: energy, grid tariff, system tariff, transmission tariff, elafgift, grid subscription, supplier subscription, VAT
- Pro rata subscription for partial periods

**How to test (unit tests — the most important tests in the system):**

All settlement tests are pure functions: known inputs → assert known outputs. No DB, no fake client.

#### Golden Master #1: Simple spot customer, full month

```
Input:
  Period: 2025-01-01 to 2025-01-31 (744 hours)
  Consumption: 1.000 kWh every hour = 744 kWh total
  Spot price: 0.50 DKK/kWh every hour (simplified)
  Supplier margin: 0.04 DKK/kWh
  Grid tariff: day (06-21) = 0.15, night (21-06) = 0.05 DKK/kWh
  System tariff: 0.054 DKK/kWh
  Transmission tariff: 0.049 DKK/kWh
  Elafgift: 0.008 DKK/kWh
  Grid subscription: 49.00 DKK/month
  Supplier subscription: 39.00 DKK/month

Expected (hand-calculated):
  Energy:        744 kWh × (0.50 + 0.04) = 744 × 0.54 = 401.76 DKK
  Grid tariff:   day hours = 31 days × 15 hours = 465 hours × 0.15 = 69.75 DKK
                 night hours = 31 days × 9 hours = 279 hours × 0.05 = 13.95 DKK
                 total grid tariff = 83.70 DKK
  System tariff: 744 × 0.054 = 40.176 DKK
  Transmission:  744 × 0.049 = 36.456 DKK
  Elafgift:      744 × 0.008 = 5.952 DKK
  Grid sub:      49.00 DKK
  Supplier sub:  39.00 DKK
  Subtotal:      656.044 DKK
  VAT (25%):     164.011 DKK
  Total:         820.055 DKK
```

#### Golden Master #2: Partial period (mid-month start)

```
Input:
  Period: 2025-01-16 to 2025-01-31 (16 days, 384 hours)
  Consumption: 1.000 kWh every hour = 384 kWh
  Same rates as Golden Master #1

Expected (hand-calculated):
  Energy:        384 × 0.54 = 207.36 DKK
  Grid tariff:   day = 16 × 15 × 0.15 = 36.00 DKK
                 night = 16 × 9 × 0.05 = 7.20 DKK
                 total = 43.20 DKK
  System tariff: 384 × 0.054 = 20.736 DKK
  Transmission:  384 × 0.049 = 18.816 DKK
  Elafgift:      384 × 0.008 = 3.072 DKK
  Grid sub:      49.00 × (16/31) = 25.29 DKK (pro rata)
  Supplier sub:  39.00 × (16/31) = 20.13 DKK (pro rata)
  Subtotal:      338.604 DKK
  VAT (25%):     84.651 DKK
  Total:         423.255 DKK
```

#### Additional settlement unit tests

| Test | Assertion |
|------|-----------|
| `Settlement_ZeroConsumption_OnlySubscriptions` | 0 kWh → only subscription lines + VAT |
| `Settlement_SingleHour_CorrectLineAmounts` | 1 hour of data → each line has the correct single-hour calculation |
| `Settlement_MissingSpotPrice_ThrowsOrHalts` | Spot price gap → settlement does not produce incorrect results silently |
| `Settlement_NegativeQuantity_Handled` | If a data point has negative kWh (possible for production), it's handled correctly |
| `Settlement_RoundingBehavior` | Verify rounding strategy (2 decimal DKK) is consistent across all lines |
| `Settlement_VatAppliedToAllLines` | VAT = 25% of sum of lines 1-7, not calculated per-line |

---

### Step 10: End-to-End Pipeline Test

**What to build:**
- Nothing new — this test wires together steps 2-9

**The definitive MVP 1 test:**

```
Given:
  FakeDataHubClient loaded with:
    - 30 × rsm012 fixtures (full January for GSRN 571313100000012345)
    - 1 × charges fixture (grid tariffs for area 344)
  Database seeded with:
    - Spot prices for all of January (744 hours, all 0.50 DKK/kWh)
    - Elafgift rate: 0.008 DKK/kWh
    - Product plan: spot + 0.04 margin, 39 DKK/month subscription

When:
  Ingestion pipeline runs (polls fake until empty)
  Settlement engine runs for January 2025

Then:
  metering_data has 744 rows for the GSRN
  Settlement result matches Golden Master #1 exactly
  All 30 messages are dequeued in the fake
  No dead-letter entries
```

This is the exit-criteria test. When it passes, MVP 1 is done.

---

## Test Fixture Lifecycle

Fixtures are version-controlled alongside the code. The directory structure:

```
tests/
  fixtures/
    rsm012/
      rsm012-single-day.json
      rsm012-multi-day/
        day-01.json
        day-02.json
        ...
        day-31.json
    charges/
      charges-grid-tariff.json
      charges-system-tariff.json
    spot-prices/
      spot-prices-january-2025.json
    golden-masters/
      golden-master-1-full-month.json    (expected settlement result)
      golden-master-2-partial-period.json
```

**Fixture creation checklist:**
1. Based on CIM EDI Guide structure (Dok. 15/00718-191)
2. Cross-referenced with Energinet's [opengeh-edi](https://github.com/Energinet-DataHub/opengeh-edi) test data
3. Values chosen for hand-calculability (round numbers where possible)
4. Each fixture has a companion comment or README explaining the scenario

---

## FakeDataHubClient — Implementation Sketch

```
FakeDataHubClient : IDataHubClient
  Fields:
    _timeseriesQueue: Queue<CimMessage>     // loaded from fixtures
    _chargesQueue: Queue<CimMessage>        // loaded from fixtures
    _dequeuedIds: HashSet<string>           // tracks acknowledged messages
    _allMessages: Dictionary<string, CimMessage>  // lookup by MessageId

  Constructor(timeseriesFixtures: string[], chargesFixtures: string[]):
    Load each fixture file → parse into CimMessage → enqueue

  PeekTimeseries():
    If _timeseriesQueue is empty → return null
    Return _timeseriesQueue.Peek()   // peek, don't dequeue yet

  PeekCharges():
    Same as above for charges queue

  Dequeue(messageId):
    If messageId not in _allMessages → throw
    If messageId already in _dequeuedIds → no-op (idempotent)
    Remove from the appropriate queue
    Add to _dequeuedIds

  GetToken():
    Return "fake-token-for-testing"

  // Test helpers
  GetDequeuedIds(): IReadOnlySet<string>
  Reset(): Clear queues, reload fixtures, clear dequeued set
```

Key design decisions:
- **Peek returns the head of the queue** — same as real DataHub (FIFO, must dequeue before next)
- **Dequeue is idempotent** — calling it twice for the same ID is fine
- **No threading concerns** — the fake runs in-process, single-threaded for tests
- **Reset enables test isolation** — each test can start from a known state

---

## Test Matrix Summary

| Layer | # Tests | Speed | Dependencies |
|-------|---------|-------|-------------|
| **Fixture validation** | ~10 | ms | None (file I/O) |
| **CIM parser** | ~10 | ms | None (pure parsing) |
| **FakeDataHubClient** | ~8 | ms | None (in-memory) |
| **Auth Manager** | ~5 | ms | Mock token source |
| **Settlement engine** | ~10+ | ms | None (pure calculation) |
| **Ingestion pipeline (integration)** | ~6 | seconds | FakeDataHubClient + PostgreSQL |
| **Spot price ingestion** | ~4 | seconds | Mock API + PostgreSQL |
| **Charges ingestion** | ~5 | seconds | FakeDataHubClient + PostgreSQL |
| **End-to-end pipeline** | ~2 | seconds | FakeDataHubClient + PostgreSQL |
| **Total** | ~60 | <30s | |

Target: all tests run in under 30 seconds in CI.

---

## Rounding Strategy

Define and test the rounding strategy early — it affects every golden master:

| Context | Rule | Rationale |
|---------|------|-----------|
| kWh quantities | 3 decimal places (as received from DataHub) | CIM spec: max 3 decimals for KWH |
| Per-hour calculation | Full precision (no intermediate rounding) | Avoid accumulation of rounding errors |
| Invoice line totals | 2 decimal places (DKK øre) | Standard invoicing precision |
| VAT | 2 decimal places, calculated on the summed subtotal | Danish tax rules |
| Rounding mode | `MidpointRounding.AwayFromZero` (banker's rounding also acceptable) | Decide once, test explicitly |

A dedicated `Rounding_Strategy_Test` should verify that the engine produces the same result regardless of whether you sum hourly rounded values or round the final sum. If they differ, the test documents which approach we chose and why.

---

## What Comes After MVP 1 (Simulator Evolution)

For context — the simulator grows in MVP 2+, but none of this is in scope now:

| MVP | Simulator addition |
|-----|--------------------|
| **MVP 2** | Upgrade to standalone HTTP server (ASP.NET Minimal API, Docker). Add MasterData queue, BRS request endpoints, scenario engine |
| **MVP 3** | Error injection (401, 503, malformed). Correction scenarios. Aggregations queue. Run in parallel with real DataHub (Actor Test) |
| **MVP 4** | Performance scenarios (80K metering points). Realistic timing patterns |

The `IDataHubClient` interface means switching from `FakeDataHubClient` to `SimulatorDataHubClient` (HTTP) to `RealDataHubClient` is a configuration change, not a rewrite.

---

## Exit Criteria Checklist

- [ ] `docker compose up` starts PostgreSQL/TimescaleDB without errors
- [ ] `FakeDataHubClient` passes all unit tests (peek, dequeue, reset, empty queue)
- [ ] All fixture files pass validation tests (valid JSON, required fields, realistic values)
- [ ] CIM parser correctly parses all fixture files (round-trip test passes)
- [ ] Auth Manager caches and renews tokens correctly
- [ ] Ingestion pipeline: fake → parse → store → dequeue works for 30 days of data
- [ ] Duplicate MessageId is skipped (idempotency)
- [ ] Malformed message goes to dead-letter and is dequeued
- [ ] Spot prices stored and queryable by hour
- [ ] Tariff rates parsed from Charges fixtures and stored with correct time-of-day ranges
- [ ] Settlement engine: Golden Master #1 (full month) passes
- [ ] Settlement engine: Golden Master #2 (partial period) passes
- [ ] End-to-end pipeline test: 30 days ingested → settlement matches golden master
- [ ] All tests run in CI on every push
- [ ] No dead-letter entries in the end-to-end test

---

## Sources

- [Implementation plan](datahub3-implementation-plan.md) — MVP definitions, simulator architecture, test pyramid
- [RSM-012 reference](rsm-012-datahub3-measure-data.md) — CIM message format, API endpoints
- [Settlement overview](datahub3-settlement-overview.md) — what settlement is
- [Product structure and billing](datahub3-product-and-billing.md) — invoice lines, rates, aconto
- [Authentication and security](datahub3-authentication-security.md) — OAuth2 flow
- [Edge cases](datahub3-edge-cases.md) — corrections (not in MVP 1 scope, but informs parser design)
- [Proposed architecture](datahub3-proposed-architecture.md) — technology stack, data model
