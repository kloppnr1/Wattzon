# Backoffice Feature Gap Analysis

## Summary

The backoffice currently exposes **12 API endpoints** covering signups, customers, products, and dashboard stats. However, **significant backend capabilities remain unexposed**, particularly around settlement, billing, metering data, and portfolio management.

---

## ✅ Features Currently Exposed

### Signups
- ✅ Create signup (POST /api/signup)
- ✅ View signup list with filters/pagination (GET /api/signups)
- ✅ View signup detail (GET /api/signups/{id})
- ✅ View signup events/timeline (GET /api/signups/{id}/events)
- ✅ Cancel signup (POST /api/signup/{id}/cancel)
- ✅ Check signup status (GET /api/signup/{id}/status)
- ✅ View correction chain (included in detail)

### Customers
- ✅ List customers with pagination/search (GET /api/customers)
- ✅ View customer detail (GET /api/customers/{id})
- ✅ View customer contracts (included in detail)
- ✅ View customer metering points (included in detail)

### Products
- ✅ List active products (GET /api/products)

### Dashboard
- ✅ Dashboard stats (GET /api/dashboard/stats)
- ✅ Recent signups (GET /api/dashboard/recent-signups)

### Address Lookup
- ✅ DAR ID → GSRN resolution (GET /api/address/{darId})

---

## ❌ Missing Features (Backend Capabilities Not Exposed)

### 1. Settlement & Billing (🔴 Critical Gap)

**Backend Capabilities:**
- `InvoicingService` - Standardized billing pipeline (settlement + aconto as invoice lines)
- `SettlementEngine` - Consumption-based settlement with tariffs, spot prices, taxes
- `CorrectionEngine` - Correction settlements for metering data changes
- `ErroneousSwitchService` - Credit handling for supplier errors
- `ReconciliationService` - RSM-014 reconciliation
- `SettlementResultStore` - Stores billing periods, settlement runs, settlement lines
- `AnnualConsumptionTracker` - Elvarme thresholds

**Missing in Backoffice:**
- ❌ View settlement runs (history of billing calculations)
- ❌ View billing periods for a customer
- ❌ View settlement line items (energy, grid, system, transmission, tax breakdown)
- ❌ View aconto payments and balances
- ❌ View final settlement details
- ❌ View correction settlements
- ❌ View erroneous switch credits
- ❌ Trigger manual settlement (admin function)
- ❌ View reconciliation results

**Impact:** Back-office staff cannot see billing details, settlement history, or troubleshoot billing issues. This is a **major operational blind spot**.

---

### 2. Metering Data (🔴 Critical Gap)

**Backend Capabilities:**
- `IMeteringDataRepository` - Stores hourly consumption data
- `GetConsumptionAsync` - Retrieve consumption for date range
- `StoreTimeSeriesWithHistoryAsync` - Store with correction history
- `GetChangesAsync` - View metering data corrections
- Supports production data (E18 - solar)
- History tracking for corrections

**Missing in Backoffice:**
- ❌ View customer consumption data
- ❌ View hourly/daily consumption charts
- ❌ View metering data corrections/history
- ❌ View production data (solar customers)
- ❌ Export consumption data
- ❌ Check data completeness for a period

**Impact:** Cannot troubleshoot consumption-related issues, verify data quality, or show customers their usage.

---

### 3. Spot Prices (🟡 Medium Priority)

**Backend Capabilities:**
- `ISpotPriceRepository` - Stores hourly spot prices per area
- Price history for DK1/DK2

**Missing in Backoffice:**
- ❌ View historical spot prices
- ❌ View current spot prices by area
- ❌ Spot price charts/trends

**Impact:** Cannot explain energy costs to customers or verify price calculations.

---

### 4. Tariffs (🟡 Medium Priority)

**Backend Capabilities:**
- Grid tariffs (transmission, system, electricity tax)
- Split-rate electricity tax (elvarme)
- Historical tariff changes
- Per-grid-area tariffs

**Missing in Backoffice:**
- ❌ View tariff configurations
- ❌ View tariff history/changes
- ❌ View customer's applicable tariffs
- ❌ Admin: Update tariff configurations

**Impact:** Cannot verify tariff calculations or handle customer inquiries about grid fees.

---

### 5. Portfolio Management (🟡 Medium Priority)

**Backend Capabilities (in `IPortfolioRepository`):**
- `CreateMeteringPointAsync`
- `ActivateMeteringPointAsync`
- `DeactivateMeteringPointAsync`
- `CreateContractAsync`
- `EndContractAsync`
- `CreateSupplyPeriodAsync`
- `EndSupplyPeriodAsync`
- `UpdateMeteringPointGridAreaAsync`
- `GetSupplyPeriodsAsync`
- `GetActiveContractAsync`

**Missing in Backoffice:**
- ❌ Manually create contracts (admin)
- ❌ End contracts manually
- ❌ View supply period history per metering point
- ❌ Update metering point grid area
- ❌ View metering point activation/deactivation history

**Impact:** Limited ability to manually fix portfolio issues or handle edge cases.

---

### 6. Process Management (🟡 Medium Priority)

**Backend Capabilities:**
- `ProcessStateMachine` - Full state machine for all process types
- Process events with detailed timeline
- BRS message correlation
- Process filtering by status/type

**Missing in Backoffice:**
- ❌ View all active processes (not just signups)
- ❌ Filter processes by type (onboarding, offboarding, switch, move-in)
- ❌ View processes by status (pending, sent, acknowledged, completed, rejected)
- ❌ Manually retry failed processes (admin)
- ❌ View BRS/RSM message correlation details

**Impact:** Cannot monitor overall process health or troubleshoot stuck processes.

---

### 7. Products (🟢 Low Priority)

**Backend Capabilities:**
- `CreateProductAsync` - Create new products
- Product active/inactive management
- Display ordering

**Missing in Backoffice:**
- ❌ Create new products (admin)
- ❌ Edit product details (admin)
- ❌ Activate/deactivate products (admin)
- ❌ Reorder product display

**Impact:** Minor - products are mostly static configuration.

---

### 8. Customer Management (🟢 Low Priority)

**Backend Capabilities:**
- `CreateCustomerAsync` - Handled automatically (now includes billing address)
- `GetCustomerByCprCvrAsync` - Duplicate detection
- Customer status management
- Billing address storage (street, house number, floor, door, postal code, city)
- Separate payer support (payer entity linked to contract)
- `PUT /api/customers/{id}/billing-address` - Update billing address
- `POST /api/payers` - Create payer
- `GET /api/payers/{id}` - Payer detail

**Missing in Backoffice:**
- ❌ Merge duplicate customers (admin)
- ❌ Update customer details manually (admin)
- ❌ View customer creation source (which signup)
- ❌ Payer management UI (create, edit, link to contract)
- ❌ Billing address edit form in customer detail

**Impact:** Minor - customers and payers are mostly managed automatically via signup flow.

---

### 9. Monitoring & Diagnostics (🔴 Critical Gap)

**Backend Capabilities:**
- Message log (all BRS/RSM messages)
- Process events with full audit trail
- Settlement orchestration status
- Data completeness checks

**Missing in Backoffice:**
- ❌ System health dashboard
- ❌ View message log (BRS/RSM traffic)
- ❌ View settlement orchestration status (which periods settled)
- ❌ View data completeness status per customer
- ❌ View background service status (orchestrator, scheduler, poller)
- ❌ Error logs/alerts

**Impact:** Cannot monitor system health or diagnose integration issues.

---

## 📊 Priority Recommendations

### 🔴 High Priority (Operational Blockers)
1. **Settlement & Billing Views** - Staff cannot troubleshoot billing issues
2. **Metering Data Views** - Cannot verify consumption or troubleshoot data issues
3. **Monitoring Dashboard** - Cannot see system health or diagnose problems

### 🟡 Medium Priority (Quality of Life)
4. **Process Management** - Better visibility into process pipeline
5. **Tariffs & Spot Prices** - Explain costs to customers
6. **Portfolio Management** - Manual interventions for edge cases

### 🟢 Low Priority (Admin Tools)
7. **Product Management** - Admin CRUD for products
8. **Customer Management** - Merge duplicates, manual fixes

---

## 🎯 Recommended Next Steps

### Phase 1: Billing & Settlement Visibility
- Add `/api/billing/periods` - List billing periods
- Add `/api/billing/settlement-runs` - Settlement history
- Add `/api/customers/{id}/billing` - Customer billing detail
- Add `/api/customers/{id}/consumption` - Consumption data with charts
- Create **Billing** page in backoffice showing settlement runs and line items

### Phase 2: Monitoring & Diagnostics
- Add `/api/admin/health` - System health metrics
- Add `/api/admin/processes` - All processes with filters
- Add `/api/admin/messages` - Message log
- Add `/api/admin/orchestration-status` - Settlement orchestration progress
- Create **Admin** section in backoffice for system monitoring

### Phase 3: Portfolio & Process Management
- Add PUT endpoints for manual portfolio operations
- Add process retry/cancel endpoints
- Create **Processes** page showing full pipeline
- Add tariff/spot price views

---

## 💡 Key Insight

**The backoffice has 100% coverage of the exposed API**, but the API only exposes ~20% of the backend's capabilities. The biggest gaps are in **settlement/billing visibility** and **system monitoring** - both critical for operations.
