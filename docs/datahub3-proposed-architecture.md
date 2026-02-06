# Foreslået arkitektur: Afregningssystem til DataHub 3

Overordnet arkitektur for et nyt afregningssystem der opererer som elleverandør (DDQ) i Energinet DataHub 3, med ca. 80.000 kunder.

## Datavolumen-estimater

| Metrik | PT1H (nuværende) | PT15M (fremtidig) |
|--------|-------------------|-------------------|
| Datapunkter pr. kunde pr. dag | 24 | 96 |
| Datapunkter pr. dag (80K kunder) | 1,92M | 7,68M |
| Datapunkter pr. måned | ~58M | ~230M |
| Datapunkter pr. år | ~700M | ~2,8 mia. |
| RSM-012-meddelelser pr. dag (gns. 1 pr. kunde) | ~80K | ~80K |
| Bytes pr. datapunkt (position + mængde + kvalitet) | ~40 B | ~40 B |
| Rå tidsserielagring pr. år | ~28 GB | ~112 GB |

Disse volumener driver centrale valg omkring lagringsmotor, partitionering og indlæsningspipeline.

---

## Systemkontekst

```
┌───────────────┐         ┌───────────────┐        ┌──────────────┐
│  Backoffice   │         │  Kunde-       │        │  Fakturering │
│  UI           │         │  portal       │        │  / ERP       │
└──────┬────────┘         └──────┬────────┘        └──────┬───────┘
       │                         │                        │
       └─────────────────┬───────┘────────────────────────┘
                         │
                  ┌──────┴───────┐
                  │   API        │
                  │   Gateway    │
                  └──────┬───────┘
                         │
       ┌─────────────────┼─────────────────┐
       │                 │                 │
┌──────┴──────┐  ┌───────┴──────┐  ┌───────┴───────┐
│ DataHub     │  │ Afregnings-  │  │ Kunde- &      │
│ Integra-    │  │ motor        │  │ Portefølje-   │
│ tionsservice│  │              │  │ service       │
└──────┬──────┘  └───────┬──────┘  └───────┬───────┘
       │                 │                 │
       └─────────────────┼─────────────────┘
                         │
              ┌──────────┴──────────┐
              │  Tidsserie-lager    │
              │  + Relationelt lager│
              └──────────┬──────────┘
                         │
              ┌──────────┴──────────┐
              │  Energinet          │
              │  DataHub 3          │
              │  (B2B API)          │
              └─────────────────────┘
```

---

## Kerneservices

### 1. DataHub Integrationsservice

Al kommunikation med DataHub 3 B2B API.

**Delkomponenter:**
- **Auth Manager** — OAuth2-tokenlivscyklus (hent, cache, proaktiv fornyelse)
- **Queue Poller** — Poller alle fire peek-endpoints på uafhængige intervaller
- **Message Parser** — CIM JSON-deserialisering, skemavalidering, routing
- **Request Sender** — Bygger og sender udgående CIM-forespørgsler, sporer ventende korrelationer

**Køer der polles:**

| Kø | Endpoint | Meddelelser | Forretningsprocesser |
|----|----------|-------------|----------------------|
| Timeseries | `GET /cim/Timeseries` | RSM-012, RSM-014 | BRS-020, BRS-021, BRS-027 |
| Aggregations | `GET /cim/Aggregations` | RSM-014 | BRS-027, BRS-028, BRS-029, BRS-030 |
| MasterData | `GET /cim/MasterData` | RSM-004, RSM-007 | BRS-001, BRS-006, BRS-009, BRS-010 |
| Charges | `GET /cim/Charges` | Pris-/tariflister | Tarif-/gebyropdateringer |

**Udgående forespørgsler:**

| Handling | BRS/RSM |
|----------|---------|
| Leverandørskifte | BRS-001, BRS-043 |
| Leveranceophør | BRS-002, BRS-005 |
| Annuller skifte / tilbageførsel | BRS-003, BRS-042 |
| Annuller leveranceophør | BRS-044 |
| Anmod om historiske data | RSM-015 |
| Anmod om aggregerede data | RSM-016 |
| Indsend kundedata | BRS-015 |

**Designbeslutninger:**
- Poll specifikke køer (ikke `/cim/all`) — uafhængigt throughput og fejlisolering pr. meddelelsestype
- Dequeue først efter bekræftet persistering — at-least-once leveringsgaranti
- Idempotent behandling via gemt `MessageId` — sikker genleverance efter nedbrud

**Throughput-betragtning:**
- Ved 80K RSM-012-meddelelser/dag behandler Timeseries-køen ~55 meddelelser/minut i gennemsnit
- Peek → parse → persist → dequeue-cyklussen skal gennemføres på <1 sekund i gennemsnit
- Parallel polling af forskellige køer forhindrer én langsom kø i at blokere andre

### 2. Afregningsmotor

Kerneforretningslogik: beregner afregningsbeløb ud fra måledata, tariffer og markedspriser.

**Ansvarsområder:**
- Beregn energiafregning pr. målepunkt pr. interval
- Anvend nettariffer (tidsdifferentierede satser fra Charges)
- Anvend produktmarginer og abonnementsgebyrer
- Producér afregningsopgørelser pr. kunde pr. faktureringsperiode
- Afstem mod DataHub engrosopgørelser (BRS-027)
- Understøt genberegning on-demand ved opdaterede data eller satser

**Forretningsprocesser:**
- **BRS-020** — Forbrugsopgørelser for profilafregnede målepunkter
- **BRS-021** — Validerede måledata modtaget → udløser afregningsberegning
- **BRS-027** — Engrosopgørelsesresultater bruges til afstemning
- **BRS-028/029/030** — On-demand aggregerede data til verifikation

**Designbeslutninger:**
- Batchorienteret til planlagte afregningskørsler (nat/uge)
- Hændelsesdrevet genberegning når nye måledata eller satsændringer ankommer
- Partitioneret efter netområde — afregningskørsler kører uafhængigt pr. netområde for parallelisering
- Uforanderlige afregningssnapshots — hver kørsel producerer et versioneret resultat, tidligere versioner bevares

**Volumenbetragtning:**
- En fuld månedlig afregningskørsel ved PT15M: 80K kunder × 96 punkter × 30 dage = ~230M rækker at læse og aggregere
- Partitionér efter netområde + måned for at holde individuelle forespørgsler håndterbare
- Præ-aggregér dagstotaler under indlæsning for at fremskynde månedlige opgørelser

### 3. Kunde- & Porteføljeservice

Administrerer leverandørens kundeportefølje, målepunktstilknytninger og livscyklus.

**Ansvarsområder:**
- Vedligehold målepunktsregister (GSRN, type, afregningsmetode, netområde, tilslutningsstatus)
- Spor leveranceperioder pr. målepunkt (start-/slutdatoer, aktiv leverandør)
- Behandl stamdata-opdateringer fra DataHub (RSM-004, RSM-007)
- Administrér kunderegistre (CPR/CVR, navn, kontakt)
- Orkestrer leverandørskifteworkflows (tilstandsmaskine pr. målepunkt)
- Koordinér til-/fraflytningsflows

**Forretningsprocesser:**
- **BRS-001** — Standard leverandørskifte
- **BRS-002** — Leveranceophør
- **BRS-003** — Annuller ventende skifte
- **BRS-005** — Tvunget leveranceophør
- **BRS-006** — Skift af balanceansvarlig
- **BRS-009** — Tilflytning
- **BRS-010** — Fraflytning
- **BRS-011** — Fejlagtig flytning
- **BRS-015** — Indsendelse af kundestamdata
- **BRS-042** — Fejlagtigt leverandørskifte (tilbageførsel)
- **BRS-043** — Leverandørskifte med kort varsel
- **BRS-044** — Annuller leveranceophør

**Designbeslutninger:**
- Tilstandsmaskine pr. målepunkt — hver BRS-proces mapper til en tilstandsovergang
- Event sourcing til auditspor — hver tilstandsændring er en uforanderlig hændelse (hvem, hvornår, hvorfor, hvilken BRS)
- Separat læsemodel til hurtige porteføljeforespørgsler (f.eks. "list alle aktive målepunkter i netområde 344")

---

## Dataarkitektur

### Tidsserie-lager

Den dominerende datavolumen er måledata. Dette kræver en lagringsstrategi optimeret til:
- Høj skrivethroughput (7,68M indsættelser/dag ved PT15M)
- Intervalforespørgsler efter målepunkt + tidsperiode
- Aggregeringsforespørgsler på tværs af mange målepunkter (afregningskørsler)

**Anbefaling: partitioneret relationel tabel (PostgreSQL/TimescaleDB eller SQL Server med partitionering)**

**Skemakoncept:**

```
metering_data
├── metering_point_id   (GSRN, indekseret)
├── timestamp           (UTC, del af partitioneringsnøgle)
├── resolution          (PT15M / PT1H / P1M)
├── quantity_kwh        (decimal)
├── quality_code        (A01/A02/A03/A06)
├── source_message_id   (DataHub MessageId, til sporbarhed)
├── received_at         (indlæsningstidspunkt)
└── PARTITION BY RANGE (timestamp), månedligt
```

**Partitioneringsstrategi:**
- Månedlige partitioner på `timestamp` — holder aktiv partition lille, gamle partitioner kan komprimeres eller arkiveres
- Sammensat indeks på `(metering_point_id, timestamp)` til punktopslag
- Ved PT15M med 80K kunder: ~230M rækker/måned, ~40 bytes/række = ~9 GB/måned rå (før indekser)

**Opbevaring:**
- Hot: indeværende måned + 2 foregående (aktivt afregningsvindue)
- Warm: 12 måneder (genberegningsvindue)
- Cold/arkiv: 3+ år (lovkrav ⚠ VERIFICÉR)

### Relationelt lager

Standard relationel database til strukturerede domænedata:

| Domæne | Nøgleentiteter |
|--------|----------------|
| Portefølje | MålepunktMeteringPoint, Customer, SupplyPeriod, BalanceResponsible |
| Livscyklus | ProcessRequest, ProcessEvent (event-sourcede tilstandsovergange) |
| Afregning | SettlementRun, SettlementLine, BillingPeriod |
| Satser | GridTariff, ProductMargin, Subscription, ChargeSchedule |
| DataHub | InboundMessage (log), OutboundRequest, PendingCorrelation |
| System | DeadLetter, ProcessedMessageId (idempotens) |

### Præ-aggregeringspipeline

For effektiv håndtering af afregningsforespørgsler over 230M rækker/måned:

```
Rå måledata (PT15M)
  → Daglig aggregeringsjob
    → daily_summary (metering_point_id, dato, total_kwh, peak_kwh, off_peak_kwh)
      → Månedlig afregningskørsel læser dagsoversigter i stedet for rådata
        → 80K × 30 = 2,4M rækker i stedet for 230M
```

For tarifdifferentieret afregning (forskellige satser pr. time) grupperer den daglige aggregering efter tarifperiode i stedet for blot at summere dagen.

---

## API Gateway

Enkelt indgangspunkt for alle interne og eksterne forbrugere.

**Forbrugere:**
- **Backoffice UI** — Porteføljestyring, afregningsgennemgang, manuel procesinitiering
- **Kundeportal** — Forbrugsoversigt, faktureringshistorik
- **Fakturering/ERP** — Eksport af afregningsresultater, faktureringstriggers

**Centrale API-domæner:**

| Domæne | Eksempler |
|--------|-----------|
| Portefølje | List målepunkter, vis kundedetaljer, søg efter netområde |
| Livscyklus | Initiér leverandørskifte, vis processtatus, annuller ventende forespørgsel |
| Måledata | Forespørg forbrug pr. målepunkt + periode, sammenlign perioder |
| Afregning | Vis afregningskørselsresultater, udløs genberegning, eksportér til fakturering |
| Satser | Vis aktuelle tariffer, upload produktmarginer |
| Admin | Systemhelbred, kø-lag, dead-letter-gennemgang |

---

## Tværgående hensyn

### Observerbarhed

- Struktureret logning med `CorrelationId` (DataHub) og internt `TraceId`
- Metrikker: modtagne meddelelser/sek pr. kø, afregningskørselsvarighed, API-latens
- Alarmer: kø-behandlingslag, DataHub-autentificeringsfejl, dead-letter-vækst

### Fejlhåndtering

| Scenarie | Handling |
|----------|----------|
| DataHub 5xx / timeout | Genforsøg med eksponentiel backoff, dequeue ikke |
| Fejl i meddelelsesparsing | Dead-letter, dequeue for at frigøre køen |
| Forretningsvalideringsfejl | Log + gem til gennemgang, dequeue |
| Afregningsberegningsfejl | Fejl kørslen, alarmér, bevar delresultater til fejlfinding |

### Sikkerhed

- OAuth2 client credentials gemt i vault (Azure Key Vault el.lign.)
- CPR/CVR-data krypteret at rest (GDPR)
- Rollebaseret adgangskontrol på API-endpoints
- Auditlog for alle tilstandsændrende operationer

### Konfiguration

| Indstilling | Formål |
|-------------|--------|
| `DataHub:Environment` | aktørtest / preprod / prod |
| `DataHub:TenantId` | Azure AD-tenant |
| `DataHub:ClientId` / `ClientSecret` | OAuth2-legitimationsoplysninger |
| `DataHub:BaseUrl` | API-host |
| `DataHub:PollIntervalMs` | Poll-interval pr. kø |
| `DataHub:ActorGLN` | Aktørens GLN |
| `Settlement:DefaultResolution` | PT1H (nuværende) eller PT15M (fremtidig) |
| `Settlement:RetentionMonthsHot` | Aktivt datavindue |
| `TimeSeries:PartitionScheme` | Månedlig / ugentlig |

---

## Teknologianbefalinger

### Applikationsplatform

| Mulighed | Egnethed | Noter |
|----------|----------|-------|
| **.NET 9 (anbefalet)** | Stærk | Naturlig for teamet, modent økosystem til baggrundsservices (`IHostedService`), stærke SQL Server/PostgreSQL-drivere, førsteklasses Azure-integration |
| Go | God til indlæsningsservices | Høj concurrency, lille footprint — anvendelig til Queue Poller hvis den adskilles som selvstændig service |
| Java/Spring | Brugbar | Udbredt i energisektoren, men tungere runtime og langsommere iteration hvis teamet er .NET-native |

**.NET passer godt fordi:**
- `BackgroundService` / `IHostedService` mapper direkte til Queue Poller-mønsteret
- `HttpClientFactory` med `DelegatingHandler` til OAuth2 token-injektion
- EF Core til relationelt lager, Dapper eller rå ADO.NET til high-throughput tidsserieindlæsning
- Aspire til lokal dev-orkestrering af flere services

### Tidsserie-database

Det mest kritiske valg — det afgør om 230M rækker/måned kan forespørges effektivt til afregning.

| Mulighed | Fordele | Ulemper |
|----------|---------|---------|
| **TimescaleDB (anbefalet)** | Formålsbygget til tidsserier oven på PostgreSQL. Automatisk partitionering (hypertables), indbygget komprimering (90%+ for måledata), continuous aggregates erstatter den manuelle præ-aggregeringspipeline, standard SQL | Kræver PostgreSQL — nyt hvis teamet kun kender SQL Server |
| PostgreSQL + native partitionering | Fuld kontrol, ingen extensions. Deklarativ partitionering fungerer godt i denne skala | Manuel partitionsstyring, ingen indbygget komprimering eller continuous aggregates |
| SQL Server med partitionering | Velkendt hvis teamet bruger SQL Server. Columnstore-indekser komprimerer godt til analyser | Partitionsstyring er mere manuel, columnstore-opdateringer er langsommere end row-store-indsættelser, licenskostnad i skala |
| ClickHouse | Ekstremt hurtigt til analytiske forespørgsler, columnar komprimering | Overkill til 80K kunder, ikke godt til punktopslag på metering_point_id, kræver separat driftsviden |
| InfluxDB | Formålsbygget tidsserie | Svagere SQL-understøttelse, sværere at joine med relationelle data, kommerciel licens til clustering |

**Hvorfor TimescaleDB:**
- Ved ~230M rækker/måned (PT15M) opnår hypertable-komprimering typisk 10-15x — reducerer 9 GB/måned til under 1 GB
- Continuous aggregates vedligeholder automatisk de daglige/timevise opsummeringer afregningsmotor behøver
- Standard PostgreSQL nedenunder — samme driver, samme værktøj, samme backup-strategi som det relationelle lager
- Én databasemotor til både tidsserier og relationelle data forenkler drift

### Relationel database

| Mulighed | Egnethed | Noter |
|----------|----------|-------|
| **PostgreSQL (anbefalet)** | Stærk | Gratis, bevist i skala, passer naturligt med TimescaleDB til en single-engine stak |
| SQL Server | Stærk | Bedre hvis organisationen allerede driver SQL Server-infrastruktur og licensering |

Hvis TimescaleDB vælges til tidsserier, er PostgreSQL til det relationelle lager det naturlige valg — én databasemotor at drifte.

### Intern message bus

Til afkobling af DataHub Integrationsservice fra domænehandlers:

| Mulighed | Egnethed | Noter |
|----------|----------|-------|
| **In-process channels (anbefalet til start)** | God | .NET `Channel<T>` eller MediatR. Simplest mulige løsning når alle services kører i én proces. Ingen infrastrukturafhængighed |
| RabbitMQ | God til udskalering | Nødvendig hvis services deployes uafhængigt. Holdbare køer, dead-letter-support indbygget |
| Azure Service Bus | God til Azure-hosting | Managed, ingen driftsbyrde. Godt match hvis man allerede er på Azure |
| Kafka | Overkill | Designet til langt højere throughput. Tilføjer driftskompleksitet der ikke er berettiget ved 80K kunder |

**Anbefaling:** Start med in-process channels. Udtrækkes til RabbitMQ eller Azure Service Bus hvis/når services har brug for uafhængig skalering eller deployment.

### Hosting & deployment

| Mulighed | Egnethed | Noter |
|----------|----------|-------|
| **Containeriseret (Docker + orkestrator)** | Anbefalet | Hver service som en container. Docker Compose til dev, Kubernetes eller Azure Container Apps til produktion |
| Azure App Service | Simpel | God til API Gateway. Mindre naturlig til langkørende baggrundsservices (Queue Poller) |
| Windows Service | Brugbar | Teamet kender mønsteret. Fungerer men begrænser portabilitet og skalering |
| VMs | Undgå | Manuel skalering, ingen isolering mellem services |

**Anbefaling:** Docker-containere fra dag ét. Brug Azure Container Apps eller Kubernetes i produktion — health checks, auto-genstart og skalering er indbygget. Queue Poller og Afregningsmotor har gavn af at køre som always-on containere frem for request-drevne services.

### Frontend

| Mulighed | Egnethed | Noter |
|----------|----------|-------|
| **React + TypeScript (anbefalet)** | Stærk | Teamet bruger det allerede. Rigt økosystem til datatabeller, grafer (forbrugsvisning), formularer |
| Blazor | Brugbar | .NET end-to-end. Svagere økosystem til kompleks datavisualisering |

### Observerbarhedsstak

| Mulighed | Egnethed | Noter |
|----------|----------|-------|
| **OpenTelemetry + Grafana/Loki/Prometheus** | Anbefalet | Leverandørneutral, .NET har førsteklasses OTel-understøttelse. Grafana-dashboards til kø-lag, afregningskørselsmetrikker |
| Azure Monitor + Application Insights | God til Azure | Managed, mindre drift. Indbygget .NET-integration |
| ELK (Elasticsearch + Kibana) | Brugbar | Tungere at drifte, men kraftfuld til logsøgning |

### Teknologistak-oversigt

```
┌─────────────────────────────────────────────────┐
│  Frontend:  React 19 + TypeScript + Vite        │
├─────────────────────────────────────────────────┤
│  API:       .NET 9 (ASP.NET Core)               │
│  Services:  .NET 9 BackgroundService             │
│  Bus:       In-process channels → RabbitMQ       │
├─────────────────────────────────────────────────┤
│  Tidsserier:  TimescaleDB (på PostgreSQL)       │
│  Relationelt: PostgreSQL                        │
├─────────────────────────────────────────────────┤
│  Hosting:      Docker → Azure Container Apps    │
│  Observerbarhed: OpenTelemetry + Grafana        │
│  Hemmeligheder: Azure Key Vault                 │
│  CI/CD:        Azure DevOps Pipelines           │
└─────────────────────────────────────────────────┘
```

---

## Estimerede månedlige driftsomkostninger (Azure, West Europe)

Alle priser er omtrentlige, baseret på Azure-listepriser primo 2025, omregnet til DKK med kurs 6,90 kr./USD. Faktiske omkostninger afhænger af reserverede instanser, enterprise-aftaler og forbrugsmønstre.

### Tre scenarier

| | Fase 1-2 (Lean start) | Produktion PT1H | Produktion PT15M |
|-|------------------------|-----------------|------------------|
| **Kunder** | 80.000 | 80.000 | 80.000 |
| **Opløsning** | PT1H | PT1H | PT15M |
| **Datapunkter/måned** | 58M | 58M | 230M |

#### Compute — Azure Container Apps

| Service | Profil | Lean start | Prod PT1H | Prod PT15M |
|---------|--------|------------|-----------|------------|
| API Gateway | Always-on, 0,5 vCPU / 1 GB | 275 kr. | 275 kr. | 275 kr. |
| DataHub Queue Poller | Always-on, 0,5 vCPU / 1 GB | 275 kr. | 275 kr. | 275 kr. |
| Afregningsmotor | Burst, 2 vCPU / 4 GB, ~2t/dag | — | 175 kr. | 345 kr. |
| **Compute subtotal** | | **550 kr.** | **725 kr.** | **895 kr.** |

Beregningsgrundlag: Azure Container Apps consumption-priser — ca. 0,000166 kr./vCPU-sek, ca. 0,0000207 kr./GiB-sek. Always-on services kører 2.592.000 sekunder/måned. Afregningsmotoren skalerer op under kørsler.

#### Database — Azure Database for PostgreSQL Flexible Server

| Komponent | Lean start | Prod PT1H | Prod PT15M |
|-----------|------------|-----------|------------|
| Compute (TimescaleDB) | Burstable B2ms (2 vCPU, 8 GB) — 690 kr. | GP D4s (4 vCPU, 16 GB) — 1.930 kr. | GP D8s (8 vCPU, 32 GB) — 3.865 kr. |
| Storage (provisioneret) | 128 GB — 105 kr. | 256 GB — 205 kr. | 512 GB — 405 kr. |
| Backup (inkluderet) | Op til 128 GB | Op til 256 GB | Op til 512 GB |
| **Database subtotal** | **795 kr.** | **2.135 kr.** | **4.270 kr.** |

Noter:
- TimescaleDB-komprimering opnår typisk 10-15x på måledata — 230M rækker/måned (~9 GB rå) komprimeres til under 1 GB
- Efter 12 måneder ved PT15M: ~112 GB rå → ~11 GB komprimeret. Lagerplads-headroom er til indekser, ukomprimerede hot-data og relationelle tabeller
- Én PostgreSQL-instans dækker både tidsserier (TimescaleDB hypertable) og relationelle data i denne skala

#### Understøttende services

| Service | Månedlig pris | Noter |
|---------|---------------|-------|
| Azure Key Vault | 20 kr. | Hemmelighedslagring til OAuth2-credentials, connection strings |
| Azure Container Registry (Basic) | 35 kr. | Docker image-lagring |
| Azure DevOps | 0 kr. | Første 5 brugere gratis; allerede i brug |
| Grafana Cloud (gratis tier) | 0 kr. | Op til 10K metrikserier, 50 GB logs. Selvhostet Grafana i container hvis overskredet: ~140 kr. |
| DNS / netværk | 35 kr. | Ubetydeligt ved dette trafikvolumen |
| **Understøttende subtotal** | **~90 kr.** | |

#### Månedlige totaler

| | Lean start | Prod PT1H | Prod PT15M |
|-|------------|-----------|------------|
| Compute | 550 kr. | 725 kr. | 895 kr. |
| Database | 795 kr. | 2.135 kr. | 4.270 kr. |
| Understøttende | 90 kr. | 90 kr. | 90 kr. |
| **Total** | **~1.435 kr./md.** | **~2.950 kr./md.** | **~5.255 kr./md.** |
| **Årligt** | **~17.220 kr./år** | **~35.400 kr./år** | **~63.060 kr./år** |

### Omkostningsreduktionsmuligheder

| Mulighed | Besparelse | Noter |
|----------|------------|-------|
| 1-årig reserveret instans (DB) | 30-35% | Sænker Prod PT15M database fra 4.270 kr. → ~2.760 kr. |
| 3-årig reserveret instans (DB) | 50-55% | Sænker Prod PT15M database fra 4.270 kr. → ~1.930 kr. |
| Dev/Test-priser | 40-50% | Til aktørtest- og preprod-miljøer |
| Selvhostet PostgreSQL på VM | 20-30% | Byt driftsindsats for lavere pris — D4s VM til ~965 kr./md. vs. managed 1.930 kr./md. |
| Spot-instanser til afregningsmotor | 60-80% | Afregningskørsler kan afbrydes og genstartes |

### Hvad er IKKE inkluderet

- **Udviklingsmiljøer** (aktørtest, preprod) — multiplicér med 0,5-0,7x pr. miljø (mindre instanser)
- **Personale-/udviklingsomkostninger** — den klart dominerende omkostning
- **Fakturering/ERP-integration** — afhænger af målsystem
- **Kundeportal-hosting** — hvis adskilt fra backoffice
- **Dataoverførsel (egress)** — Azure opkræver for udgående data, men volumener er små (~1 GB/md. DataHub-trafik)
- **Disaster recovery / geo-redundans** — tilføjer ~50% til databaseomkostninger ved zone-redundant HA

### Pris pr. kunde

| | Lean start | Prod PT1H | Prod PT15M |
|-|------------|-----------|------------|
| Månedlig pris pr. kunde | 0,02 kr. | 0,04 kr. | 0,07 kr. |
| Årlig pris pr. kunde | 0,22 kr. | 0,44 kr. | 0,79 kr. |

Ved disse volumener er infrastrukturomkostningen ubetydelig sammenlignet med forretningsværdien. Databasen er den primære omkostningsdriver, og reserverede instanser reducerer den markant.

---

## Fasevis udrulning

| Fase | Scope | Forretningsprocesser |
|------|-------|----------------------|
| 1 | DataHub-forbindelse + måledataindlæsning | Auth, Queue Poller, RSM-012 (BRS-021), tidsserie-lager |
| 2 | Portefølje + stamdata | RSM-004/007, BRS-001/009/010, Kunde- & Porteføljeservice |
| 3 | Afregningsmotor | BRS-020, BRS-027, afregningsberegninger, faktureringseksport |
| 4 | Fuld livscyklusstyring | BRS-002/003/005/006/011/042/043/044, tilstandsmaskine |
| 5 | Engrosopgørelse + gebyrer | BRS-027/028/029/030, RSM-014/016/017, Charges-kø |
| 6 | PT15M-migrering | Re-partitionér tidsserie-lager, opdatér afregningsmotor, belastningstest ved 7,68M punkter/dag |

---

## Kilder

- [DataHub 3 DDQ Forretningsproces-reference](datahub3-ddq-business-processes.md)
- [RSM-012 Måledata-reference](rsm-012-datahub3-measure-data.md)
- CIM Webservice Interface (Dok. 22/03077-1)
- CIM EDI Guide (Dok. 15/00718-191)
