# Next Development Phase: MVP 3 Completion + MVP 4 Foundation

Plan for the next phase of DataHub.Settlement development. MVP 3 delivered the core edge-case calculations (corrections, elvarme, solar, erroneous switch — all verified by golden master tests), but several integration-level features and hardening work remain incomplete. MVP 4 introduces production readiness concerns.

This phase bridges the two: close out MVP 3 gaps, then lay the MVP 4 foundation.

---

## Current State Assessment

### MVP 3 — What's Done

| Feature | Status | Evidence |
|---------|--------|----------|
| Corrections & delta calculation | Complete | `CorrectionEngine.cs`, GM#5, GM#9 |
| Elvarme split-rate threshold | Complete | `AnnualConsumptionTracker`, GM#7 |
| Solar/E18 net settlement | Complete | Settlement engine branching, GM#8 |
| Erroneous switch (BRS-042) | Complete | `ErroneousSwitchService.cs`, GM#6, `BrsRequestBuilder.BuildBrs042()` |
| Reconciliation (RSM-014 parser) | Complete | `CimJsonParser.ParseRsm014()`, `ReconciliationService.cs` |
| Move-in/Move-out (BRS-009/010) | Complete | Builder methods, simulator scenarios, integration tests |
| Tariff change mid-period | Complete | `PeriodSplitter`, GM#10 |
| Resilient DataHub client (401/503) | Complete | `ResilientDataHubClient.cs`, unit tests |
| Dead-letter handling | Complete | `MessageLog.cs`, `QueuePollerTests.cs` |
| Missing spot price validation | Complete | `SpotPriceValidator.cs` |
| All 10 golden master tests | Passing | GM#1–GM#10 |

### MVP 3 — What's Missing

| Feature | Gap | Priority |
|---------|-----|----------|
| Aggregations queue persistence | Handler is a stub — logs but doesn't store or compare | P1 |
| Simulator error injection | No 401/503/malformed scenarios in simulator | P2 |
| BRS-011 (erroneous move) | Zero code | P2 |
| RSM-015/016 (historical data requests) | Zero code | P3 |
| Customer disputes workflow | Zero code | P3 |
| Concurrent edge-case tests | No test for correction-during-switch or mid-quarter move-out with aconto | P2 |

---

## Phase Structure

The phase is split into two tracks that can overlap. The **Onboarding API** (B1) is the top priority — it is the entry point for all sales channels and must be built first to establish the API-first integration pattern that everything else depends on.

```
Track A: MVP 3 Completion (close the gaps)
  A1. Aggregations persistence & reconciliation comparison
  A2. Simulator error injection scenarios
  A3. BRS-011 erroneous move
  A4. Concurrent edge-case integration tests
  A5. RSM-015/016 historical data requests

Track B: MVP 4 Foundation (production readiness)
  B1. Onboarding API — sales channel entry point          ★ TOP PRIORITY
  B2. Settlement result export API
  B3. Invoice generation model
  B4. Customer portal data layer
  B5. Monitoring & health checks
  B6. Performance baseline & load testing
```

Track A has no dependencies on Track B. They can be developed in parallel.

---

## Track A: MVP 3 Completion

### A1. Aggregations Queue Persistence & Reconciliation Comparison

**Problem:** `QueuePollerService.ProcessAggregationsAsync()` is a stub. It logs the RSM-014 message but doesn't persist the data or trigger reconciliation comparison.

**What to build:**

| Task | Detail |
|------|--------|
| Create `datahub.aggregation_data` table | Store RSM-014 aggregation results per grid area, period, resolution |
| Migration V022 | `CREATE TABLE datahub.aggregation_data (id, grid_area, period_start, period_end, resolution, total_kwh, source_message_id, received_at)` |
| `AggregationRepository` | Store and query aggregation data |
| Wire `QueuePollerService` | On RSM-014: parse → store → trigger reconciliation |
| Auto-reconciliation | After storing aggregation, compare against own settlement for same grid area + period |
| Store discrepancies | `datahub.reconciliation_result` table with match/mismatch status, delta, deviating GSRNs |
| API: Reconciliation results | Expose reconciliation results via API for back-office consumption |

**Tests:**

| Test | Type |
|------|------|
| RSM-014 → store → query roundtrip | Integration |
| Matching aggregation produces "match" result | Unit |
| Mismatching aggregation produces discrepancy with correct delta | Unit |
| Full pipeline: enqueue RSM-014 → poll → store → reconcile → result | Integration |

**Exit criteria:** RSM-014 messages are persisted, automatically compared against own settlement, and discrepancies are surfaced in the dashboard.

---

### A2. Simulator Error Injection

**Problem:** The simulator only handles happy-path scenarios. The `ResilientDataHubClient` has unit tests for 401/503 retry, but there's no end-to-end test proving the full polling pipeline recovers from errors.

**What to build:**

| Scenario | Simulator behavior | System expectation |
|----------|-------------------|-------------------|
| Token expiry | Return 401 on next peek | Renew token, retry, succeed |
| Service unavailable | Return 503 for N requests, then 200 | Backoff, retry, resume |
| Malformed message | Return invalid JSON on peek | Dead-letter, dequeue, continue |
| Partial outage | 503 on Timeseries only, other queues normal | Other queues unaffected |

**Implementation:**

| Task | Detail |
|------|--------|
| Admin endpoint: `POST /admin/inject-error` | Configure next N responses for a specific queue to return a given status code |
| Admin endpoint: `POST /admin/inject-malformed` | Enqueue invalid JSON on a specific queue |
| `ErrorInjectionMiddleware` in Simulator | Intercept peek requests, check error injection state |
| Integration tests | 4 scenarios above, verified end-to-end |

**Tests:**

| Test | Type |
|------|------|
| 401 → token refresh → retry succeeds | Integration (simulator) |
| 503 × 3 → backoff → resume | Integration (simulator) |
| Malformed JSON → dead-letter → next message OK | Integration (simulator) |
| Partial outage → unaffected queues continue | Integration (simulator) |

**Exit criteria:** All 4 error injection scenarios pass as integration tests against the Docker simulator.

---

### A3. BRS-011 Erroneous Move

**Problem:** BRS-011 (correcting a move-in or move-out date) is not implemented. When the original move date was wrong, the supply period dates need to change and settlement recalculated.

**What to build:**

| Task | Detail |
|------|--------|
| `BrsRequestBuilder.BuildBrs011()` | CIM JSON for erroneous move request with corrected date |
| State machine transition | New process type `BRS011`, similar lifecycle to BRS-042 |
| Supply period adjustment | On confirmation: update `supply_period.start_date` or `end_date` |
| Recalculation trigger | After date adjustment, recalculate settlement for the affected period |
| Pro-rata subscription adjustment | Subscriptions recalculated for the new period length |
| Aconto adjustment | If aconto payments exist, recalculate proportional amounts |
| Simulator endpoint | `POST /v1.0/cim/requestcorrectionofmove` (or similar — verify DataHub endpoint name) |
| Simulator scenario: `erroneous_move` | BRS-011 request → confirmation → updated RSM-012 |

**Tests:**

| Test | Type |
|------|------|
| BRS-011 request builder produces valid CIM JSON | Unit |
| Supply period date adjustment | Unit |
| Settlement recalculation after date change | Unit (golden master candidate) |
| Full BRS-011 flow against simulator | Integration |

**Exit criteria:** BRS-011 can be submitted, confirmed, and the system recalculates settlement with corrected dates.

---

### A4. Concurrent Edge-Case Integration Tests

**Problem:** Golden master tests verify the calculations for edge cases, but no integration tests verify that concurrent real-world scenarios (correction arriving during a switch, move-out mid-quarter) work through the full pipeline.

**What to build:**

| Scenario | What to test |
|----------|-------------|
| Correction during active switch | Enqueue BRS-001 + RSM-012 correction for overlapping period → only delta within our supply period is settled |
| Mid-quarter move-out + aconto | Customer moves out 6 weeks into quarter → partial settlement + aconto reconciliation |
| Tariff change + correction | Grid company changes tariff mid-month, then sends correction for same month → both applied correctly |

**Implementation:** These are pure integration tests — no new production code expected (the calculation logic already exists). The tests exercise the full pipeline: simulator → queue poller → parser → repository → settlement engine → result verification.

**Exit criteria:** All 3 concurrent scenarios pass as integration tests.

---

### A5. RSM-015/016 Historical Data Requests

**Problem:** No ability to request historical validated data (RSM-015) or detailed aggregated data (RSM-016) from DataHub. These are needed for reconciliation dispute resolution and customer dispute handling.

**What to build:**

| Task | Detail |
|------|--------|
| `BrsRequestBuilder.BuildRsm015Request()` | Request validated data for a GSRN + period |
| `BrsRequestBuilder.BuildRsm016Request()` | Request detailed aggregated data for grid area + period |
| `CimJsonParser.ParseRsm015()` | Parse response — validated metering data (similar to RSM-012) |
| `CimJsonParser.ParseRsm016()` | Parse response — detailed aggregation data |
| `IDataHubClient` extensions | `RequestHistoricalData()` and `RequestDetailedAggregation()` methods |
| Wire into reconciliation | When discrepancy detected, auto-request RSM-015 for deviating GSRNs |
| Simulator endpoints | Respond to RSM-015/016 requests with fixture data |

**Tests:**

| Test | Type |
|------|------|
| RSM-015 request builder produces valid CIM JSON | Unit |
| RSM-016 request builder produces valid CIM JSON | Unit |
| RSM-015 response parser | Unit (fixtures) |
| RSM-016 response parser | Unit (fixtures) |
| Reconciliation discrepancy → auto RSM-015 → resolve | Integration |

**Exit criteria:** System can request and parse historical data from DataHub. Reconciliation auto-requests RSM-015 when discrepancies are found.

---

## Track B: MVP 4 Foundation

### B1. Onboarding API — Sales Channel Entry Point ★ TOP PRIORITY

**Problem:** The settlement system can process customers end-to-end, but there's no entry point for "a customer just signed up." Sales happen through three channels — website (self-service), mobile app (self-service), and customer service (phone). Today, customer creation is manual or demo-seeded. All three channels need the same programmatic entry point.

**Terminology:** "Sales channel" = the system calling the API (website, mobile app, customer service UI). "Customer" = the person signing up for electricity.

**Design principles:**
- **API-first.** All sales channels call the same endpoints. One API, multiple callers.
- **Simple API, smart backend.** The sales channel just submits a signup and tracks status. All DataHub complexity (which BRS process, notice periods, rejections, retries) is handled internally by the orchestration layer.
- **Margin is the product.** Customers choose a supplier based on margin + subscription — grid tariffs, system tariffs, and taxes are pass-through costs identical across all suppliers. The API only needs to present our own pricing, not a full invoice estimate.

```
┌──────────────┐  ┌──────────────┐  ┌──────────────┐
│  Website     │  │  Mobile App  │  │  Back Office  │
│  (self-serv) │  │  (self-serv) │  │  (cust. svc)  │
└──────┬───────┘  └──────┬───────┘  └──────┬───────┘
       │                 │                 │
       └─────────────────┼─────────────────┘
                         │
                  ┌──────▼──────┐
                  │  Onboarding │  Simple API surface
                  │     API     │  (4 endpoints)
                  └──────┬──────┘
                         │
              ┌──────────▼──────────┐
              │  Orchestration      │  Owns the complexity:
              │  Layer              │  • BRS-001 vs BRS-009
              │                     │  • 15 business day calc
              │                     │  • RSM-001 → status
              │                     │  • RSM-022 → activation
              └─────────────────────┘
```

**Scope boundary:** The back-office web application (for handling rejections, GSRN disambiguation, manual corrections) will be a **separate project within this repo** — it shares the same database and domain models but is not part of the B1 scope. The existing `DataHub.Settlement.Web` project is a development/testing dashboard only.


---

#### Business context: what the sales channel does NOT need to know

The Danish energy market has structural complexity that the API must hide from the sales channel:

| Complexity | Who handles it | Sales channel provides |
|-----------|---------------|-------------------|
| **GSRN discovery** — customer knows address, not GSRN | We look up GSRN from DAR ID via Energinet address API | DAR ID (address identifier) |
| **BRS-001 vs BRS-009** — supplier switch or move-in? Different processes, different notice periods | Orchestration maps `type` to correct BRS process | `type`: `switch` or `move_in` |
| **Effective date and notice periods** — BRS-001 requires 15 business days, BRS-009 can be immediate | Orchestration validates the date against process-specific rules | Desired effective date |
| **Grid area unknown at signup** — tariffs only arrive via RSM-022 after activation | Irrelevant for signup — grid tariffs are pass-through, same regardless of supplier | Nothing — products show margin + subscription only |
| **Rejection handling** — CPR/CVR mismatch, conflicting process (E16), invalid GSRN | Status shows `rejected` with reason. Back office reviews and handles correction manually | Nothing — status shows `rejected` with reason |
| **RSM-022 activation** — master data arrives on effective date, reveals grid area, settlement method, meter type | Orchestration processes RSM-022, assigns tariffs, activates metering point | Nothing — status changes to `active` |

**The sales channel's mental model:** "I submitted a signup. It's either processing, active, rejected, or cancelled."

#### Three addresses, three roles

The system distinguishes between three potentially different entities and addresses:

| Role | What | Identified by | Address stored |
|------|------|---------------|----------------|
| **Supply point** | Where the meter is | GSRN (resolved from DAR ID) | Not stored — implicit in GSRN + grid area |
| **Customer** | Who holds the contract | CPR/CVR + name | `customer.billing_*` fields (postal address) |
| **Payer** | Who pays the invoice | `payer.cpr_cvr` | `payer.billing_*` fields |

In most cases, the customer is the payer (default — `contract.payer_id` is NULL). When a separate payer is specified at signup, the system creates a `payer` record and links it to the contract. Examples: parent paying for student child, company paying for employee, landlord paying for tenant.

---

#### API surface

| Endpoint | Purpose |
|----------|---------|
| `GET /api/products` | List products the sales channel can offer (margin, subscription, spot/fixed) |
| `POST /api/signup` | Submit a new customer signup |
| `GET /api/signup/{id}/status` | Check signup progress |
| `POST /api/signup/{id}/cancel` | Cancel before activation |

Four endpoints. Back office handles GSRN disambiguation (multiple meters per address) before calling the API.

**Request/response:**

```
POST /api/signup
{
  "dar_id": "0a3f50a0-75eb-32b8-e044-0003ba298018",
  "customer_name": "Anders Jensen",
  "cpr_cvr": "0101901234",
  "contact_type": "private",
  "email": "anders@example.dk",
  "phone": "+4512345678",
  "product_id": "<uuid>",
  "type": "switch",
  "effective_date": "2026-04-01",

  // Billing address (customer's postal address — distinct from supply point)
  "billing_street": "Nørrebrogade",
  "billing_house_number": "42",
  "billing_floor": "3",
  "billing_door": "th",
  "billing_postal_code": "2200",
  "billing_city": "København N",

  // Optional: separate payer (omit if customer pays their own bills)
  "payer_name": "Jensen Holding ApS",
  "payer_cpr_cvr": "12345678",
  "payer_contact_type": "business",
  "payer_email": "regnskab@jensen.dk",
  "payer_billing_street": "Strandvejen",
  "payer_billing_house_number": "100",
  "payer_billing_postal_code": "2900",
  "payer_billing_city": "Hellerup"
}

→ 201 Created
{
  "signup_id": "SGN-2026-00042",
  "status": "registered",
  "gsrn": "571313180000000001",
  "effective_date": "2026-04-01"
}
```

The sales channel provides a **DAR ID** (Danish Address Register identifier). We resolve the GSRN internally. The `type` field is either `switch` (BRS-001 — taking over from another supplier) or `move_in` (BRS-009 — no current supplier). The `effective_date` is the customer's desired start date.

```
GET /api/signup/SGN-2026-00042/status

→ 200 OK
{
  "signup_id": "SGN-2026-00042",
  "status": "processing",
  "gsrn": "571313180000000001",
  "effective_date": "2026-04-01",
  "rejection_reason": null
}
```

**External statuses** (simplified from the internal DataHub state machine):

| External status | Internal states mapped | Meaning |
|----------------|----------------------|---------|
| `registered` | Process created, not yet sent | We have your signup, preparing to send to DataHub |
| `processing` | `sent_to_datahub`, `acknowledged`, `effectuation_pending` | In progress — DataHub is handling the switch |
| `active` | `completed` | Supply is live, metering data flowing |
| `rejected` | `rejected` | DataHub rejected — reason provided |
| `cancelled` | `cancelled` | Customer or system cancelled |

The sales channel never sees `sent_to_datahub` vs. `acknowledged` vs. `effectuation_pending` — that's internal. They just see "processing."

---

#### Orchestration layer (internal — what the API hides)

This is where the real complexity lives. A key design decision shapes the entire flow:

**Portfolio entities are NOT created at signup.** The signup only creates a customer record, a signup tracking record, and a process request. Metering point, contract, and supply period are created later when RSM-022 arrives with confirmed data from DataHub. This avoids placeholder data and conflicts with the existing `QueuePollerService` which creates metering points from RSM-022.

**Existing orchestration gaps that must be filled:**

| Gap | What's missing | What to build |
|-----|---------------|---------------|
| Sending BRS-001/009 | No service picks up pending process requests and sends them to DataHub | Extend `ProcessSchedulerService` to send pending requests via `HttpDataHubClient` |
| Processing RSM-001 | `QueuePollerService` doesn't handle acceptance/rejection receipts | Add RSM-001 handling to `QueuePollerService.ProcessMasterDataAsync()` |
| Signup status sync | No link between process state changes and signup status | Wire `OnboardingService.SyncFromProcessAsync()` into queue poller |
| Portfolio creation on activation | Current RSM-022 handler creates metering point but not contract | Extend RSM-022 handler to also create contract + supply period from signup data |

**Step 1: Validate and create (synchronous, in API request)**
```
1. Look up GSRN from DAR ID via address lookup service
   - If no GSRN found: return 400 with error
   - If multiple GSRNs: return 400 — back office handles disambiguation
2. Validate GSRN format (18 digits, starts with "57")
3. Check no active signup already exists for this GSRN
4. Validate product exists and is active
5. Validate effective date:
   - type = "switch": must be ≥ 15 business days from today
   - type = "move_in": must be ≥ today
6. Map type to process type: "switch" → supplier_switch, "move_in" → move_in
7. Create customer record (name, CPR/CVR, contact type)
8. Create signup record (dar_id, gsrn, customer_id, product_id, type, effective_date)
9. Create process request (pending)
10. Return signup ID + resolved GSRN immediately
```

Portfolio entities NOT created here. No metering point, no contract, no supply period.

**Step 2: Send to DataHub (background — ProcessSchedulerService, NEW)**
```
1. Pick up process requests with status "pending"
2. Look up signup record by process_request_id to get CPR/CVR
3. Build BRS request:
   - supplier_switch → BRS-001 (gsrn, cpr_cvr, effective_date)
   - move_in → BRS-009 (gsrn, cpr_cvr, effective_date)
4. Send via HttpDataHubClient.SendRequestAsync()
5. Store correlation ID from response
6. Transition process: pending → sent_to_datahub
7. Sync signup status: registered → processing
```

This fills the existing gap where nothing sends pending requests to DataHub.

**Step 3: Handle RSM-001 receipt (background — QueuePollerService, NEW)**
```
On RSM-001 from MasterData queue:
  - Parse acceptance/rejection
  - Look up process by correlation ID

  If accepted:
    - Transition: sent_to_datahub → acknowledged → effectuation_pending
    - Sync signup status (stays "processing")

  If rejected:
    - Transition: sent_to_datahub → rejected
    - Store rejection reason
    - Sync signup status: processing → rejected (with reason)
```

**Step 4: Handle RSM-022 activation (background — QueuePollerService, EXTENDED)**
```
On RSM-022 from MasterData queue:
  - Parse master data (grid area, type, settlement method, price area)
  - Existing behavior: create/update metering point, ensure grid area

  NEW — create portfolio from signup:
  - Look up signup by GSRN
  - Create contract (customer_id from signup, product_id from signup, GSRN, effective_date, billing_frequency=quarterly, payment_model=aconto)
  - Create supply period (GSRN, effective_date)
  - Activate metering point
  - Transition process: effectuation_pending → completed
  - Sync signup status: processing → active
```

Now the portfolio only contains confirmed, real data from DataHub.

**Step 5: Cancellation**
```
POST /api/signup/{id}/cancel:
  - If signup status is "registered" (not yet sent):
    - Cancel process internally
    - Sync signup: registered → cancelled

  - If signup status is "processing" or "awaiting_effectuation" (BRS sent, before effective date):
    - Send RSM-002 cancel within BRS-001 (same correlation ID) to DataHub
    - Wait for confirmation (RSM-002 accept/reject)
    - Sync signup: processing → cancellation_pending → cancelled

  - If signup status is "active":
    - Return 409 — too late, use offboarding (BRS-002) instead
```

---

#### Address-to-GSRN lookup

The API accepts a **DAR ID** (Danish Address Register identifier), not a raw GSRN. The `AddressLookupService` resolves DAR ID → GSRN(s) internally.

**Edge case: multiple GSRNs per address.** An apartment building may have multiple metering points at the same DAR ID. The API returns 400 if the lookup returns multiple GSRNs. Back office handles disambiguation (identifying the correct metering point) and re-submits with the right address.

**Stub implementation:** The existing `StubAddressLookupClient` generates deterministic GSRNs from DAR IDs. Real Energinet API integration is a future task — the interface is already abstracted via `IAddressLookupClient`.

---

#### Business decisions (resolved)

| Decision | Resolution |
|----------|-----------|
| **Default billing model** | **Aconto.** Quarterly pre-payment is the standard. Settlement still calculates actual consumption per hour — the difference between actual and aconto paid is reconciled each quarter. |
| **Rejection handling** | **Back office handles all rejections.** The system surfaces the rejection reason via the status endpoint. No auto-retry logic. Back office staff (customer service) review the rejection, correct the data, and re-submit manually. |
| **Multiple GSRNs per DAR ID** | **Back office handles disambiguation.** If the address lookup returns multiple metering points, back office resolves which one is correct before creating the signup. The API does not need a disambiguation flow. |
| **Switch vs. move-in determination** | **Back office decides.** The sales channel / back office provides the `type` field. The system does not attempt to figure out whether there's a current supplier — that's a back-office concern. |
| **Multiple signups per customer** | **Allowed.** One customer can have multiple metering points (e.g., house + garage). |

---

#### What to build

| Layer | Task |
|-------|------|
| **Migration V021** | `portfolio.signup` table (dar_id, gsrn, customer_id, product_id, process_request_id, type, effective_date, status). Product table fixes: drop `binding_period_months` (binding periods not allowed in DK electricity market), add `description`, `green_energy`, `display_order` |
| **Application** | `OnboardingService` (DAR ID → GSRN lookup, validate, create signup, cancel, sync from process), `GsrnValidator`, `BusinessDayCalculator`, `ISignupRepository`, models |
| **Infrastructure** | `SignupRepository` (Dapper), `BusinessDayCalculator` (Danish holidays), product listing query |
| **API** | 4 endpoints in `DataHub.Settlement.Api`, DI wiring, OpenAPI/Swagger |
| **ProcessSchedulerService** (extend) | Pick up `pending` process requests, look up signup for CPR/CVR, build + send BRS-001/009 via `HttpDataHubClient`, transition to `sent_to_datahub` |
| **QueuePollerService** (extend) | Add RSM-001 handling (accepted → acknowledged, rejected → rejected + reason). Extend RSM-022 handler to create contract + supply period from signup data |
| **Signup status sync** | Wire `OnboardingService.SyncFromProcessAsync()` into queue poller on every process state change |
| **Simulator** | Onboarding scenario: signup → BRS-001 → RSM-001 accepted → RSM-022 → active |

**Tests:**

| Test | Type |
|------|------|
| GSRN validation: format, prefix, length | Unit |
| Business day calculation: skip weekends, 15-day minimum | Unit |
| Effective date validation: switch requires 15 days, move_in allows today | Unit |
| DAR ID → GSRN lookup succeeds | Unit |
| DAR ID with multiple GSRNs returns 400 | Unit |
| Signup creates customer + signup + process (no metering point, no contract) | Integration |
| Signup queues correct BRS process (BRS-001 for switch, BRS-009 for move_in) | Integration |
| ProcessSchedulerService sends pending BRS-001 to DataHub | Integration |
| RSM-001 accepted → process acknowledged | Integration |
| RSM-001 rejected → signup status rejected with reason | Integration |
| RSM-022 → creates metering point + contract + supply period from signup | Integration |
| Status reflects simplified external states | Integration |
| Cancel before send (registered → cancelled) | Integration |
| Cancel after send (processing → RSM-002 cancel → cancellation_pending → cancelled) | Integration |
| Cancel after activation returns 409 | Integration |
| Full flow: signup → BRS-001 → RSM-001 → RSM-022 → portfolio created → status = active | Integration (simulator) |

**Exit criteria:**
- All sales channels can create customers through 4 API endpoints
- Sales channel sees simple status progression (registered → processing → active)
- Pending process requests are automatically sent to DataHub (gap filled)
- RSM-001 receipts are processed (gap filled)
- RSM-022 creates portfolio entities from signup data (contract, supply period)
- Portfolio only contains confirmed data — no placeholders
- Cancellation works at all stages (registered, processing)
- Full signup → activation flow works end-to-end against the simulator

---

### B1b. Back-Office Web Application

**Problem:** Back-office staff (customer service) need a UI to create signups, handle DataHub rejections, disambiguate GSRNs, and monitor the onboarding pipeline. The settlement system provides the API — this is the application that consumes it.

**Technology:** React + Vite + Tailwind CSS. Plain HTML tables and forms. No component library. Lives in `backoffice/` at the repo root alongside `DataHub.Settlement/`. Calls the API over HTTP.

**Scope boundary:** This is a separate project within the same repo. It shares no code with the .NET backend — it talks exclusively through the REST API.

---

#### What back-office staff do

| Task | How it works today | What the app provides |
|------|-------------------|----------------------|
| **Create a signup** | Manual / not possible | Form: enter address → resolve GSRN → select product → submit |
| **Handle rejected signup** | Not visible | See rejection reason, correct data, re-submit as new signup |
| **Disambiguate address** | Not possible | Address lookup shows all GSRNs, staff picks the right one |
| **Monitor signups** | Not visible | Table of all signups, filterable by status |
| **Cancel signup** | Not possible | Cancel button on signup detail |
| **View customer** | Database query | Customer detail: contracts, metering points, supply periods |

---

#### Pages

| Page | Route | What it shows |
|------|-------|---------------|
| **Signup list** | `/signups` | Table of all signups with status filter (all, registered, processing, active, rejected, cancelled). Columns: signup number, customer name, GSRN, type, effective date, status, created. Click row → detail |
| **New signup** | `/signups/new` | Step 1: Enter DAR ID → API returns GSRN(s). If multiple, show picker. Step 2: Select product. Step 3: Enter customer details (name, CPR/CVR, contact type, email, phone). Step 4: Choose type (switch/move-in), effective date. Submit. |
| **Signup detail** | `/signups/:id` | Full signup info, current status, rejection reason if rejected, process event timeline, cancel button (if cancellable) |
| **Customer list** | `/customers` | Table of all customers. Columns: name, CPR/CVR, contact type, status. Click → detail |
| **Customer detail** | `/customers/:id` | Customer info, list of signups, active contracts, metering points, supply periods |
| **Products** | `/products` | Table of products. Inline edit for description, display order, green energy flag |

---

#### API endpoints needed (missing from B1)

The current API has 4 endpoints for the signup flow. The back-office app needs more:

| Endpoint | Purpose | Status |
|----------|---------|--------|
| `GET /api/products` | List active products | Exists |
| `POST /api/signup` | Create signup | Exists |
| `GET /api/signup/{id}/status` | Signup status | Exists |
| `POST /api/signup/{id}/cancel` | Cancel signup | Exists |
| **`GET /api/signups`** | List all signups, filter by status | **New** |
| **`GET /api/signups/{id}`** | Full signup detail (includes customer info) | **New** |
| **`GET /api/signups/{id}/events`** | Process event timeline for a signup | **New** |
| **`GET /api/address/{darId}`** | Look up address → list of GSRNs | **New** |
| **`GET /api/customers`** | List customers | **New** |
| **`GET /api/customers/{id}`** | Customer detail with contracts, metering points | **New** |

No product CRUD yet — back-office can view products, creation/editing stays in the database for now.

---

#### What to build

| Layer | Task |
|-------|------|
| **API** (extend `DataHub.Settlement.Api`) | 6 new endpoints listed above. Add CORS for local dev (React on :5173, API on :5001). |
| **Repository** (extend) | `ISignupRepository.GetAllAsync(status?)`, `IPortfolioRepository.GetCustomerAsync(id)`, `IPortfolioRepository.GetCustomersAsync()`, `IPortfolioRepository.GetContractsForCustomerAsync(id)` |
| **Frontend project** (`backoffice/`) | Vite + React + Tailwind. `fetch()` wrapper for API calls. Pages: signup list, new signup, signup detail, customer list, customer detail, products. |

---

#### Frontend structure

```
backoffice/
├── index.html
├── package.json
├── vite.config.js
├── tailwind.config.js
├── src/
│   ├── main.jsx
│   ├── api.js              — fetch wrapper (base URL, error handling)
│   ├── App.jsx             — routes
│   ├── layout/
│   │   └── Layout.jsx      — sidebar nav + main content area
│   ├── pages/
│   │   ├── SignupList.jsx
│   │   ├── SignupNew.jsx
│   │   ├── SignupDetail.jsx
│   │   ├── CustomerList.jsx
│   │   ├── CustomerDetail.jsx
│   │   └── Products.jsx
│   └── index.css           — Tailwind imports
```

No state management library. React state + `useEffect` for data fetching. Simple.

---

#### Implementation order

1. **API first** — add the 6 missing endpoints + CORS. This is backend work, testable independently.
2. **Frontend scaffold** — Vite + React + Tailwind project setup, routing, layout with sidebar nav.
3. **Signup list page** — table with status filter, the most immediately useful page.
4. **Address lookup + new signup** — the full creation flow with GSRN resolution.
5. **Signup detail** — view details, event timeline, cancel.
6. **Customer pages** — list and detail.
7. **Products page** — read-only listing.

---

#### Exit criteria

- Back-office staff can create signups through the UI, including GSRN disambiguation
- Rejected signups are visible with reasons — staff can create a corrected re-submission
- All signups are listed and filterable by status
- Process event timeline visible on signup detail
- Customer list and detail pages functional
- Frontend runs on `localhost:5173`, API on `localhost:5001`

---

### B2. Settlement Result Export API

**Problem:** Settlement results exist only in the database and dashboard. No programmatic way for external systems (ERP, billing) to retrieve them.

**What to build:**

| Task | Detail |
|------|--------|
| REST API endpoints in `DataHub.Settlement.Api` | `GET /api/settlement/runs` — list settlement runs with filters (date, grid area, status) |
| | `GET /api/settlement/runs/{id}` — detailed run with all line items |
| | `GET /api/settlement/runs/{id}/lines` — line items with pagination |
| | `GET /api/settlement/customers/{gsrn}/invoices` — invoice history per metering point |
| Response DTOs | `SettlementRunDto`, `SettlementLineDto`, `InvoiceSummaryDto` |
| OpenAPI documentation | Swagger/Swashbuckle for API documentation |
| Authentication | API key or JWT (simple — production auth is MVP 4 proper) |

**Tests:**

| Test | Type |
|------|------|
| API returns correct settlement data | Integration |
| Pagination works correctly | Integration |
| Filter by date range, grid area, status | Integration |
| Empty results return 200 with empty list | Unit |

**Exit criteria:** External systems can query settlement results via REST API. API is documented with OpenAPI/Swagger.

---

### B3. Invoice Generation Model

**Problem:** Settlement produces line items, but there's no invoice entity that groups them into a customer-facing document with an invoice number, due date, and payment reference.

**What to build:**

| Task | Detail |
|------|--------|
| Domain model | `Invoice` entity with: invoice_number, customer_id, gsrn, period, issue_date, due_date, total_excl_vat, vat_amount, total_incl_vat, status (draft/issued/paid/credited), payment_reference |
| Migration V023 | `billing.invoice` table |
| `InvoiceGenerator` service | Takes settlement run → groups lines by customer/GSRN → creates invoice with number + due date |
| Invoice numbering | Sequential, per-year (e.g., `2026-00001`) |
| Credit note model | `CreditNote` linked to original invoice, for corrections/erroneous switches |
| PDF generation (stub) | Interface `IInvoiceRenderer` with a simple text/HTML implementation — real PDF is MVP 4 |

**Tests:**

| Test | Type |
|------|------|
| Settlement run → invoice with correct totals | Unit |
| Invoice numbering is sequential | Unit |
| Correction settlement → credit note linked to original | Unit |
| Invoice round-trip to database | Integration |

**Exit criteria:** Settlement runs produce invoices with proper numbers, due dates, and line items. Credit notes link to originals.

---

### B4. Customer Portal Data Layer

**Problem:** No API for customers to view their own consumption, invoices, and contract details. The dashboard is internal — a customer-facing portal needs a different data layer.

**What to build:**

| Task | Detail |
|------|--------|
| Read-only query service | `CustomerPortalQueryService` — optimized queries for customer-facing data |
| Consumption data API | `GET /api/portal/{gsrn}/consumption?from=&to=&resolution=` — hourly, daily, monthly aggregation |
| Invoice history API | `GET /api/portal/{gsrn}/invoices` — issued invoices with line-item breakdown |
| Contract details API | `GET /api/portal/{gsrn}/contract` — current product, margin, subscription |
| Spot price history API | `GET /api/portal/spot-prices?area=&from=&to=` — DK1/DK2 prices |

**Implementation notes:**
- These are **read-only** endpoints — no mutations
- Authentication is a stub (real auth with NemID/MitID is MVP 4 proper)
- Consumption queries leverage TimescaleDB `time_bucket()` for efficient aggregation
- Response DTOs are customer-friendly (no internal IDs, Danish-language labels)

**Tests:**

| Test | Type |
|------|------|
| Consumption aggregation returns correct hourly/daily/monthly values | Integration |
| Invoice history returns all issued invoices | Integration |
| Spot price query returns correct data for DK1/DK2 | Integration |

**Exit criteria:** Customer-facing data is queryable via API. TimescaleDB aggregation works for consumption data at different resolutions.

---

### B5. Monitoring & Health Checks

**Problem:** The system has OpenTelemetry tracing but no health checks, no structured alerting, and no operational metrics beyond what Aspire Dashboard shows.

**What to build:**

| Task | Detail |
|------|--------|
| Health check endpoints | `/health/live` (process alive), `/health/ready` (DB connected, queues reachable) |
| Database health check | Verify PostgreSQL connection, check migration status |
| DataHub connectivity check | Token endpoint reachable, queue peek returns 200 or 204 |
| Operational metrics | Counters: messages_processed, messages_dead_lettered, settlement_runs_completed, settlement_runs_failed |
| | Gauges: queue_depth (per queue), active_metering_points, pending_processes |
| | Histograms: message_processing_duration, settlement_run_duration |
| Alerting rules (config) | Define thresholds for: dead-letter rate > 5%, queue depth growing, settlement run failure |
| Structured logging | Ensure all log entries include CorrelationId, GSRN (where applicable), ProcessId |

**Tests:**

| Test | Type |
|------|------|
| Health check returns healthy when DB is up | Integration |
| Health check returns unhealthy when DB is down | Integration |
| Metrics increment correctly on message processing | Unit |

**Exit criteria:** Health endpoints work. Operational metrics are exposed. Structured logging includes correlation IDs.

---

### B6. Performance Baseline & Load Testing

**Problem:** The system works with demo data (a handful of metering points). MVP 4 requires 80K+ metering points. No performance baseline exists.

**What to build:**

| Task | Detail |
|------|--------|
| Load data generator | Script to seed N metering points with M months of hourly data |
| Baseline measurements | Time to: ingest 1 day of data for N points, run settlement for N points, query consumption for 1 GSRN |
| TimescaleDB tuning | Verify chunk interval, compression policy, retention policy at scale |
| Query optimization | Identify slow queries with `EXPLAIN ANALYZE` at 80K scale |
| Settlement engine profiling | Profile `SettlementEngine.Calculate()` at scale — memory, CPU |
| Document results | Performance report with baseline numbers and bottlenecks identified |

**Target scale:**

| Metric | Target |
|--------|--------|
| Metering points | 80,000 |
| Hourly readings per day | 80,000 × 24 = 1.92M rows |
| Monthly settlement run | 80,000 points × ~720 hours = ~57.6M calculations |
| Query: single GSRN, 1 month consumption | < 100ms |
| Full monthly settlement | < 30 minutes (WARNING: VERIFY — this is a rough target) |

**Exit criteria:** Performance baseline established. Bottlenecks identified. TimescaleDB configuration validated at scale.

---

## Execution Order

Recommended sequence. **B1 (Onboarding API) starts immediately** — it establishes the API-first pattern and is the single entry point all sales channels need.

```
Week 1-2:  B1 (onboarding API)               ★ TOP PRIORITY — sales channel entry point
           A1 (aggregations persistence)     — unblocks reconciliation dashboard

Week 3-4:  B1 (onboarding API, continued)    — full signup → activation flow, simulator scenario
           B5 (monitoring & health checks)   — independent, quick win

Week 5-6:  A2 (simulator error injection)     — test infrastructure
           B2 (settlement export API)        — follows B1 API patterns

Week 7-8:  A3 (BRS-011 erroneous move)        — new feature
           A4 (concurrent edge-case tests)    — test gap closure
           B3 (invoice generation model)      — depends on settlement being solid

Week 9-10: A5 (RSM-015/016 historical data)   — depends on A1 (reconciliation)
           B4 (customer portal data layer)    — depends on B2 (API patterns)

Week 11-12: B6 (performance baseline)         — depends on B1-B4 (APIs to benchmark)
            Polish, documentation, review
```

Items within the same week can be worked in parallel.

---

## Deferred to MVP 4 Proper

These items are **not** in this phase:

| Item | Reason for deferral |
|------|-------------------|
| Customer disputes workflow | Requires RSM-015/016 (A5) + invoice model (B3) + customer portal (B4) — build the pieces first, then the workflow |
| ERP integration | Requires settlement export API (B2) + invoice model (B3) to be stable |
| Payment services (PBS/Betalingsservice) | Requires invoice model (B3) + production infrastructure |
| Digital post (e-Boks) | Requires invoice PDF generation |
| Real customer portal authentication (NemID/MitID) | Security scope — separate work stream |
| Pilot customers (10-50) | Requires all of the above |
| Full migration | Requires successful pilot |
| Preprod validation | Requires production infrastructure |
| Security audit (ISAE 3402, GDPR) | Requires stable feature set |

---

## New Golden Master Tests

| Test | Scenario | Track |
|------|----------|-------|
| GM#11 | Erroneous move: move-in date corrected 3 days later, settlement recalculated | A3 |
| GM#12 | Concurrent correction during switch: only delta within supply period settled | A4 |

---

## New Database Migrations

| Migration | Track | Purpose |
|-----------|-------|---------|
| V021 | B1 | `portfolio.product` fixes: drop `binding_period_months`, add `description`, `green_energy`, `display_order`. New `portfolio.signup` table (dar_id, gsrn, customer_id, product_id, process_request_id, type, effective_date, status) |
| V022 | A1 | `datahub.aggregation_data` + `datahub.reconciliation_result` tables |
| V023 | B3 | `billing.invoice` + `billing.credit_note` tables |

---

## Success Criteria for This Phase

1. **Onboarding API live:** All three sales channels (web, app, customer service) can create customers through a single API, with full signup → activation flow working end-to-end
2. **MVP 3 complete:** All planned features implemented, all 12 golden master tests pass, all integration tests pass
3. **Settlement export API:** External systems can query settlement results via REST
4. **Invoice model:** Settlement runs produce invoices with numbers, due dates, and line items
5. **Customer portal data:** Consumption, invoices, and contract data queryable via API
6. **Monitoring:** Health checks and operational metrics operational
7. **Performance baseline:** Measured at 80K metering points, bottlenecks documented
8. **CI green:** All unit + integration tests pass on every push

---

## Risk Register (This Phase)

| Risk | Impact | Mitigation |
|------|--------|------------|
| DAR ID → GSRN lookup returns multiple GSRNs | Signup fails with 400 | Sales channel handles disambiguation on their side before calling the API |
| DAR ID lookup service unavailable | Signup fails entirely | Retry with backoff. Future: allow direct GSRN input as fallback |
| CPR/CVR mismatch discovered only after BRS-001 sent | Signup stuck in rejected state, customer confused | Surface rejection reason via status endpoint. Customer service can correct and re-submit |
| 15+ business day gap causes customer dropout | Customer forgets or switches to competitor | Clear status communication. Future: email/SMS at each state change |
| Effective date calculation wrong (holidays, weekends) | BRS-001 rejected for insufficient notice | Build robust business day calculator with Danish holiday calendar |
| RSM-015/016 CIM format unknown | Parser may need rework once real messages are seen | Build parser from documentation, plan for fixture updates |
| BRS-011 endpoint name/format unverified | Request may be rejected by real DataHub | Research Energinet documentation, build against best understanding |
| TimescaleDB performance at 80K scale | Settlement may be too slow | Profile early (B6), identify bottlenecks before building more features |
| Invoice numbering conflicts in distributed deployment | Duplicate invoice numbers | Use database sequence, not application-generated numbers |
| API authentication model changes | Breaking changes for ERP/sales integration | Keep auth simple (API key) in this phase, design for replacement |
