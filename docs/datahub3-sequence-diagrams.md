# DataHub 3: Sekvensdiagrammer for meddelelsesflows

Diagrammerne viser kommunikationen mellem aktører i de vigtigste forretningsprocesser. Bruges som supplement til [Kundelivscyklus](datahub3-customer-lifecycle.md).

**Aktører:**
- **Leverandør (DDQ)** — elleverandør
- **DataHub** — Energinets centrale datahub
- **Netvirk (DDM/MDR)** — netvirksomhed / måledataansvarlig
- **Gammel DDQ** — den fratrædende leverandør (ved skifte)
- **Ny DDQ** — den tiltrædende leverandør (ved indgående skifte)
- **Settl** — internt afregningssystem
- **D365** — Dynamics 365 (fakturering/ERP)

---

## 1. BRS-001: Leverandørskifte (vi overtager kunde)

Det mest almindelige onboarding-flow. Kunden har valgt os som ny leverandør.

```mermaid
sequenceDiagram
    autonumber
    participant Salg as Salg/CRM
    participant DDQ as Leverandør (DDQ)
    participant DH as DataHub
    participant GmlDDQ as Gammel DDQ
    participant Netvirk as Netvirk (DDM)

    Note over Salg,DDQ: Kunde underskriver kontrakt

    Salg->>DDQ: Opret kundepost + GSRN
    DDQ->>DH: BRS-001 (RSM-001)<br/>GSRN + ikrafttrædelsesdato + CPR/CVR
    DH-->>DDQ: Kvittering (RSM-009): accepteret/afvist

    alt Afvist
        DH-->>DDQ: Afvisningsårsag (forkert GSRN, CPR-mismatch, konflikt)
        DDQ->>Salg: Fejl — ret data og genindsend
    end

    DH->>GmlDDQ: Notifikation: målepunkt skifter leverandør
    Note over DH: Venter til ikrafttrædelsesdato

    DH->>DDQ: RSM-007 (MasterData-kø)<br/>Stamdata-snapshot: type, afregningsmetode,<br/>netområde, GLN, tilslutningsstatus
    DH->>DDQ: RSM-012 (Timeseries-kø)<br/>Første måledata (evt. historiske)

    DDQ->>DDQ: Tildel tariffer (baseret på netområde)<br/>Aktiver målepunkt i portefølje<br/>Opsæt faktureringsplan + aconto
```

**Tidsfrister:** Min. 15 hverdage varsel (BRS-001) eller 1 hverdag (BRS-043 kort varsel).

**Annullering:** Kunden fortryder → send BRS-003 inden ikrafttrædelsesdato.

---

## 2. RSM-012: Dagligt måledataflow (drift)

Det daglige heartbeat — netvirksomheden aflæser målere, DataHub validerer og videresender.

```mermaid
sequenceDiagram
    autonumber
    participant Netvirk as Netvirk (MDR)
    participant DH as DataHub
    participant DDQ as Leverandør (DDQ)
    participant Settl as Afregningsmotor

    loop Dagligt (for hvert målepunkt)
        Netvirk->>DH: BRS-021: Validerede måledata<br/>(kWh pr. time/kvarter)
        DH->>DH: Validering + skemacheck
        DH->>DDQ: RSM-012 (Timeseries-kø, E66)<br/>ProcessType: E23 (periodisk) eller D42 (flex)
    end

    DDQ->>DDQ: GET /cim/Timeseries → peek besked
    DDQ->>DDQ: Parse CIM JSON:<br/>MeteringPointId, period, resolution,<br/>Point[] (position + quantity + quality)
    DDQ->>DDQ: Gem i tidsserie-lager
    DDQ->>DH: DELETE /cim/dequeue/{MessageId}

    Note over DDQ,Settl: Ved faktureringsperiode-slut

    Settl->>Settl: Afregningskørsel:<br/>energi = kWh × (spot + margin)<br/>nettarif = kWh × tarifsats<br/>produktmargin = kWh × produktsats<br/>+ abonnement + afgifter + moms
```

**Vigtige felter i RSM-012:**
- `Series/MarketEvaluationPoint/mRID` = GSRN (18 cifre)
- `Series/Period/resolution` = PT15M, PT1H eller P1M
- `Series/Period/Point/quantity` = kWh (max 3 decimaler)
- `Series/Period/Point/quality` = A01/A02/A03/A06

---

## 3. BRS-002: Leveranceophør (vi opsiger)

Kunden opsiger eller fraflytter. Vi initierer ophøret.

```mermaid
sequenceDiagram
    autonumber
    participant DDQ as Leverandør (DDQ)
    participant DH as DataHub
    participant Netvirk as Netvirk (DDM)
    participant Settl as Afregningsmotor
    participant D365 as D365 (fakturering)

    Note over DDQ: Beslutning: leveranceophør<br/>(kundeopsigelse / manglende betaling / fraflytning)

    DDQ->>DH: BRS-002 (RSM-005)<br/>GSRN + ikrafttrædelsesdato + årsag
    DH-->>DDQ: Kvittering (RSM-009)

    alt Kunden fortryder / betaler
        DDQ->>DH: BRS-044: Annuller leveranceophør
        DH-->>DDQ: Kvittering: ophør annulleret
        Note over DDQ: Leverance fortsætter
    end

    Note over DH: Ikrafttrædelsesdato nået

    DH->>DDQ: RSM-012 (Timeseries-kø)<br/>Endelige måledata op til slutdato

    DDQ->>DDQ: Markér målepunkt inaktivt<br/>Registrér leveranceperiodens slutdato

    Settl->>Settl: Slutafregning: delvis periode<br/>energi + tarif + abonnement (forholdsmæssigt)

    alt Acontokunde
        Settl->>Settl: Acontoopgørelse:<br/>faktisk forbrug vs. acontobetalinger
        Settl->>D365: Kredit (overbetalt) eller<br/>debit (underbetalt)
    end

    Settl->>D365: Slutfaktura
    D365->>D365: Send til kunde (e-Boks/e-mail)

    Note over DDQ: Arkivér kundepost (5 år)<br/>Bevar måledata (3+ år)
```

**Offboarding-scenarier:**
- **Scenarie A:** Anden leverandør sender BRS-001 for vores målepunkt → vi modtager, ikke initierer
- **Scenarie B/D:** Vi sender BRS-002 (opsigelse / manglende betaling)
- **Scenarie C:** Fraflytning → BRS-010

---

## 4. BRS-001 indgående: Leverandørskifte (vi mister kunde)

En anden leverandør overtager vores kunde. Vi er den passive part.

```mermaid
sequenceDiagram
    autonumber
    participant NyDDQ as Ny DDQ (anden leverandør)
    participant DH as DataHub
    participant DDQ as Leverandør (DDQ)
    participant Settl as Afregningsmotor
    participant D365 as D365 (fakturering)

    NyDDQ->>DH: BRS-001 (RSM-001)<br/>Anmod om vores målepunkt
    DH->>DDQ: Notifikation: målepunkt skifter<br/>Ikrafttrædelsesdato: DD-MM-YYYY

    Note over DDQ: Vi kan ikke blokere skiftet

    Note over DH: Ikrafttrædelsesdato nået

    DH->>DDQ: RSM-012: Endelige måledata<br/>op til skiftedato

    DDQ->>DDQ: Markér målepunkt inaktivt

    Settl->>Settl: Slutafregning (delvis periode)
    Settl->>D365: Slutfaktura + acontoopgørelse
    D365->>D365: Send slutfaktura til kunde
```

---

## 5. Engrosopgørelse og afstemning (BRS-027)

Månedlig afstemning af vores egne afregningsberegninger mod DataHubs engrosopgørelse.

```mermaid
sequenceDiagram
    autonumber
    participant DH as DataHub
    participant DDQ as Leverandør (DDQ)
    participant Settl as Afregningsmotor

    Note over DH: Månedlig engrosopgørelse kører

    DH->>DDQ: RSM-014 (Aggregations-kø, E31)<br/>Aggregerede data pr. netområde

    DDQ->>DDQ: Peek + parse RSM-014

    Settl->>Settl: Sammenlign:<br/>Egen afregning vs. DataHub-aggregering

    alt Afvigelse fundet
        Settl->>Settl: Identificér afvigende målepunkter
        DDQ->>DH: RSM-016: Anmod detaljerede<br/>aggregerede data for perioden
        DH->>DDQ: RSM-014 (svar med<br/>OriginalTransactionReference)
        Settl->>Settl: Analyser afvigelse:<br/>manglende måledata? forkerte satser?

        alt Manglende måledata
            DDQ->>DH: RSM-015: Anmod historiske<br/>validerede data for målepunkt
            DH->>DDQ: RSM-012 (ProcessType E30)<br/>Historiske data
            Settl->>Settl: Genberegn berørte perioder
        end
    else Ingen afvigelse
        Note over Settl: ✓ Afstemning OK
    end

    DDQ->>DH: DELETE /cim/dequeue/{MessageId}
```

**Aggregeringstyper (⚠ VERIFICÉR koder):**
- D03 = Foreløbig aggregering
- D04 = Korrigeret aggregering
- D05 = Endelig aggregering

---

## 6. Tarifopdatering (Charges-kø)

Netvirksomheden ændrer tarifsatser — påvirker fremtidige afregningsberegninger.

```mermaid
sequenceDiagram
    autonumber
    participant Netvirk as Netvirk (DDM)
    participant DH as DataHub
    participant DDQ as Leverandør (DDQ)
    participant Settl as Afregningsmotor

    Netvirk->>DH: Opdaterede tarifsatser<br/>(typisk årligt, 1. jan / 1. apr / 1. okt)
    DH->>DDQ: Charges-kø: Nye tarifsatser<br/>Netområde + gyldighedsperiode + satser

    DDQ->>DDQ: Peek + parse Charges-besked
    DDQ->>DDQ: Opdatér PriceElementRates:<br/>Price, Price2..Price24 (timer 1-24)<br/>+ gyldighedsdatoer

    Note over DDQ,Settl: Fra gyldighedsdato

    Settl->>Settl: Nye afregningskørsler bruger<br/>opdaterede satser automatisk

    Note over Settl: Eksisterende fakturaer<br/>berøres IKKE (medmindre<br/>korrektion modtages)

    DDQ->>DH: DELETE /cim/dequeue/{MessageId}
```

---

## 7. Overblik: Kø-routing

Oversigt over hvilke meddelelser der ankommer i hvilke køer:

```mermaid
flowchart LR
    DH[DataHub B2B API]

    DH -->|GET /cim/Timeseries| TS[Timeseries-kø]
    DH -->|GET /cim/Aggregations| AG[Aggregations-kø]
    DH -->|GET /cim/MasterData| MD[MasterData-kø]
    DH -->|GET /cim/Charges| CH[Charges-kø]

    TS --> RSM012[RSM-012: Måledata<br/>E66 / E23,D42,E30]
    TS --> RSM014a[RSM-014: Aggregerede data<br/>E31]

    AG --> RSM014b[RSM-014: Aggregerede data<br/>E31]

    MD --> RSM007[RSM-007: Stamdata-snapshot]
    MD --> RSM004[RSM-004: Stamdataændring]

    CH --> Tarif[Tarif-/prisopdateringer]
```

---

## Kilder

- [Kundelivscyklus: Onboarding til offboarding](datahub3-customer-lifecycle.md)
- [DataHub 3 DDQ Forretningsproces-reference](datahub3-ddq-business-processes.md)
- [RSM-012 Måledata-reference](rsm-012-datahub3-measure-data.md)
- [Foreslået systemarkitektur](datahub3-proposed-architecture.md)
- [Autentificering og sikkerhed](datahub3-authentication-security.md)
- CIM Webservice Interface (Dok. 22/03077-1)
- CIM EDI Guide (Dok. 15/00718-191)
