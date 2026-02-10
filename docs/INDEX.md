# DataHub.Settlement Documentation Index

**Welcome to the DataHub.Settlement documentation.** This index helps you navigate 18 comprehensive documents covering business context, architecture, implementation, and integration.

---

## Quick Navigation

**🚀 New to the project?**
Start with [README.md](../README.md) → [Settlement Overview](1-getting-started/datahub3-settlement-overview.md)

**💼 Want to understand the business?**
[Customer Lifecycle](2-business-context/datahub3-customer-lifecycle.md) → [Product & Billing](2-business-context/datahub3-product-and-billing.md) → [Edge Cases](2-business-context/datahub3-edge-cases.md)

**🔧 Want to understand the code?**
[ARCHITECTURE.md](../DataHub.Settlement/ARCHITECTURE.md) → [Database Model](3-architecture/datahub3-database-model.md) → [Class Diagram](3-architecture/datahub3-class-diagram.md)

**🔌 Need to integrate with DataHub?**
[DDQ Business Processes](4-integration/datahub3-ddq-business-processes.md) → [Authentication & Security](4-integration/datahub3-authentication-security.md)

**📋 Planning work?**
[Next Phase Plan](5-planning/next-phase-plan.md) → [Implementation Status](5-planning/mvp1-implementation-plan.md)

---

## Application Guide

This repository contains **two web applications** with different purposes:

### 🔧 Development Dashboard (Blazor)
- **Port**: localhost:5000
- **For**: Developers testing settlement logic
- **Features**: Simulation, time-travel, CIM message viewer, settlement debugging
- **Status**: Development/testing tool only — NOT for production

### 📊 V Back Office (React)
- **Port**: localhost:5173
- **For**: Customer service staff
- **Features**: Signup management, customer lookup, rejection handling, pipeline monitoring, settlement corrections
- **Status**: Production end-user application

**Documentation**: See [README.md](../README.md) Quick Start for setup instructions.

---

## Documentation Map

```
                     ┌─────────────────────┐
                     │   README.md         │  ← START HERE
                     │ (What & Why)        │
                     └──────────┬──────────┘
                                │
                ┌───────────────┼───────────────┐
                ▼               ▼               ▼
         ┌─────────────┐ ┌──────────────┐ ┌──────────────┐
         │ Settlement  │ │ ARCHITECTURE │ │ Next Phase   │
         │  Overview   │ │     .md      │ │    Plan      │
         │ (Business)  │ │ (Technical)  │ │  (Roadmap)   │
         └──────┬──────┘ └──────┬───────┘ └──────────────┘
                │                │
         ┌──────┴──────┐  ┌─────┴──────┐
         ▼             ▼  ▼            ▼
    Customer      Product  Database   Integration
    Lifecycle     Billing  Model      Protocols
    & Market      & Edge   Diagrams   & Auth
    Rules         Cases    & Classes
```

---

## All Documents by Category

### 0. Start Here
- **[README.md](../README.md)** ⭐ - Project overview, setup instructions, getting started
- **[ARCHITECTURE.md](../DataHub.Settlement/ARCHITECTURE.md)** ⭐ - Current system design, decisions, rationale

### 1. Getting Started
- **[Settlement Overview](1-getting-started/datahub3-settlement-overview.md)** - What the settlement system does, high-level concepts

### 2. Business Context
- **[Customer Lifecycle](2-business-context/datahub3-customer-lifecycle.md)** - Complete journey from onboarding to offboarding
- **[Product & Billing](2-business-context/datahub3-product-and-billing.md)** - Aconto payments, invoices, legal requirements
- **[Edge Cases](2-business-context/datahub3-edge-cases.md)** - Corrections, solar, elvarme, erroneous switches
- **[Market Rules](2-business-context/datahub3-market-rules.md)** - Danish energy market regulations and business rules

### 3. Architecture & Design
- **[Database Model](3-architecture/datahub3-database-model.md)** - Schema design, tables, relationships, TimescaleDB
- **[Class Diagram](3-architecture/datahub3-class-diagram.md)** - Domain model, entities, value objects
- **[Sequence Diagrams](3-architecture/datahub3-sequence-diagrams.md)** - Key process flows and interactions

### 4. DataHub Integration
- **[DDQ Business Processes](4-integration/datahub3-ddq-business-processes.md)** - BRS/RSM message types and workflows
- **[RSM-012 Measure Data](4-integration/rsm-012-datahub3-measure-data.md)** - Metering data format and parsing
- **[Authentication & Security](4-integration/datahub3-authentication-security.md)** - OAuth2, token management, Azure AD
- **[CIS and External Systems](4-integration/datahub3-cis-and-external-systems.md)** - Integration architecture

### 5. Implementation & Planning
- **[Next Phase Plan](5-planning/next-phase-plan.md)** ⭐ - Current development priorities and roadmap
- **[MVP1 Implementation Plan](5-planning/mvp1-implementation-plan.md)** - Original implementation tasks (completed)
- **[Implementation Plan](5-planning/datahub3-implementation-plan.md)** - Phased delivery approach
- **[Proposed Architecture](5-planning/datahub3-proposed-architecture.md)** - Historical design proposal (see ARCHITECTURE.md for current)

---

## Reading Paths

### 👔 Path 1: Business Analyst

**Goal:** Understand what the system does and why

1. [README.md](../README.md) - Get context
2. [Settlement Overview](1-getting-started/datahub3-settlement-overview.md) - Understand settlement concepts
3. [Customer Lifecycle](2-business-context/datahub3-customer-lifecycle.md) - See the complete customer journey
4. [Product & Billing](2-business-context/datahub3-product-and-billing.md) - Learn about aconto and invoices
5. [Edge Cases](2-business-context/datahub3-edge-cases.md) - Understand special scenarios
6. [Market Rules](2-business-context/datahub3-market-rules.md) - Know the regulatory context
7. [DDQ Business Processes](4-integration/datahub3-ddq-business-processes.md) - See how we integrate with DataHub

**Skip:** Architecture and implementation docs (unless curious)

---

### 💻 Path 2: New Developer

**Goal:** Get productive quickly

1. [README.md](../README.md) - Set up your environment
2. [Settlement Overview](1-getting-started/datahub3-settlement-overview.md) - Understand what you're building
3. [ARCHITECTURE.md](../DataHub.Settlement/ARCHITECTURE.md) - Learn design decisions
4. [Database Model](3-architecture/datahub3-database-model.md) - Understand the schema
5. [Class Diagram](3-architecture/datahub3-class-diagram.md) - See the domain model
6. [Customer Lifecycle](2-business-context/datahub3-customer-lifecycle.md) - Business context you need
7. [Next Phase Plan](5-planning/next-phase-plan.md) - See what's being worked on

**Then:** Dive into specific integration or business docs as needed for your tasks

---

### 🏗️ Path 3: Solution Architect

**Goal:** Evaluate design and integration approach

1. [README.md](../README.md) - Project overview
2. [ARCHITECTURE.md](../DataHub.Settlement/ARCHITECTURE.md) - Current design decisions
3. [Database Model](3-architecture/datahub3-database-model.md) - Data architecture
4. [Sequence Diagrams](3-architecture/datahub3-sequence-diagrams.md) - Process flows
5. [DDQ Business Processes](4-integration/datahub3-ddq-business-processes.md) - External integration
6. [Authentication & Security](4-integration/datahub3-authentication-security.md) - Security approach
7. [CIS and External Systems](4-integration/datahub3-cis-and-external-systems.md) - Integration architecture
8. [Implementation Plan](5-planning/datahub3-implementation-plan.md) - Delivery strategy

**Optional:** Business context docs for domain understanding

---

### 🔄 Path 4: Current Team Member (Quick Reference)

**Goal:** Find specific information fast

**For business questions:** → `2-business-context/`
**For technical questions:** → `ARCHITECTURE.md` + `3-architecture/`
**For DataHub integration:** → `4-integration/`
**For current work:** → [Next Phase Plan](5-planning/next-phase-plan.md)

**New feature implementation?**
1. Check relevant business context doc
2. Review architecture and sequence diagrams
3. Check database model for related tables
4. Review similar existing implementations

---

## Document Status Legend

- ⭐ **Essential** - Read these first
- **Current** - Reflects implemented system (most docs)
- **Historical** - Original planning, see newer docs for current state

---

## Tips for Using This Documentation

1. **Start broad, then go deep** - Overview → specifics
2. **Follow cross-references** - Documents link to related content
3. **Check the date** - Most docs are current; planning docs show evolution
4. **Look for examples** - Many docs include concrete scenarios (Golden Masters, invoice samples)
5. **Ask questions** - If something is unclear, the team wants to know

---

## Contributing to Documentation

When updating documentation:
- Keep related concepts together in the same document
- Add cross-references to related docs
- Update this INDEX if you add/remove/reorganize files
- Consider your audience (business vs technical)
- Include examples where helpful

---

*Last updated: 2026-02-08*
