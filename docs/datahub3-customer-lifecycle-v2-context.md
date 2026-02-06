# Context for continuing datahub3-customer-lifecycle.md (v2)

## What was done in v1

Enhanced the customer lifecycle document with:

1. **Detailed Tidslinjeoversigt** — vertical flow diagram showing for each phase:
   - Trigger (what kicks off the phase)
   - DataHub processes with direction arrows (`→` we send, `←` we receive)
   - Fakturering actions (billing consequences)

2. **Proces-til-fase-mapping table** — 4 columns: BRS/RSM, Fase, Rolle, Faktureringskonsekvens

3. **Fakturaberegning section** — how to build a correct invoice:
   - Energi: Nordpool spotpris + Verdo-margin = `CalculatedPrice`
   - Nettariffer: time-differentiated grid tariffs from netvirksomhed
   - Produktmargin: per-kWh product plan charges
   - Faste gebyrer: subscriptions (net + Verdo)
   - Afgifter + moms (25%)
   - Verification checklist

## Key domain knowledge

### Invoice calculation (from Xellent codebase)
- `PowerExchangePrice` = raw Nordpool spot price (DKK/kWh)
- `CalculatedPrice` = spot + Verdo-margin (pre-calculated in billing history)
- `TimeValue` = kWh consumed in that hour
- Tariffs: `PriceElementRates` table (Price, Price2..Price24 for hours 1-24)
- Product margin: `ExuRateTable` based on product type
- `ChargeTypeCode = 3` identifies tariffs
- Hour indexing: DB uses 1-24, C# DateTime.Hour is 0-23

### Correction formulas (from CorrectionService.cs)
- Electricity: `deltaKwh × calculatedPrice`
- Tariff: `originalKwh × (newRate - oldRate) + deltaKwh × newRate`
- Product margin: `deltaKwh × productRate`

## Current document structure

1. Tidslinjeoversigt (detailed vertical flow)
2. Proces-til-fase-mapping (reference table)
3. Fakturaberegning (invoice calculation guide)
4. Fase 1-6 (detailed walkthroughs)
5. Særtilfælde (edge cases)
6. Kilder (sources)

## Files

- `docs/datahub3-customer-lifecycle-v1.md` — frozen v1
- `docs/datahub3-customer-lifecycle.md` — working copy for v2
- `docs/datahub3-ddq-business-processes.md` — BRS/RSM reference
- `docs/datahub3-proposed-architecture.md` — system architecture
- `docs/rsm-012-datahub3-measure-data.md` — measure data reference

## Related source files

- `src/XellentSettlement.Api/Services/CorrectionService.cs` — calculation logic
- `src/XellentSettlement.Api/Models/Entities/FlexBillingHistoryLine.cs` — billing data model
- `src/XellentSettlement.Api/Models/Entities/PriceElementRates.cs` — tariff rates

## Potential v2 improvements

- Add concrete examples with real numbers
- Expand aconto vs. faktisk billing model section
- Add sequence diagrams for DataHub message flows
- Document error scenarios and recovery procedures
- Add glossary of terms (GSRN, DDQ, DDM, GLN, etc.)
- Expand Særtilfælde with more detailed procedures
