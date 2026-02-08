# MVP 2: Full Customer Lifecycle — What Was Built

Handle a customer's full lifecycle — offboarding, cancellations, rejections, aconto, and final settlement. Everything that happens after the sunshine path. A customer can go through every state in the system.

**Delivered:** Run the full lifecycle simulator scenario — a customer goes through onboarding, operation, grid area change, and offboarding. Aconto customers get quarterly combined invoices. Final settlement produces the correct closing invoice. All golden master tests pass.

**Builds on MVP 1:** MVP 1 delivered the sunshine path (signup → switch → data → settlement). MVP 2 adds everything else in the customer lifecycle.

---

## Offboarding (BRS-002, BRS-010)

Three paths for a customer leaving:

| Scenario | BRS | Who initiates | What happens |
|----------|-----|--------------|-------------|
| Customer switches to another supplier | Incoming BRS-001 | Other supplier | We receive notification, run final settlement |
| Customer terminates contract | BRS-002 | We send | End of supply, metering point transferred to default supplier |
| Customer moves out | BRS-010 | We or grid company | Move-out with final metering data |

### Implementation

**State machine extension** (V011 migration):
- Added `offboarding` and `final_settled` statuses to process state machine
- Transition: `completed → offboarding → final_settled`
- `ProcessStateMachine.MarkOffboardingAsync()` — triggers final settlement flow
- `ProcessStateMachine.MarkFinalSettledAsync()` — terminal state after final invoice

**BRS request builders** (`BrsRequestBuilder.cs`):
- `BuildBrs002()` — End of Supply (process type E03)
- `BuildBrs010()` — Move-out (process type E01)

**Offboarding flow:**

```
1. Trigger: customer leaving (switch, termination, move-out, non-payment)
2. Mark process: completed → offboarding
3. Mark metering point inactive, record supply period end date
4. Receive final RSM-012 from DataHub (up to end date)
5. Run final settlement for partial period
6. Generate final invoice (within 4 weeks per elleveringsbekendtgørelsen §17)
7. Mark process: offboarding → final_settled
```

---

## Cancellation (BRS-003)

Customer changes their mind before the effective date.

### Implementation

**BRS-003 builder** (`BrsRequestBuilder.cs`):
- `BuildBrs003()` — Cancel Change of Supplier (process type E65)
- Same GLN structure as BRS-001, with cancellation indicators

**State machine transitions:**
- `pending → cancelled` — not yet sent to DataHub, cancel immediately
- `effectuation_pending → cancelled` — sent and acknowledged, send BRS-003 to DataHub

**Cancel after activation:** Not allowed. Returns error — use BRS-042 (erroneous switch) instead.

**Additional builders for related cancellations:**
- `BuildBrs044()` — Cancel end-of-supply/termination (process type E03)

---

## Rejection Handling (RSM-009)

DataHub rejects a BRS request — CPR mismatch, conflicting process, invalid GSRN.

### Implementation

**State machine:**
- `ProcessStateMachine.MarkRejectedAsync()` — terminal state with rejection reason
- Transition: `sent_to_datahub → rejected`
- Rejection reason stored in process event payload

**Parser integration:**
- CIM parser handles E59 message type (rejection)
- Extracts error code (e.g., E16 = conflicting process)

**Simulator scenario (`rejection`):**
1. System sends BRS-001 with incorrect CPR
2. Simulator returns RSM-009 rejected (error code E16)
3. System transitions process to `rejected` with reason

---

## RSM-004 Master Data Changes

Grid area changes and metering point updates during active supply.

### Implementation

**CIM parser** (`CimJsonParser.cs`):
- `ParseRsm004()` — parses E44 message type (grid area change)
- Extracts new grid area code and settlement method

**Queue poller integration:**
- `QueuePollerService.ProcessMasterDataAsync()` — handles RSM-004 on MasterData queue
- Updates metering point's grid area code in portfolio
- Triggers tariff reassignment for the new grid area

**Simulator scenario (`full_lifecycle`):**
- Enqueues RSM-004 changing grid area from 344 to 391
- Tests that tariff lookup uses the new grid area for subsequent settlement

---

## Aconto Settlement

Quarterly prepayment model — customer pays a fixed estimate, reconciled against actual consumption each quarter.

### Architecture decision

Even though the customer pays aconto, the **settlement engine always runs exactly as normal** behind the scenes. The aconto amount is purely a payment/cash flow parameter, not a settlement parameter.

### Implementation

**`AcontoEstimator`** — calculates quarterly prepayment amount:
- New customer: 4,000 kWh/year (house) or 2,500 kWh/year (apartment) × expected average price ÷ 4
- Existing customer: last 12 months actual consumption × current price levels ÷ 4

**`AcontoSettlementService.CalculateQuarterlyInvoice()`**:

```
Input:
  - Actual settlement result for the quarter (from settlement engine)
  - Aconto payments made during the quarter

Calculation:
  difference = actual_total - paid_aconto
    > 0 → customer underpaid (owes more)
    < 0 → customer overpaid (credit)

  new_aconto = recalculated estimate for next quarter

Output: Combined quarterly invoice
  Part 1: Settlement (actual cost ± difference from aconto)
  Part 2: New aconto for upcoming quarter
  = One net amount
```

**Database** (V012 migration):
- `billing.aconto_payment` table — tracks monthly aconto amounts per customer
- V020 migration adds `aconto` to `settlement_line` charge type enum

### Golden Master Test #3 — Aconto quarterly

| Component | Amount |
|-----------|--------|
| Actual settlement (January) | 793.14 DKK |
| Aconto paid | 700.00 DKK |
| Difference (underpaid) | 93.14 DKK |
| New quarterly aconto estimate | 800.00 DKK |
| **Combined invoice total** | **893.14 DKK** |

---

## Final Settlement

Partial-period settlement when a customer leaves mid-billing-period.

### Implementation

**`FinalSettlementService.CalculateFinal()`**:

```
Input:
  - Settlement result for partial period (start of quarter → departure date)
  - Aconto payments (if aconto customer)

If aconto customer:
  final_amount = actual_settlement - aconto_paid
    > 0 → customer owes remaining balance
    < 0 → customer gets refund

If non-aconto customer:
  final_amount = actual_settlement total

Output: Final invoice (no new aconto — customer is leaving)
```

**Regulatory requirement:** Final invoice must be issued within **4 weeks** of customer departure (elleveringsbekendtgørelsen §17).

### Golden Master Test #4 — Final settlement at offboarding

| Component | Amount |
|-----------|--------|
| Actual settlement (Jan 16 – Feb 1, partial period) | 409.36 DKK |
| Aconto paid | 300.00 DKK |
| **Amount due** | **109.36 DKK** |

---

## HTTP Simulator (Docker-based)

MVP 1 used `FakeDataHubClient` (in-memory). MVP 2 added a standalone HTTP simulator that runs as a Docker container and mimics the real DataHub B2B API.

### Implementation

`Simulator/Program.cs` — ASP.NET 9 Minimal API:

| Endpoint | Behavior |
|----------|----------|
| `POST /oauth2/v2.0/token` | Returns fake bearer token (`sim-token-{guid}`) |
| `GET /v1.0/cim/{queue}` | Peek next message from queue (204 if empty) |
| `DELETE /v1.0/cim/dequeue/{messageId}` | Remove message from queue |
| `POST /v1.0/cim/requestchangeofsupplier` | Accept/reject BRS-001 (checks GSRN activation state) |
| `POST /v1.0/cim/requestendofsupply` | Accept BRS-002 |
| `POST /v1.0/cim/requestcancelchangeofsupplier` | Accept BRS-003 |

**Admin endpoints** for test control:
- `POST /admin/scenario/{name}` — load predefined scenario
- `POST /admin/enqueue` — manually enqueue a message
- `POST /admin/activate/{gsrn}` — mark GSRN as active
- `POST /admin/reset` — clear all state
- `GET /admin/requests` — view outbound request audit

**State management** (`SimulatorState.cs`):
- `ConcurrentDictionary<string, ConcurrentQueue<QueueMessage>>` for queues
- `ConcurrentBag<OutboundRequest>` for audit trail
- Thread-safe GSRN activation tracking

**Scenario engine** (`ScenarioLoader.cs`):
- 6 predefined scenarios with realistic CIM JSON payloads
- Each scenario enqueues the right sequence of messages for the flow

---

## Full Lifecycle Test

`FullLifecycleTests.cs` — end-to-end flow verification:

```
Step 1:  Create customer + product + metering point + contract
Step 2:  Submit BRS-001 (supplier switch)
Step 3:  Simulate DataHub acknowledgement → auto-transition to effectuation_pending
Step 4:  Effective date reached → mark completed
Step 5:  RSM-012 received → 744 hourly readings stored
Step 6:  Run settlement for January → 793.14 DKK (matches GM#1)
Step 7:  Mark offboarding (customer leaving)
Step 8:  Receive final RSM-012 (Feb 1-16, partial period)
Step 9:  Run final settlement → matches GM#4
Step 10: Mark final_settled (terminal state)
Step 11: Assert: all 9 state transitions logged in process_event table
```

---

## Additional BRS Support

| BRS | Builder method | Purpose |
|-----|---------------|---------|
| BRS-001 | `BuildBrs001()` | Supplier switch (MVP 1) |
| BRS-002 | `BuildBrs002()` | End of supply |
| BRS-003 | `BuildBrs003()` | Cancel switch |
| BRS-009 | `BuildBrs009()` | Move-in |
| BRS-010 | `BuildBrs010()` | Move-out |
| BRS-043 | `BuildBrs043()` | Short notice switch |
| BRS-044 | `BuildBrs044()` | Cancel termination |

All request builders generate CIM JSON with correct process type codes, GLN identifiers, and GSRN references.

---

## What MVP 2 Delivered

- Full customer lifecycle: onboarding → operation → offboarding → final settlement
- All happy-path BRS processes (001, 002, 003, 009, 010, 043, 044) can be submitted and responses handled
- State machine handles all transitions including rejection and cancellation
- Aconto quarterly cycle (estimation → payment → settlement → combined invoice)
- Final settlement with partial-period pro-rata and aconto reconciliation
- RSM-004 master data changes (grid area reassignment)
- Standalone HTTP simulator (Docker) with 6 predefined scenarios
- Full lifecycle integration test (onboarding through final settlement)
- Golden master tests #3 (aconto) and #4 (final settlement) pass
