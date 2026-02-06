# DataHub 3: Business Process Reference for Electricity Suppliers (DDQ)

## 1. Introduction

### What Is DataHub 3?

DataHub is the central IT hub for the Danish electricity retail market, owned and operated by Energinet DataHub A/S. All market communication between electricity suppliers, network operators, and other market participants flows through DataHub.

DataHub 3 (also called "DataHub 3.0") is the current version, migrated from the legacy ebIX/EDIEL-based DataHub 2 to a CIM-based messaging model using JSON/XML over REST APIs.

### The DDQ Role

**DDQ** (Danish: "den daglige leverandør af el til kunden") is the **electricity supplier** (also called "balance supplier" / "elleverandør"). This is the market role that:

- Has a supply obligation to the end customer
- Receives validated and aggregated metering data from DataHub
- Initiates supplier switches and move-in/move-out processes
- Receives settlement and wholesale aggregation results
- Is responsible for customer master data submission

Other key market roles referenced in this document:

| Code | Role (EN) | Role (DA) |
|------|-----------|-----------|
| DDQ | Electricity supplier | Elleverandør |
| DDM | Grid access provider / Network operator | Netvirksomhed |
| DDZ | DataHub (metered data administrator) | DataHub |
| DGL | DataHub (as delegated sender) | DataHub |
| MDR | Metered data responsible | Måledataansvarlig |
| EZ | System operator (Energinet) | Systemansvarlig |
| STS | Danish Energy Agency | Energistyrelsen |
| DDK | Balance responsible party | Balanceansvarlig |

---

## 2. Technical Foundation

### 2.1 Authentication (OAuth2 Client Credentials)

Source: CIM Webservice Interface (Dok. 22/03077-1)

- **Method:** OAuth 2.0 Client Credentials Grant
- **Token endpoint:** `POST https://login.microsoftonline.com/{tenant-id}/oauth2/v2.0/token`
- **Body:** `grant_type=client_credentials&client_id={id}&client_secret={secret}&scope={scope}/.default`
- **Token lifetime:** ~1 hour (`expires_in: 3599`)
- **Credentials:** Created in DataHub actor portal → "B2B adgang" tab. Expire after 12 months.

**Known tenant IDs:**

| Environment | Tenant ID |
|-------------|-----------|
| Aktørtest | `5a396c36-d56e-4db4-880b-c7894f2d9966` |
| Preprod | `20e7a6b4-86e0-4e7a-a34d-6dc5a75d1982` |
| Prod | `4b8c3f88-6cca-480c-af02-b2d2f220913f` |

### 2.2 Base URLs

| Environment | Host |
|-------------|------|
| Aktørtest | `api.itlev.datahub.dk` |
| Preprod | `preprod.b2b.datahub3.dk` |
| Production | `b2b.datahub3.dk` |

All endpoints are prefixed: `https://{host}/v1.0/cim/`

### 2.3 B2B API Endpoints

Source: CIM Webservice Interface (Dok. 22/03077-1); GitHub Energinet-DataHub/ARCHIVED-geh-post-office

#### Peek Endpoints (GET — retrieve next message from queue)

| Endpoint | Queue Content |
|----------|---------------|
| `GET /v1.0/cim/Timeseries` | RSM-012 (NotifyValidatedMeasureData) + RSM-014 (NotifyAggregatedMeasureData) |
| `GET /v1.0/cim/Aggregations` | RSM-014 (NotifyAggregatedMeasureData) — ⚠ VERIFY: may overlap with Timeseries |
| `GET /v1.0/cim/MasterData` | RSM-007 (GenericNotification for master data changes), RSM-004 ⚠ VERIFY |
| `GET /v1.0/cim/Charges` | Charge/tariff price list notifications ⚠ VERIFY exact RSM |
| `GET /v1.0/cim/all` | Peeks across all queues (returns first available message from any queue) |

#### Dequeue Endpoint (DELETE — acknowledge and remove)

| Endpoint | Purpose |
|----------|---------|
| `DELETE /v1.0/cim/dequeue/{message-id}` | Acknowledge processed message; removes from queue |

#### Submit Endpoints (POST — send messages to DataHub)

| Endpoint | Purpose |
|----------|---------|
| `POST /v1.0/cim/requestchangeofsupplier` | BRS-001 / BRS-043: Supplier switch |
| `POST /v1.0/cim/requestendofsupply` | BRS-002 / BRS-005: End of supply |
| `POST /v1.0/cim/requestcancelchangeofsupplier` | BRS-003 / BRS-042: Cancel incorrect switch ⚠ VERIFY path |
| `POST /v1.0/cim/requestcustomerdata` | BRS-015: Submit customer master data ⚠ VERIFY path |
| `POST /v1.0/cim/requestvalidatedmeasuredata` | RSM-015: Request historical metering data |
| `POST /v1.0/cim/requestaggregatedmeasuredata` | RSM-016: Request aggregated data |
| `POST /v1.0/cim/notifyvalidatedmeasuredata` | RSM-012: Submit validated data (network operator only) |

⚠ VERIFY: Exact endpoint paths may differ per DataHub version. Cross-reference with the latest CIM Webservice Interface document from Energinet.

#### HTTP Headers

```
Authorization: Bearer {token}
Accept: application/json       (or application/xml)
Content-Type: application/json (for POST requests)
```

Response headers include:
- `MessageId` — unique ID for peek/dequeue
- `MessageType` — identifies the RSM/document type
- `CorrelationId` — for DataHub support troubleshooting

#### Response Codes

| Code | Peek | Dequeue |
|------|------|---------|
| 200 | Message returned | Message acknowledged |
| 204 | Queue empty | — |
| 400 | — | Unknown message-id |
| 404 | — | Bundle not found |

### 2.4 Queue Architecture

Source: CIM EDI Guide (Dok. 15/00718-191), page 256

- Each actor has **one set of queues per GLN**
- Messages are placed in the appropriate queue based on document type
- If an actor has multiple systems behind one GLN, internal distribution is the actor's responsibility
- Messages must be dequeued (acknowledged) before the next message becomes visible — FIFO, one-at-a-time
- A message that is peeked but not dequeued will remain at the head of the queue

---

## 3. Measure Data Processes

These processes handle the flow of validated and aggregated metering data between DataHub and market participants.

### 3.1 RSM-012: NotifyValidatedMeasureData

**Full reference:** [docs/rsm-012-datahub3-measure-data.md](rsm-012-datahub3-measure-data.md)

- **Document type:** E66
- **Direction:** DataHub/MDR → DDQ (supplier receives)
- **Queue:** `Timeseries`
- **Trigger:** BRS-021 (network operator submits validated metered data)
- **ProcessType codes:** E23 (periodic), D42 (flex), E30 (historical response)
- **Content:** Time series with per-interval quantity + quality for a single metering point
- **Resolution:** PT15M, PT1H, or P1M

**Key facts for DDQ:**
- ProcessType E23 is used for all periodic data (both initial readings and subsequent updates)
- No explicit flag distinguishes first-time data from updated data
- Delegatable: E23 to both DDQ and DDM; E30 to DDQ only

### 3.2 RSM-014: NotifyAggregatedMeasureData

- **Document type:** E31
- **Direction:** DataHub → DDQ
- **Queue:** `Timeseries` (and possibly `Aggregations`)
- **Trigger:** BRS-027 (wholesale aggregation completed)
- **ProcessType codes:** ⚠ VERIFY — expected D03 (preliminary aggregation), D04 (corrected aggregation), D05 (final aggregation)
- **Content:** Aggregated time series per grid area / balance responsible / supplier combination

**Key facts for DDQ:**
- Contains aggregated consumption per grid area — used for wholesale settlement
- Can also be a response to RSM-016 (request for aggregated data)
- When responding to RSM-016, includes `OriginalTransactionReference`

### 3.3 RSM-015: RequestValidatedMeasureData

- **Document type:** E73 ⚠ VERIFY
- **Direction:** DDQ → DataHub (supplier sends request)
- **Submit endpoint:** `POST /v1.0/cim/requestvalidatedmeasuredata` ⚠ VERIFY
- **Response:** RSM-012 message with ProcessType E30 (historical data) delivered to Timeseries queue

**Key facts for DDQ:**
- Used to request historical validated metering data for a specific metering point + period
- Response comes asynchronously via the Timeseries queue
- The response RSM-012 will contain `OriginalTransactionReference` linking back to the request

### 3.4 RSM-016: RequestAggregatedMeasureData

- **Document type:** E74 ⚠ VERIFY
- **Direction:** DDQ → DataHub (supplier sends request)
- **Submit endpoint:** `POST /v1.0/cim/requestaggregatedmeasuredata` ⚠ VERIFY
- **Response:** RSM-014 message delivered to Timeseries/Aggregations queue

**Key facts for DDQ:**
- Used to request aggregated metering data for a grid area + period
- Response comes asynchronously via queue
- Useful for verifying settlement data or backfilling missing aggregations

### 3.5 RSM-017: NotifyWholesaleServices ⚠ VERIFY

- **Document type:** ⚠ VERIFY
- **Direction:** DataHub → DDQ
- **Queue:** ⚠ VERIFY (likely `Aggregations` or a dedicated wholesale queue)
- **Trigger:** BRS-027 / BRS-028 / BRS-029 / BRS-030 wholesale calculation results

**Key facts for DDQ:**
- Contains calculated wholesale settlement amounts (energy, tariffs, subscriptions, fees)
- Includes monetary amounts, not just kWh volumes
- ⚠ VERIFY: RSM-017 may be the name used for the wholesale results notification; confirm with latest Energinet documentation

### 3.6 RSM-019: RejectRequestValidatedMeasureData ⚠ VERIFY

- **Direction:** DataHub → DDQ
- **Queue:** Timeseries ⚠ VERIFY
- **Trigger:** Rejection of an RSM-015 or RSM-016 request

**Key facts for DDQ:**
- Returned when a data request fails validation (e.g., metering point not found, period out of range, unauthorized)
- Contains rejection reason codes
- ⚠ VERIFY: May be called RSM-019 or may use RSM-009 (generic acknowledgement) with rejection codes

---

## 4. Supplier Switching & Lifecycle Processes

These processes govern how supply obligations are created, transferred, and terminated.

### 4.1 BRS-001: Change of Supplier (Leverandørskifte)

- **RSM message:** RSM-001 (RequestChangeOfSupplier)
- **Initiator:** New DDQ (the gaining supplier)
- **Submit endpoint:** `POST /v1.0/cim/requestchangeofsupplier` ⚠ VERIFY
- **Effective:** Start of a future day (midnight)
- **Timeline:** Minimum 15 business days notice ⚠ VERIFY — may be reduced for BRS-043

**Flow:**
1. New DDQ sends RSM-001 to DataHub with metering point ID + requested effective date
2. DataHub validates (metering point exists, no conflicting processes, customer data matches)
3. DataHub sends confirmation to new DDQ and notification to old DDQ
4. On effective date, supply transfers in DataHub
5. DataHub sends RSM-012 (metering data) and RSM-007 (master data) to new DDQ

**DDQ receives:**
- Confirmation/rejection of the switch request
- Master data for the metering point (via MasterData queue)
- Historical metering data (if configured)

### 4.2 BRS-002: End of Supply (Leveranceophør)

- **RSM message:** RSM-005 (RequestEndOfSupply)
- **Initiator:** Current DDQ
- **Submit endpoint:** `POST /v1.0/cim/requestendofsupply` ⚠ VERIFY

**Key facts:**
- Used when a customer terminates their contract, doesn't pay, or moves out without a new supplier
- After end of supply, the metering point enters "supplier of last resort" ⚠ VERIFY
- The current DDQ can cancel end of supply (e.g., if customer pays) before effective date

### 4.3 BRS-003: Cancel Change of Supplier (Annuller leverandørskifte)

- **RSM message:** RSM-003 (RequestCancelChangeOfSupplier) ⚠ VERIFY
- **Initiator:** New DDQ (the gaining supplier) or DataHub
- **Purpose:** Cancel an already-requested BRS-001 supplier switch before it takes effect

**Key facts:**
- Must be sent before the effective date of the original switch
- DataHub notifies both old and new DDQ of the cancellation

### 4.4 BRS-005: End of Supply — Forced (Tvunget leveranceophør) ⚠ VERIFY

- **Initiator:** DataHub or network operator
- **Purpose:** Force end of supply when supplier loses authorization or other regulatory reasons

⚠ VERIFY: BRS-005 may cover a different scenario. Cross-reference with latest BRS document.

### 4.5 BRS-042: Incorrect Change of Supplier — Rollback (Fejlagtigt leverandørskifte)

- **RSM message:** RSM-003 ⚠ VERIFY
- **Initiator:** The DDQ who identifies the error
- **Purpose:** Reverse a supplier switch that was performed in error (after effective date)

**Key facts:**
- Can be initiated after the switch has already taken effect (retroactive)
- DataHub validates the rollback request and recalculates supply periods
- ⚠ VERIFY: Time limits apply — typically within 20 business days of effective date

### 4.6 BRS-043: Change of Supplier at Short Notice (Leverandørskifte med kort varsel)

- **RSM message:** RSM-001 (same as BRS-001)
- **Initiator:** New DDQ
- **Timeline:** 1 business day notice ⚠ VERIFY
- **Conditions:** Only available in specific circumstances (e.g., customer at risk of disconnection)

**Key facts:**
- Uses the same RSM-001 message as BRS-001 but with a shorter notice period
- Cannot be used if end of supply has been reported and the time limit has been exceeded
- ⚠ VERIFY: The business reason code may differ from BRS-001

### 4.7 BRS-044: Cancel End of Supply (Annuller leveranceophør) ⚠ VERIFY

- **RSM message:** ⚠ VERIFY
- **Initiator:** Current DDQ
- **Purpose:** Cancel a previously submitted BRS-002 end of supply before effective date

**Key facts:**
- Example scenario: customer makes payment after end of supply was reported
- Must be submitted before the end of supply effective date

---

## 5. Customer & Master Data Processes

These processes handle metering point master data and customer information.

### 5.1 BRS-006: Change of Balance Responsible (Skift af balanceansvarlig) ⚠ VERIFY

- **Initiator:** DDK (balance responsible party) or DDQ
- **Purpose:** Change which balance responsible party is associated with a supplier's metering points

**DDQ involvement:**
- Receives notification of balance responsible changes affecting their metering points
- ⚠ VERIFY: DDQ may need to acknowledge or is only notified

### 5.2 BRS-009: Move-in (Tilflytning)

- **RSM message:** RSM-001 ⚠ VERIFY (or a move-specific variant)
- **Initiator:** DDQ
- **Purpose:** Register a new customer at a metering point

**Flow:**
1. DDQ sends move-in request with metering point ID + effective date + customer CPR/CVR
2. DataHub validates (metering point exists, no active supply, customer data)
3. DataHub confirms move-in and activates supply
4. DDQ receives master data and metering data via queues

**Key facts:**
- Supply only commences after DataHub and DDQ have received master data message with `connectionStatus = connected` and an effective supply start date
- ⚠ VERIFY: Move-in and supplier switch may share the same RSM-001 with different business reason codes

### 5.3 BRS-010: Move-out (Fraflytning)

- **Initiator:** DDQ or DDM (network operator)
- **Purpose:** Register customer departure from a metering point

**Key facts:**
- Current DDQ is notified and supply obligation ends
- Network operator (DDM) may also initiate if they learn of the move-out
- Final metering data is sent via RSM-012 for the supply period

### 5.4 BRS-011: Incorrect Move (Fejlagtig flytning) ⚠ VERIFY

- **Initiator:** DDQ or DDM
- **Purpose:** Correct a move-in or move-out that was submitted in error

**Key facts:**
- Allows retroactive correction of move dates
- ⚠ VERIFY: Time limits and specific RSM messages used

### 5.5 BRS-015: Submission of Customer Master Data (Fremsendelse af kundestamdata)

- **RSM message:** ⚠ VERIFY (likely uses a generic notification or dedicated customer data RSM)
- **Initiator:** DDQ
- **Submit endpoint:** ⚠ VERIFY

**Key facts:**
- DDQ submits CPR/CVR and customer name for their metering points
- Required when starting supply (BRS-001/BRS-009) or when customer data changes
- DataHub validates CPR/CVR against the CPR register
- ⚠ VERIFY: May be a sub-step of BRS-001/BRS-009 rather than a standalone process

---

## 6. Master Data Notifications (DataHub → DDQ)

### 6.1 RSM-004: GenericNotification ⚠ VERIFY

- **Direction:** DataHub → DDQ
- **Queue:** `MasterData`
- **Purpose:** Notify supplier of master data changes to metering points in their portfolio

**Content includes changes to:**
- Metering point type
- Settlement method (flex/profiled)
- Grid area
- Connection status (connected/disconnected)
- Network operator assignment

### 6.2 RSM-007: InformMasterData ⚠ VERIFY

- **Direction:** DataHub → DDQ
- **Queue:** `MasterData`
- **Purpose:** Full master data snapshot sent after supplier switch or move-in

⚠ VERIFY: RSM-004 and RSM-007 naming. In CIM DataHub 3, these may use different names or be consolidated.

### 6.3 RSM-009: Acknowledgement / Rejection

- **Direction:** DataHub → DDQ
- **Queue:** Varies (delivered to the queue corresponding to the original request)
- **Purpose:** Confirm or reject a submitted request (BRS-001, BRS-002, RSM-015, etc.)

**Content:**
- Reference to original transaction
- Acceptance or rejection status
- Rejection reason code(s) if rejected

---

## 7. Metered Data Submission & Consumption Statements

### 7.1 BRS-020: Consumption Statement for Profile-Settled Metering Point (Forbrugsopgørelse)

- **Initiator:** DataHub (automatic)
- **Purpose:** Send periodic consumption statement for profile-settled (non-flex) metering points

**DDQ involvement:**
- DDQ receives the consumption statement via queue
- Contains the settled consumption for a billing period
- ⚠ VERIFY: May be delivered via RSM-012 or a dedicated message type

### 7.2 BRS-021: Submission of Metered Data (Indsendelse af måledata)

- **Initiator:** DDM / MDR (network operator)
- **Purpose:** Network operator submits validated meter readings to DataHub

**DDQ involvement:**
- DDQ does not initiate this process
- DDQ receives the result as an RSM-012 message via the Timeseries queue

**Flow:**
```
Network Operator (MDR)
  → submits validated metered data to DataHub (BRS-021)
    → DataHub validates
      → DataHub sends RSM-012 to DDQ via Timeseries queue
```

---

## 8. Settlement & Aggregation (Wholesale Model)

These processes handle wholesale settlement calculations and provide aggregated financial data to market participants.

### 8.1 BRS-027: Aggregation of Wholesale Services (Engrosopgørelse)

- **Initiator:** DataHub (automatic, scheduled)
- **Purpose:** Calculate aggregated wholesale amounts per grid area, supplier, and balance responsible

**DDQ receives:**
- RSM-014 (aggregated metering data) via Timeseries/Aggregations queue
- RSM-017 (wholesale service results) ⚠ VERIFY via dedicated queue

**Aggregation types:**
- Preliminary (D03) — available shortly after metering period
- Corrected (D04) — after corrections are submitted
- Final (D05) — after the correction window closes ⚠ VERIFY codes

### 8.2 BRS-028: Request Aggregated Subscriptions or Fees (Forespørg abonnementer/gebyrer) ⚠ VERIFY

- **Initiator:** DDQ
- **Purpose:** Request aggregated subscription/fee data from DataHub

**Key facts:**
- On-demand request for specific grid area + period
- ⚠ VERIFY: Whether this uses RSM-016 or a separate request message

### 8.3 BRS-029: Request Aggregated Tariffs (Forespørg tariffer) ⚠ VERIFY

- **Initiator:** DDQ
- **Purpose:** Request aggregated tariff data from DataHub

**Key facts:**
- On-demand request similar to BRS-028 but for time-based tariffs
- Response includes tariff amounts per interval
- ⚠ VERIFY: Exact RSM message and endpoint

### 8.4 BRS-030: Request Settlement Basis (Forespørg afregningsgrundlag) ⚠ VERIFY

- **Initiator:** DDQ
- **Purpose:** Request the full settlement basis for a grid area + period

**Key facts:**
- Returns the complete dataset used for wholesale settlement
- Useful for reconciliation and audit
- ⚠ VERIFY: Exact RSM message and endpoint

---

## 9. Queue-to-Message Mapping Summary

| Queue Endpoint | RSM Messages | Document Types | DDQ Direction |
|----------------|-------------|----------------|---------------|
| `GET /v1.0/cim/Timeseries` | RSM-012 (NotifyValidatedMeasureData) | E66 | Receive |
| `GET /v1.0/cim/Timeseries` | RSM-014 (NotifyAggregatedMeasureData) | E31 | Receive |
| `GET /v1.0/cim/Aggregations` | RSM-014 (aggregated only) ⚠ VERIFY | E31 | Receive |
| `GET /v1.0/cim/MasterData` | RSM-004/007 (master data notifications) ⚠ VERIFY | ⚠ VERIFY | Receive |
| `GET /v1.0/cim/Charges` | Charge/price list notifications ⚠ VERIFY | ⚠ VERIFY | Receive |
| `GET /v1.0/cim/all` | All of the above | Mixed | Receive |
| `DELETE /v1.0/cim/dequeue/{id}` | — | — | Acknowledge |

### Submit Endpoints Summary (DDQ → DataHub)

| Endpoint (POST) | BRS/RSM | Purpose |
|-----------------|---------|---------|
| `requestchangeofsupplier` ⚠ VERIFY | BRS-001/043, RSM-001 | Supplier switch |
| `requestendofsupply` ⚠ VERIFY | BRS-002/005, RSM-005 | End of supply |
| `requestcancelchangeofsupplier` ⚠ VERIFY | BRS-003/042 | Cancel/rollback switch |
| `requestvalidatedmeasuredata` ⚠ VERIFY | RSM-015 | Request historical data |
| `requestaggregatedmeasuredata` ⚠ VERIFY | RSM-016 | Request aggregated data |
| `requestcustomerdata` ⚠ VERIFY | BRS-015 | Submit customer data |

---

## 10. CIM Document Types Reference

| Code | Document Type | Used In |
|------|---------------|---------|
| E66 | Validated metered data, time series | RSM-012 |
| E31 | Aggregated metered data | RSM-014 |
| E73 | Request validated metered data ⚠ VERIFY | RSM-015 |
| E74 | Request aggregated metered data ⚠ VERIFY | RSM-016 |
| E44 | Notification to supplier ⚠ VERIFY | Master data notifications |
| E65 | Settlement data ⚠ VERIFY | Wholesale results |

### Process Type Codes (BusinessReasonCode)

| Code | Meaning | Used In |
|------|---------|---------|
| E23 | Periodic metering | RSM-012 |
| E30 | Historical data | RSM-012 (response to RSM-015 request) |
| D42 | Periodical flex metering | RSM-012 (flex metering points) |
| D03 | Preliminary aggregation ⚠ VERIFY | RSM-014 |
| D04 | Corrected aggregation ⚠ VERIFY | RSM-014 |
| D05 | Final aggregation ⚠ VERIFY | RSM-014 |

### Quality Codes

| CIM Code | Meaning |
|----------|---------|
| A01 | Adjusted (korrigeret) |
| A02 | Not available (manglende) |
| A03 | Estimated (estimeret) |
| A06 | Calculated (beregnet) |

---

## 11. Market Rule Compliance Notes

### Time Limits

| Process | Deadline | Source |
|---------|----------|--------|
| BRS-001 supplier switch notice | 15 business days ⚠ VERIFY | Forskrift H1 |
| BRS-043 short-notice switch | 1 business day ⚠ VERIFY | Forskrift H1 |
| BRS-042 incorrect switch rollback | 20 business days ⚠ VERIFY | Forskrift H1 |
| BRS-002 end of supply notice | ⚠ VERIFY | Forskrift H1 |
| Correction window (BRS-021) | 3 years ⚠ VERIFY | Market regulation |

### Delegation Rules

Source: CIM EDI Guide page 256

- RSM-012 with E23 (periodic): delegatable to DDQ and DDM
- RSM-012 with E30 (historical): delegatable to DDQ only
- Other delegations: ⚠ VERIFY per RSM type

### Validation Principles

- DataHub validates all incoming messages against CIM schema before processing
- Business validation (e.g., metering point exists, actor is authorized) happens after schema validation
- Rejection is communicated via acknowledgement message (RSM-009 or inline rejection) ⚠ VERIFY

### Settlement Method Impact

- **Flex-settled** metering points (D42): Settled on actual hourly metering data
- **Profile-settled** metering points (E23): Settled on estimated profiles, with later correction to actuals
- The settlement method determines which ProcessType code appears in RSM-012

---

## 12. Sources

### Official Energinet Documents (in repo `docs/`)

- `edi-transaktioner-for-det-danske-elmarked-rsm-guide.pdf` (Dok. 15/00718-196) — RSM Guide, ebIX format
- `edi-transaktioner-for-danske-elmarkede-cim-ver15.pdf` (Dok. 15/00718-191) — CIM EDI Guide
- `20220427-cim-xml-og-cim-json-webservice-interface.pdf` (Dok. 22/03077-1) — CIM Webservice Interface
- `rsm-012-datahub3-measure-data.md` — Detailed RSM-012 reference (in this repo)

### Energinet Online Resources

- [BRS Business Processes](https://energinet.atlassian.net/wiki/spaces/DHDOCS/pages/152961170) — Process overview (Confluence, requires login)
- [RSM Endpoints](https://energinet.atlassian.net/wiki/spaces/DHDOCS/pages/654147782) — API endpoint documentation
- [CIM Interface Guide](https://energinet.atlassian.net/wiki/spaces/DHDOCS/pages/653983967) — CIM webservice details
- [DataHub EDI Communication](https://energinet.atlassian.net/wiki/spaces/DHDOCS/pages/653787350) — General EDI overview
- [RSM-014 Confluence](https://energinet.atlassian.net/wiki/spaces/DHDOCS/pages/939950134) — Aggregated measure data
- [BRS Wholesale Model (English PDF)](https://en.energinet.dk/media/wcpnvzrp/enbrswholesalemodelv37english.pdf) — Full BRS specification
- [BRS Forretningsprocesser (Danish PDF)](https://energinet.dk/media/2nqdysv3/brs-forretningsprocesser-for-det-danske-elmarked.pdf) — Danish BRS specification
- [CIM EDI Guide (latest)](https://energinet.dk/media/xk2p3ngh/edi-transaktioner-for-danske-elmarkede-cim-ver15.pdf) — Latest CIM transaction guide
- [Forskrift H1 Vejledning](https://energinet.dk/media/ptpjjsaa/forskrift-h1-vejledning.pdf) — Market regulation guidance

### Related Documentation

- [Edge Cases and Error Handling](datahub3-edge-cases.md) — Edge cases for BRS-011, BRS-042, and other error scenarios

### GitHub

- [Energinet-DataHub/opengeh-edi](https://github.com/Energinet-DataHub/opengeh-edi) — Open-source EDI implementation
- [Energinet-DataHub/ARCHIVED-geh-post-office](https://github.com/Energinet-DataHub/ARCHIVED-geh-post-office) — Message hub (archived, but documents queue architecture)
