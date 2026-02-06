# RSM-012: How DataHub 3 Communicates Measure Data

## Overview

RSM-012 ("Fremsend måledata for et målepunkt" / NotifyValidatedMeasureData) is a **notification message** used to send validated time series data for individual metering points. It carries both initial readings and corrections.

- **Document type:** E66 (Validated metered data, time series)
- **Senders:** Network operator (MDR) or DataHub (DGL)
- **Receivers:** Electricity supplier (DDQ), network operator (DDM), system operator (EZ), Danish Energy Agency (STS)

## How Corrections Arrive

When a network operator submits corrected meter data to DataHub (via BRS-021), DataHub validates and forwards an RSM-012 message to the electricity supplier's **message queue**. The correction client polls this queue.

### There Is No Explicit Correction Flag

**Verified from RSM Guide (Dok. 15/00718-196), pages 84, 92, 98:**

The ebIX message format has a `Function` field with codes:
- **9** = Original
- **5** = Update (for korrektioner)

However, page 92 states: *"Der anvendes kun funktionskode original (9) ved afsendelse af tidsserier til og fra DataHub."* — **Only Function=9 (Original) is used when sending to/from DataHub.**

In the CIM format (DataHub 3), there is no Function field at all.

The `ProcessType` (BusinessReasonCode) has these values for RSM-012:
- **E23** = Periodic metering (periodisk opgørelse) — default for all non-flex metering points
- **E30** = Historical data (historiske data) — used for requested historical data via RSM-015
- **D42** = Periodical flex metering

**E23 is used for both initial data AND corrections** (page 98: "Alle øvrige målepunktstyper sendes med årsagskode E23"). E30 is for historical data exchanges, not specifically corrections.

**Conclusion:** The correction client must detect corrections by comparing incoming timeseries against what's already stored. If data already exists for that MeteringPointId + period, the delta is what needs to be settled.

## DataHub 3 B2B API

### Authentication (OAuth2 Client Credentials)

Source: CIM Webservice Interface (Dok. 22/03077-1)

- **Method:** OAuth 2.0 Client Credentials Grant
- **Token endpoint:** `POST https://login.microsoftonline.com/{tenant-id}/oauth2/v2.0/token`
- **Body:** `grant_type=client_credentials&client_id={id}&client_secret={secret}&scope={scope}/.default`
- **Token expires:** 1 hour (`expires_in: 3599`)
- **Client credentials:** Created in DataHub actor portal (B2B adgang tab), expire after 12 months

Known tenant IDs (from Confluence — verify with Energinet):
- Preprod: `20e7a6b4-86e0-4e7a-a34d-6dc5a75d1982`
- Prod: `4b8c3f88-6cca-480c-af02-b2d2f220913f`
- Aktørtest: `5a396c36-d56e-4db4-880b-c7894f2d9966` (from webservice interface doc)

### Base URL
- `https://{host}/v1.0/cim/`
- Known hosts: `api.itlev.datahub.dk` (test), `b2b.datahub3.dk` (prod), `preprod.b2b.datahub3.dk` (preprod)

### Endpoints

Source: CIM Webservice Interface (Dok. 22/03077-1), page 5

| Operation | Method | Path | Purpose |
|-----------|--------|------|---------|
| Submit | POST | `/v1.0/cim/notifyvalidatedmeasuredata` | Send RSM-012 to DataHub (network operator) |
| **Peek** | **GET** | **`/v1.0/cim/Timeseries`** | **Retrieve next message from timeseries queue** |
| **Dequeue** | **DELETE** | **`/v1.0/cim/dequeue/{id-of-message}`** | **Acknowledge and remove processed message** |

The **Timeseries** queue contains both RSM-012 (NotifyValidatedMeasureData) and RSM-014 (NotifyAggregatedMeasureData). The `MessageType` HTTP header identifies which document type was returned.

There is also a `GET /v1.0/cim/all` endpoint that peeks across all queues.

### Peek Response
- **200 OK** — message returned. Headers include `MessageId` and `MessageType`. Body contains the CIM document.
- **204 No Content** — queue is empty.
- `Accept: application/json` or `application/xml` to choose format.

### Dequeue Response
- **200 OK** — message acknowledged and removed.
- **400 Bad Request** with error body if message-id is unknown.

### Headers
```
Authorization: Bearer {token}
Accept: application/json
```

All responses include a `CorrelationId` header for troubleshooting with DataHub support.

## RSM-012 Message Structure (CIM)

Source: CIM EDI Guide (Dok. 15/00718-191), pages 66-70

### Header
| Field | CIM Path | Value/Format |
|-------|----------|--------------|
| Message ID | `MarketDocument/mRID` | UUID (An..36) |
| Document Type | `MarketDocument/type` | `E66` |
| Process Type | `MarketDocument/Process/ProcessType` | `E23`, `E30`, or `D42` |
| Sender GLN | `Sender_MarketParticipant/mRID` | GLN |
| Sender Role | `Sender_MarketParticipant/MarketRole/type` | `MDR` (network op) or `DGL` (DataHub) |
| Receiver GLN | `Receiver_MarketParticipant/mRID` | GLN |
| Receiver Role | `Receiver_MarketParticipant/MarketRole/type` | `DDQ` (supplier), `DDM`, `EZ`, `STS` |
| Created | `createdDateTime` | ISO-8601 UTC |

### Timeseries (Series, cardinality 1..*)
| Field | CIM Path | Format |
|-------|----------|--------|
| Transaction ID | `Series/mRID` | An..36 (mandatory) |
| Original Ref | `Series/OriginalTransactionReference_Series/mRID` | An..36 (only for RSM-015 responses) |
| MeteringPointId | `Series/MarketEvaluationPoint/mRID` | GSRN, 18 digits |
| Metering Point Type | `Series/MarketEvaluationPoint/type` | Code: E17 (consumption), E18 (production), E20 (exchange), D05-D20, D99 |
| Registration Date | `Series/Registration_DateAndOrTime/dateTime` | ISO-8601 UTC |
| Product | `Series/Product` | `8716867000030` (active energy) |
| Unit | `Series/Quantity_Measure_Unit/name` | `KWH`, `K3` (kVArh), `KWT`, etc. |
| Resolution | `Series/Period/resolution` | `PT15M`, `PT1H`, or `P1M` |
| Period Start | `Series/Period/timeInterval:start` | ISO-8601 UTC (YYYY-MM-DDThh:mmZ) |
| Period End | `Series/Period/timeInterval:end` | ISO-8601 UTC |
| Position | `Series/Period/Point/position` | 1-based integer, count matches resolution (e.g. 24 for PT1H per day) |
| Quantity | `Series/Period/Point/quantity` | Decimal, max 3 decimals for KWH |
| Quality | `Series/Period/Point/quality` | `A01` (adjusted), `A02` (missing), `A03` (estimated), `A06` (calculated) |

### Quality Codes (CIM vs ebIX)
| CIM Code | ebIX Code | Meaning |
|----------|-----------|---------|
| A01 | — | Adjusted (korrigeret) |
| A02 | — | Not available (manglende) |
| A03 | 56 | Estimated (estimeret) |
| A06 | D01 | Calculated (beregnet) |
| — | E01 | As read (målt) — not sent in CIM if measured |

If quantity is missing, the `quantity` element is omitted and `quality = A02`.

### Business Rules (from RSM Guide page 98)
- Resolution must be `PT15M`, `PT1H`, or `P1M`
- Positions must cover whole days (e.g. 24 positions for PT1H per day)
- Quantity max 3 decimals for KWH
- `OriginalTransactionReference` only used when responding to RSM-015 request
- Flex metering points use ProcessType D42, all others use E23

## Correction Flow (What the Client Must Do)

```
1. Authenticate → get Bearer token (refresh every <1h)
2. GET /v1.0/cim/Timeseries (Accept: application/json)
   → 204: queue empty, wait and retry
   → 200: read MessageId header and MessageType header
3. Check MessageType header = NotifyValidatedMeasureData (skip others)
4. Parse CIM JSON body:
   - Extract MeteringPointId, period (start/end), resolution
   - Extract Point[] array (position + quantity per interval)
5. Look up original consumption in our database for same MeteringPointId + period
   - If no original data exists → this is initial data, not a correction → dequeue and skip
   - If original data exists → calculate delta per interval
6. Calculate financial impact using stored rates and product plan
7. Generate invoice/credit note via ERP
8. DELETE /v1.0/cim/dequeue/{MessageId} → acknowledge
9. Repeat from step 2
```

## Message Queue Architecture

Source: CIM EDI Guide page 256

Each actor has a **single queue per GLN**. RSM-012 messages are placed in the actor's queue identified by their GLN number. If an actor has multiple systems, it's their responsibility to distribute messages internally.

RSM-012 can be delegated:
- E30 (Historical): delegatable to electricity supplier (EL) only
- E23 (Periodic): delegatable to both electricity supplier (EL) and network operator (NV)

## Open Questions for Energinet (INC0478625)
1. Can OAuth2 credentials be issued for an "udgået" (decommissioned) GLN?
2. Is full aktørtest required for a read-only peek/dequeue client?
3. Will the Timeseries peek endpoint remain stable after Phase 3?
4. What happens to the old GLN's message queue after Phase 3?

## Related Documentation

- [Edge Cases and Error Handling](datahub3-edge-cases.md#1-metering-data-corrections) — detection logic, correction formulas, system design implications

## Sources

### Official Energinet Documents (in repo `docs/`)
- `edi-transaktioner-for-det-danske-elmarked-rsm-guide.pdf` (Dok. 15/00718-196) — RSM Guide, ebIX format
- `edi-transaktioner-for-danske-elmarkede-cim-ver15.pdf` (Dok. 15/00718-191) — CIM EDI Guide
- `20220427-cim-xml-og-cim-json-webservice-interface.pdf` (Dok. 22/03077-1) — CIM Webservice Interface

### Online
- [RSM-012 Confluence](https://energinet.atlassian.net/wiki/spaces/DHDOCS/pages/1315995649)
- [CIM Interface Guide](https://energinet.atlassian.net/wiki/spaces/DHDOCS/pages/653983967)
- [RSM Endpoints](https://energinet.atlassian.net/wiki/spaces/DHDOCS/pages/654147782)
- [EDI RSM Guide](https://energinet.atlassian.net/wiki/spaces/DHDOCS/pages/939950083)
- [BRS Processes](https://energinet.atlassian.net/wiki/spaces/DHDOCS/pages/152961170)
- [DataHub 3 EDI Communication](https://energinet.atlassian.net/wiki/spaces/DHDOCS/pages/653787350)
