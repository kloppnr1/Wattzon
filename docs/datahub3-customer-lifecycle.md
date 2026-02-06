# Kundelivscyklus: Onboarding til offboarding

End-to-end-gennemgang af en normal privatkunde fra kontraktunderskrivelse til fraflytning. Dækker både DataHub-kommunikation og den interne faktureringsproces.

---

## Tidslinjeoversigt

Pilretning: `→` = vi sender til DataHub, `←` = vi modtager fra DataHub.

```
FASE 1: ONBOARDING                                              ~15 hverdage
─────────────────────────────────────────────────────────────────────────────
Trigger:      Kontrakt underskrevet — kunde vælger Verdo som leverandør
DataHub:      → BRS-001 (leverandørskifte) med GSRN + ønsket dato + CPR/CVR
              → BRS-043 (kort varsel) eller → BRS-009 (tilflytning)
              → BRS-015 (indsend kundestamdata)
              → BRS-003 (annullér hvis kunden fortryder)
Fakturering:  Opret kundepost, vælg produkt-/tarifplan
              Opsæt faktureringsplan (månedlig/kvartalsvis)
              Beregn aconto-estimat baseret på forventet årsforbrug
                                    │
                                    ▼
FASE 2: AKTIVERING                                               ~1 dag
─────────────────────────────────────────────────────────────────────────────
Trigger:      Ikrafttrædelsesdato nået — vi er nu leverandør på målepunktet
DataHub:      ← RSM-007 (stamdata: afregningsmetode, netområde, GLN, type)
              ← RSM-012 (første måledata, evt. historiske for overgang)
Fakturering:  Tildel nettariffer ud fra netområde + netvirksomhedens GLN
              Indlæs Nordpool spotpris-feed
              Aktiver målepunkt i porteføljen
                                    │
                                    ▼
FASE 3: FØRSTE FAKTURA                                           ~1 måned
─────────────────────────────────────────────────────────────────────────────
Trigger:      Første faktureringsperiode afsluttet
DataHub:      ← RSM-012 (løbende timemåledata — dagligt for flex)
              ← RSM-014 (aggregerede data til afstemning)
Fakturering:  Afregningskørsel pr. time for hele perioden:
                energi        = kWh × (Nordpool spot + Verdo-margin)
                nettarif      = kWh × tarifsats (tidsdiff. dag/nat/spids)
                produktmargin = kWh × produktsats
                + abonnement (dagssats) + elafgift (kWh) + moms (25%)
              Generér faktura → send til kunde (e-Boks/e-mail/post)
                                    │
                                    ▼
FASE 4: DRIFT                                                    måneder/år
─────────────────────────────────────────────────────────────────────────────
Trigger:      Kunden er aktiv — løbende leverance
DataHub:      ← RSM-012 (daglige timemåledata)
              ← RSM-014 (månedlige aggregeringer)
              ← BRS-027 (engrosopgørelse)
              ← Charges (tarifopdateringer fra netvirksomhed)
              ← RSM-004/007 (stamdataændringer)
              → BRS-028/029/030 (on-demand dataforespørgsler)
Fakturering:  Periodisk fakturering (månedligt/kvartalsvist)
              Engrosafstemning: egen beregning vs. DataHub (RSM-014/BRS-027)
              Tarifopdateringer ved nye satser fra netvirksomhed
              Acontoopgørelse: faktisk forbrug vs. acontobetalinger (hvert kvartal)
                                    │
                                    ▼
FASE 5: OFFBOARDING                                              ~15 hverdage
─────────────────────────────────────────────────────────────────────────────
Trigger:      Kunde fraflytter / anden leverandør overtager / manglende betaling
DataHub:      → BRS-002 (leveranceophør — vi opsiger, scenarie B/D)
              → BRS-010 (fraflytning — scenarie C)
              → BRS-044 (annullér ophør ved fortrydelse)
              ← BRS-001 (indgående skifte fra anden DDQ — scenarie A)
Fakturering:  Markér målepunkt inaktivt, registrér slutdato
              Kør slutafregning for delvis periode
                                    │
                                    ▼
FASE 6: AFSLUTNING                                               ~1 måned
─────────────────────────────────────────────────────────────────────────────
Trigger:      Endelige måledata modtaget fra DataHub
DataHub:      ← RSM-012 (endelige måledata op til slutdato)
Fakturering:  Slutafregning: energi + tarif + abonnement (forholdsmæssigt)
              Acontoopgørelse: faktisk forbrug vs. samlede acontobetalinger
              Slutfaktura: kredit → tilbagebetaling / debit → opkrævning
              Arkivér kundepost (5 år) + bevar måledata (3+ år)

─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─

SÆRTILFÆLDE (kan forekomme i enhver fase)
─────────────────────────────────────────────────────────────────────────────
              → BRS-042 (fejlagtigt skifte)    → kreditér alle fakturaer
              → BRS-011 (fejlagtig flytning)   → genberegn + kredit/debit
              → RSM-015 (historiske data)      → verifikation ved tvister
              → RSM-016 (aggregerede data)     → afstemning
              ← BRS-021 (rettet måledata)      → genberegn berørte perioder
```

---

## Proces-til-fase-mapping

| BRS/RSM | Fase | Rolle | Faktureringskonsekvens |
|---------|------|-------|-----------------------|
| BRS-001 (leverandørskifte) | 1 - Onboarding | Vi initierer | Opsæt faktureringsplan + acontoberegning |
| BRS-043 (kort varsel-skifte) | 1 - Onboarding | Vi initierer | Opsæt faktureringsplan + acontoberegning |
| BRS-009 (tilflytning) | 1 - Onboarding | Vi initierer | Opsæt faktureringsplan + acontoberegning |
| BRS-015 (kundestamdata) | 1 - Onboarding | Vi indsender | Ingen direkte |
| BRS-003 (annuller skifte) | 1 - Onboarding | Vi initierer (hvis kunde annullerer før aktivering) | Annuller oprettet faktureringsplan |
| RSM-007 (stamdata-snapshot) | 2 - Aktivering | Vi modtager | Tildel tariffer og produktplan |
| RSM-012 (måledata) | 2-6 | Vi modtager (løbende) | Beregningsgrundlag for afregning |
| RSM-014 (aggregerede data) | 3-4 | Vi modtager (periodisk) | Afstemning mod engrosopgørelse |
| BRS-027 (engrosopgørelse) | 4 - Drift | Vi modtager | Afstem egen afregning mod DataHub |
| BRS-028/029/030 (on-demand data) | 4 - Drift | Vi anmoder | Verifikation af afregningskomponenter |
| BRS-006 (skift af balanceansvarlig) | 4 - Drift | Vi modtager notifikation | Kan påvirke afregningsopsætning |
| RSM-004 (stamdataændring) | 4 - Drift | Vi modtager | Kan udløse genberegning ved ændret afregningsmetode/netområde |
| Charges (tarifopdateringer) | 4 - Drift | Vi modtager | Opdatér satstabeller, påvirker fremtidige fakturaer |
| BRS-002 (leveranceophør) | 5 - Offboarding | Vi initierer (scenarie B, D) | Slutafregning + slutfaktura + acontoopgørelse |
| BRS-010 (fraflytning) | 5 - Offboarding | Vi eller DDM initierer (scenarie C) | Slutafregning + slutfaktura + acontoopgørelse |
| BRS-044 (annuller leveranceophør) | 5 - Offboarding | Vi initierer (ved annullering) | Annuller planlagt slutafregning |
| BRS-001 fra anden DDQ | 5 - Offboarding | Vi modtager (scenarie A) | Slutafregning + slutfaktura + acontoopgørelse |
| BRS-042 (fejlagtigt skifte) | Særtilfælde | Vi initierer | Kreditér alle fakturaer for fejlperioden |
| BRS-011 (fejlagtig flytning) | Særtilfælde | Vi initierer | Genberegn berørte perioder, kredit-/debitnotaer |
| RSM-015 (anmod historiske data) | Særtilfælde | Vi anmoder (tvister, verifikation) | Ingen direkte (verifikation) |
| RSM-016 (anmod aggregerede data) | Særtilfælde | Vi anmoder (afstemning) | Ingen direkte (afstemning) |

---

## Fakturaberegning: Opbygning af en korrekt faktura

En faktura beregnes pr. time (flexafregning) for hele faktureringsperioden. Hver time har sit eget forbrug (kWh fra RSM-012) og sin egen spotpris fra Nordpool.

### Energi (Nordpool spot + Verdo-margin)

Energiprisen pr. time sammensættes af to dele:

| Komponent | Kilde | Beskrivelse |
|-----------|-------|-------------|
| Nordpool spotpris | Elbørsen via markedsdata | Timepris i DKK/kWh, varierer time for time |
| Verdo-margin (tillæg) | Produktplan / kontraktvilkår | Fast øre/kWh-tillæg oven på spotprisen |

```
Energi pr. time = kWh × (spotpris + Verdo-margin)
```

I Xellent er dette forudberegnet:
- `PowerExchangePrice` = ren Nordpool spotpris
- `CalculatedPrice` = spotpris + Verdo-margin (allerede sammenlagt)
- `TimeValue` = kWh forbrugt i timen

Verdo-marginen er den fortjeneste Verdo tager pr. kWh oven på indkøbsprisen fra Nordpool. Størrelsen afhænger af kundens produktplan (f.eks. fast tillæg på X øre/kWh).

### Nettariffer (transport)

Nettariffer opkræves af netvirksomheden for transport af strøm. Satserne er tidsdifferentierede (forskellige satser for dag/nat/spids).

```
Nettarif pr. time = kWh × tarifsats_for_timen
```

- Satser fra `PriceElementRates` (kolonner Price, Price2..Price24 for timer 1-24)
- Tilknytning via `PriceElementCheckData` (dato-interval for hvornår tariffen gælder)
- Kun poster med `ChargeTypeCode = 3` er tariffer
- Netområde (fra RSM-007) bestemmer hvilken netvirksomheds tariffer der gælder

### Produktmargin

Yderligere per-kWh-gebyr defineret i kundens produktplan (f.eks. grøn energi-tillæg, servicetillæg).

```
Produktmargin pr. time = kWh × produktsats
```

- Satser fra `ExuRateTable` baseret på produkttype
- Produkttilknytning via `ProductExtentTable`

### Faste gebyrer (abonnement)

| Gebyr | Kilde | Beregning |
|-------|-------|-----------|
| Netabonnement | Netvirksomhed | Fast månedligt beløb, fordelt pr. dag |
| Eget abonnement (Verdo) | Produktplan | Fast månedligt beløb, fordelt pr. dag |

### Afgifter og moms

| Afgift | Beregning |
|--------|-----------|
| Elafgift | kWh × afgiftssats |
| Moms (25%) | Beregnes af summen af alle ovenstående komponenter |

### Samlet beregning

```
For hver time i faktureringsperioden:
  energi        = kWh × (Nordpool spotpris + Verdo-margin)
  nettarif      = kWh × tarifsats_for_timen
  produktmargin = kWh × produktsats
  elafgift      = kWh × afgiftssats
  abonnement    = dagssats / 24

Fakturalinje  = Σ alle timer for hver komponent
Moms          = 25% af total
Fakturatotal  = sum af alle linjer + moms
```

### Verifikation af en faktura

1. Hent RSM-012-måledata for perioden (kWh pr. time)
2. Hent Nordpool-spotpriser for samme timer
3. Bekræft at `CalculatedPrice ≈ spotpris + aftalt Verdo-margin` for hver time
4. Hent gældende tarifsatser fra netvirksomheden for perioden
5. Beregn hver komponent pr. time og summér
6. Sammenlign med engrosopgørelse (RSM-014 / BRS-027) for afstemning

---

## Fase 1: Onboarding (Kontrakt til skifteanmodning)

**Trigger:** Kunden underskriver leveringsaftale med os.

### Interne trin

1. Salg opretter kundepost (navn, CPR/CVR, kontaktoplysninger, kontraktvilkår)
2. Salg registrerer målepunktets GSRN (18-cifret nummer fra kundens nuværende regning eller via Eloverblik)
3. Systemet bestemmer den korrekte proces:
   - **Ny kunde på eksisterende målepunkt** → leverandørskifte (BRS-001 eller BRS-043)
   - **Kunde flytter ind på ny adresse** → tilflytning (BRS-009)
4. Systemet vælger produkt-/tarifplan baseret på kontraktvilkår
5. Onboarding-post oprettes med status `afventer_datahub`

### DataHub-kommunikation

| Trin | Retning | BRS/RSM | Hvad sker der |
|------|---------|---------|---------------|
| 1 | DDQ → DataHub | **BRS-001** (RSM-001) | Indsend leverandørskifteanmodning med GSRN + ønsket ikrafttrædelsesdato + kundens CPR/CVR |
| 2 | DataHub → DDQ | Kvittering | DataHub validerer: målepunkt eksisterer, ingen konflikter, CPR/CVR matcher |
| 3 | DataHub → gammel DDQ | Notifikation | Nuværende leverandør får besked om at de mister målepunktet |

**Ved behov for kort varsel** (f.eks. hastesag), brug **BRS-043** i stedet — samme meddelelse, kortere varselsperiode.

**Ved tilflytning** (ingen nuværende leverandør på adressen), brug **BRS-009** — lignende flow men uden gammel leverandør.

### Tidsfrister
- BRS-001: minimum 15 hverdages varsel før ikrafttrædelse ⚠ VERIFICÉR
- BRS-043: 1 hverdags varsel ⚠ VERIFICÉR
- BRS-009: kan træde i kraft umiddelbart eller på en fremtidig dato ⚠ VERIFICÉR

### Hvad kan gå galt
- **Afvisning:** DataHub afviser anmodningen (forkert GSRN, konflikterende proces, CPR-mismatch) → ret data og genindsend
- **Annullering af kunde:** Kunden fortryder → send **BRS-003** for at annullere før ikrafttrædelsesdato

---

## Fase 2: Aktivering (Skiftet træder i kraft)

**Trigger:** Ikrafttrædelsesdatoen for leverandørskiftet er nået.

### DataHub-kommunikation

| Trin | Retning | BRS/RSM | Hvad sker der |
|------|---------|---------|---------------|
| 1 | DataHub → DDQ | **RSM-007** (MasterData-kø) | Fuldstændig stamdata-snapshot for målepunktet: type, afregningsmetode, netområde, tilslutningsstatus, netvirksomhed |
| 2 | DataHub → DDQ | **BRS-015** svar | Bekræftelse af kundestamdata ⚠ VERIFICÉR |
| 3 | DataHub → DDQ | **RSM-012** (Timeseries-kø) | Første måledataleverance — kan inkludere historiske data for overgangsperioden |

### Interne trin

1. Modtag og gem stamdata → målepunkt er nu `aktivt` i porteføljen
2. Registrér leveranceperiodens startdato
3. Tildel produkt-/tarifplan til målepunktet
4. Indlæs nettariffer for målepunktets netområde (fra Charges-kø-data)
5. Opsæt faktureringsplan (månedlig eller kvartalsvis — jf. kontrakt)
6. Ved acontofakturering: beregn estimeret kvartalsvist acontobeløb baseret på forventet årsforbrug
7. Kunden er nu synlig i kundeportalen

### Nøgledata modtaget ved aktivering

| Data | Kilde | Bruges til |
|------|-------|------------|
| Målepunktstype (E17 forbrug, E18 produktion) | RSM-007 | Bestemmer afregningsmetode |
| Afregningsmetode (flex / profil) | RSM-007 | Bestemmer hvordan måledata modtages og afregning beregnes |
| Netområde | RSM-007 | Mapper til netvirksomhedens tarifplan |
| Estimeret årsforbrug | RSM-007 ⚠ VERIFICÉR | Beregningsgrundlag for aconto |
| Netvirksomhedens GLN | RSM-007 | Identificerer hvilke tariffer der gælder |

---

## Fase 3: Første faktura

**Trigger:** Første faktureringsperiode afsluttes (typisk 1 måned efter aktivering).

### Måledataflow (løbende fra aktivering)

| Hændelse | Retning | Meddelelse | Frekvens |
|----------|---------|------------|----------|
| Netvirksomhed aflæser måler | MDR → DataHub | BRS-021 | Dagligt (flex) eller månedligt (profil) |
| DataHub videresender til os | DataHub → DDQ | RSM-012 (E66, Timeseries-kø) | Samme frekvens |
| DataHub kører engrosopgørelse | DataHub → DDQ | RSM-014 (E31, Aggregations-kø) | Månedligt ⚠ VERIFICÉR |

### Fakturaberegning

For et **flexafregnet** målepunkt (mest udbredt for kunder med fjernaflæste målere):

```
For hvert interval i faktureringsperioden:
  1. Energiomkostning = mængde_kwh × spotpris_for_interval
  2. Nettarif         = mængde_kwh × nettarifsats_for_interval
  3. Produktmargin    = mængde_kwh × produktsats_for_interval
  4. Abonnement       = dagligt_abonnementsgebyr / intervaller_pr_dag
  5. Afgifter         = mængde_kwh × gældende_afgiftssatser

Fakturalinjetotal = sum af alle intervaller for hver komponent
```

For et **profilafregnet** målepunkt:
- Bruger estimeret forbrugsprofil fordelt på intervaller
- Faktisk forbrug afstemmes efterfølgende via BRS-020-forbrugsopgørelse

### Fakturakomponenter

| Linje | Kilde | Beregningsgrundlag |
|-------|-------|--------------------|
| Energi (spot + margin) | RSM-012-mængder × markedspris + produktmargin | Pr. interval |
| Nettarif (transport) | RSM-012-mængder × nettarifsatser | Pr. interval, tidsdifferentieret |
| Systemtarif | RSM-012-mængder × systemtarifsats | Pr. interval |
| Abonnement (net) | Netvirksomhedens faste månedlige gebyr | Pr. dag |
| Abonnement (eget) | Produktplanens faste gebyr | Pr. dag |
| Elafgift | RSM-012-mængder × afgiftssats | Pr. kWh |
| PSO / grøn afgift ⚠ VERIFICÉR | RSM-012-mængder × afgiftssats | Pr. kWh |
| Moms (25%) | Sum af ovenstående | Standard dansk moms |

### Aconto vs. faktisk

| Model | Beskrivelse | Afstemning |
|-------|-------------|------------|
| **Aconto** | Kunden betaler fast kvartalsvist estimat. Acontoopgørelse afstemmer mod faktisk forbrug. | Hvert kvartal (ved faktureringsperiode-slut) |
| **Faktisk** | Kunden betaler baseret på faktisk målt forbrug hver periode. | Hver faktura er endelig (ingen afstemning nødvendig) |

### Interne trin

1. Afregningsmotor kører for faktureringsperioden
2. Afregningsresultater grupperes efter fakturalinjetyper
3. Faktura genereres og sendes til kunden (e-mail, e-Boks eller post)
4. Betalingsopfølgning begynder (betalingsfrist typisk netto 14-30 dage)
5. Afregningsresultater gemmes til revision og afstemning

---

## Fase 4: Drift (Løbende leverance)

Kunden er aktiv. Følgende sker løbende:

### Daglige operationer

| Hændelse | DataHub | Internt |
|----------|---------|---------|
| Måledata modtages | RSM-012 via Timeseries-kø | Gemmes i tidsserie-databasen |
| Tarif-/gebyropdateringer | Charges-kø | Satstabeller opdateres |
| Stamdataændringer | RSM-004/007 via MasterData-kø | Portefølje opdateres |

### Periodiske operationer

| Hændelse | Frekvens | DataHub | Internt |
|----------|----------|---------|---------|
| Fakturagenerering | Månedligt / kvartalsvist | — | Afregningskørsel → faktura → send til kunde |
| Engrosopgørelsesafstemning | Månedligt ⚠ VERIFICÉR | RSM-014 (BRS-027) | Sammenlign egen afregning med DataHub-aggregering |
| Acontoopgørelse | Kvartalsvist (ved aconto) | — | Beregn faktisk vs. acontobetalinger, net på kvartalsfaktura |
| Produkt-/prisændringer | Jf. kontrakt | — | Opdatér produktsatser, underret kunde |
| Nettarifændringer | Typisk årligt | Charges-kø | Opdatér satstabeller, genberegn fremtidige estimater |
| Skift af balanceansvarlig | Sjældent | BRS-006-notifikation | Opdatér porteføljeregistre |

### Kundeselvbetjening (portal)

- Se forbrugsdata (time-/dags-/månedsgrafer)
- Se og download fakturaer
- Opdatér kontaktinformation
- Se kontraktdetaljer og produktplan

### Betaling og inkasso

| Hændelse | Handling |
|----------|----------|
| Faktura udstedt | Registrér tilgodehavende, send til kunde |
| Betaling modtaget | Match til faktura, opdatér saldo |
| Betaling forfalden | Send rykker (1., 2.) |
| Vedvarende manglende betaling | Initiér leveranceophør (BRS-002) — se Fase 5 |

---

## Fase 5: Offboarding

En kunde forlader os af en af flere årsager. Hver følger et forskelligt forløb:

### Scenarie A: Kunden skifter til anden leverandør

**Trigger:** En anden leverandør indsender BRS-001 for vores målepunkt.

| Trin | Retning | Hvad sker der |
|------|---------|---------------|
| 1 | DataHub → DDQ | Vi modtager notifikation om at en ny leverandør har anmodet om vores målepunkt |
| 2 | (afventer) | Ikrafttrædelsesdatoen nås — leveranceforpligtelsen overgår |
| 3 | DataHub → DDQ | RSM-012 med endelige måledata op til skiftedatoen |
| 4 | Internt | Markér målepunkt som `inaktivt`, registrér leveranceperiodens slutdato |
| 5 | Internt | Kør slutafregning for den delvise periode |
| 6 | Internt | Generér slutfaktura / kreditnota |

Vi initierer ikke noget — den tilgående leverandør driver processen.

### Scenarie B: Kunden opsiger kontrakt

**Trigger:** Kunden meddeler os at de ønsker at ophøre med leverance (flytter til udlandet, skifter til egen produktion osv.).

| Trin | Retning | BRS/RSM | Hvad sker der |
|------|---------|---------|---------------|
| 1 | DDQ → DataHub | **BRS-002** (RSM-005) | Indsend anmodning om leveranceophør med ikrafttrædelsesdato |
| 2 | DataHub → DDQ | Kvittering | DataHub bekræfter |
| 3 | (ikrafttrædelse) | | Leverance ophører. Målepunktet kan overgå til "forsyningspligtig leverandør" ⚠ VERIFICÉR |
| 4 | DataHub → DDQ | RSM-012 | Endelige måledata |
| 5 | Internt | | Slutafregning + faktura |

**Annulleringsmulighed:** Hvis kunden fortryder før ikrafttrædelsesdatoen, send **BRS-044** for at annullere leveranceophøret.

### Scenarie C: Kunden fraflytter

**Trigger:** Kunden melder fraflytning til ny adresse, eller netvirksomheden melder fraflytning.

| Trin | Retning | BRS/RSM | Hvad sker der |
|------|---------|---------|---------------|
| 1 | DDQ → DataHub (eller DDM → DataHub) | **BRS-010** | Fraflytningsbesked med ikrafttrædelsesdato |
| 2 | DataHub → DDQ | Kvittering | DataHub bekræfter |
| 3 | DataHub → DDQ | RSM-012 | Endelige måledata op til fraflytningsdato |
| 4 | Internt | | Slutafregning + faktura |

Hvis kunden også **tilflytter** en ny adresse vi leverer til, kører fraflytning og en ny BRS-009-tilflytning parallelt.

### Scenarie D: Manglende betaling (tvunget leveranceophør)

**Trigger:** Kunden har ikke betalt efter gentagne rykkere.

| Trin | Retning | BRS/RSM | Hvad sker der |
|------|---------|---------|---------------|
| 1 | Internt | | Inkassoproces udtømt, beslutning om opsigelse |
| 2 | DDQ → DataHub | **BRS-002** (RSM-005) | Indsend leveranceophør med årsag: manglende betaling ⚠ VERIFICÉR |
| 3 | DataHub → DDQ | Kvittering | DataHub bekræfter |
| 4 | (ikrafttrædelse) | | Leverance ophører |
| 5 | DataHub → DDQ | RSM-012 | Endelige måledata |
| 6 | Internt | | Slutafregning + faktura. Udestående gæld overgår til inkasso/afskrivning |

**Annulleringsmulighed:** Hvis kunden betaler før ikrafttrædelsesdatoen, send **BRS-044** for at annullere.

---

## Fase 6: Afslutning (Slutafregning)

Uanset offboarding-årsag er afslutningsprocessen den samme:

### Slutafregning

1. Modtag endelige RSM-012-måledata fra DataHub (dækker den sidste delvise periode)
2. Kør afregning for den delvise faktureringsperiode (periodens start → leverancens slutdato)
3. Beregn alle komponenter: energi, nettarif, abonnementer (forholdsmæssigt), afgifter

### For acontokunder: slutopgørelse

1. Beregn faktisk forbrug fra kvartalets start til leverancens slutdato (delvis periode)
2. Sammenlign med acontobetalinger modtaget for denne periode
3. Generér slutfaktura med afstemning:
   - Hvis kunden har betalt for meget → kreditnota / tilbagebetaling
   - Hvis kunden har betalt for lidt → slutfaktura med restbeløb
4. Frist: inden 4 uger efter leverancens ophør (jf. elleveringsbekendtgørelsen)

### Slutfaktura

| Linje | Beskrivelse |
|-------|-------------|
| Energi + margin | Faktisk forbrug × satser for den delvise periode |
| Nettarif | Faktisk forbrug × tarifsatser, forholdsmæssigt |
| Abonnement (eget) | Forholdsmæssigt til leverancens slutdato |
| Abonnement (net) | Forholdsmæssigt til leverancens slutdato |
| Afgifter og gebyrer | Pr. kWh på faktisk forbrug |
| Acontoopgørelse (hvis relevant) | Difference mellem betalte estimater og faktisk total |
| Udestående saldo | Eventuelle ubetalte tidligere fakturaer |
| **Skyldig / tilgodehavende** | Nettobeløb |

### Efter slutfaktura

1. Send slutfaktura til kunden
2. Ved kreditsaldo → initiér tilbagebetaling til kundens bankkonto
3. Ved debitsaldo → normale betalingsvilkår, derefter inkasso hvis ubetalt
4. Arkivér kundepost (bevar i lovpligtig periode — 5 år ⚠ VERIFICÉR)
5. Måledata bevares jf. opbevaringspolitik (3+ år ⚠ VERIFICÉR)
6. Kundeportaladgang deaktiveres efter endelig betaling

---

## Særtilfælde

### Fejlagtigt leverandørskifte (BRS-042)

Hvis det opdages at et leverandørskifte er sket ved en fejl (f.eks. forkert målepunkt, kunden har ikke accepteret):

1. DDQ → DataHub: **BRS-042** tilbageførselsanmodning
2. DataHub tilbagefører skiftet, genindsætter den gamle leverandør
3. Alle måledata og afregning for den fejlagtige periode skal tilbageføres
4. Eventuelle udstedte fakturaer skal krediteres
5. Tidsfrist: inden for 20 hverdage efter ikrafttrædelse ⚠ VERIFICÉR

### Fejlagtig flytning (BRS-011)

Hvis en til- eller fraflytningsdato var forkert:

1. DDQ → DataHub: **BRS-011** med den rettede dato
2. DataHub justerer leveranceperioden
3. Måledata og afregning for den berørte periode skal genberegnes
4. Fakturajusteringer udstedes som kredit-/debitnotaer

### Efterfølgende måledataopdateringer

Netvirksomhed indsender opdaterede måleraflæsninger for en tidligere periode (BRS-021 → RSM-012):

1. Ny RSM-012 modtages med data for en allerede afregnet periode
2. Afregningsmotor genberegner den berørte periode
3. Differencen udstedes som kredit- eller debitnota på næste faktura (eller som selvstændig regulering)

### Kunde bestrider faktura

1. Kunden kontakter support
2. Vi kan anmode om historiske validerede data fra DataHub (RSM-015) for verifikation
3. Vi kan anmode om aggregerede data (RSM-016) til krydskontrol
4. Hvis måledata var forkerte → netvirksomhed indsender rettelse (BRS-021)
5. Hvis vores beregning var forkert → genberegn og udsted kredit-/debitnota

---

## Kilder

- [DataHub 3 DDQ Forretningsproces-reference](datahub3-ddq-business-processes.md)
- [Foreslået systemarkitektur](datahub3-proposed-architecture.md)
- [RSM-012 Måledata-reference](rsm-012-datahub3-measure-data.md)
