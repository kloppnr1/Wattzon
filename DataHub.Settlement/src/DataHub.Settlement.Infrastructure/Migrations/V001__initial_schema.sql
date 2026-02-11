-- =============================================================================
-- V001: Initial schema (consolidated from V001–V034)
-- Fresh database — all tables in final state, no ALTER migrations needed
-- =============================================================================

-- ---------------------------------------------------------------------------
-- Extensions
-- ---------------------------------------------------------------------------
CREATE EXTENSION IF NOT EXISTS timescaledb;
CREATE EXTENSION IF NOT EXISTS pgcrypto;

-- ---------------------------------------------------------------------------
-- Schemas
-- ---------------------------------------------------------------------------
CREATE SCHEMA IF NOT EXISTS portfolio;
CREATE SCHEMA IF NOT EXISTS metering;
CREATE SCHEMA IF NOT EXISTS tariff;
CREATE SCHEMA IF NOT EXISTS settlement;
CREATE SCHEMA IF NOT EXISTS lifecycle;
CREATE SCHEMA IF NOT EXISTS datahub;
CREATE SCHEMA IF NOT EXISTS billing;

-- ===========================================================================
-- PORTFOLIO
-- ===========================================================================

CREATE TABLE portfolio.grid_area (
    code                TEXT PRIMARY KEY,
    grid_operator_gln   TEXT NOT NULL,
    grid_operator_name  TEXT NOT NULL,
    price_area          TEXT NOT NULL CHECK (price_area IN ('DK1', 'DK2')),
    created_at          TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE portfolio.customer (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name                TEXT NOT NULL,
    cpr_cvr             TEXT NOT NULL,
    contact_type        TEXT NOT NULL CHECK (contact_type IN ('private', 'business')),
    email               TEXT,
    phone               TEXT,
    status              TEXT NOT NULL DEFAULT 'active' CHECK (status IN ('active', 'inactive', 'archived')),
    billing_street      TEXT,
    billing_house_number TEXT,
    billing_floor       TEXT,
    billing_door        TEXT,
    billing_postal_code TEXT,
    billing_city        TEXT,
    billing_dar_id      TEXT,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_customer_cpr_cvr ON portfolio.customer (cpr_cvr);
CREATE INDEX idx_customer_status ON portfolio.customer (status);

CREATE TABLE portfolio.metering_point (
    gsrn                TEXT PRIMARY KEY,
    type                TEXT NOT NULL CHECK (type IN ('E17', 'E18')),
    settlement_method   TEXT NOT NULL CHECK (settlement_method IN ('flex', 'non_profiled')),
    connection_status   TEXT NOT NULL DEFAULT 'connected'
                        CHECK (connection_status IN ('connected', 'disconnected', 'closed_down')),
    grid_area_code      TEXT NOT NULL REFERENCES portfolio.grid_area(code),
    grid_operator_gln   TEXT NOT NULL,
    price_area          TEXT NOT NULL CHECK (price_area IN ('DK1', 'DK2')),
    electrical_heating  BOOLEAN NOT NULL DEFAULT false,
    linked_gsrn         TEXT,
    metering_point_type TEXT NOT NULL DEFAULT 'E17',
    activated_at        TIMESTAMPTZ,
    deactivated_at      TIMESTAMPTZ,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_metering_point_grid_area ON portfolio.metering_point (grid_area_code);
CREATE INDEX idx_metering_point_status ON portfolio.metering_point (connection_status);

CREATE TABLE portfolio.product (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name                    TEXT NOT NULL,
    energy_model            TEXT NOT NULL CHECK (energy_model IN ('spot', 'fixed_price', 'mixed')),
    margin_ore_per_kwh      NUMERIC(8,4) NOT NULL,
    supplement_ore_per_kwh  NUMERIC(8,4),
    subscription_kr_per_month NUMERIC(10,2) NOT NULL,
    description             TEXT,
    green_energy            BOOLEAN NOT NULL DEFAULT false,
    display_order           INT NOT NULL DEFAULT 0,
    is_active               BOOLEAN NOT NULL DEFAULT true,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE portfolio.supply_period (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    gsrn            TEXT NOT NULL REFERENCES portfolio.metering_point(gsrn),
    start_date      DATE NOT NULL,
    end_date        DATE,
    end_reason      TEXT CHECK (end_reason IN ('supplier_switch', 'move_out', 'non_payment')),
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_supply_period_gsrn ON portfolio.supply_period (gsrn);
CREATE INDEX idx_supply_period_active ON portfolio.supply_period (gsrn) WHERE end_date IS NULL;
CREATE UNIQUE INDEX idx_supply_period_one_active ON portfolio.supply_period (gsrn) WHERE end_date IS NULL;

CREATE TABLE portfolio.payer (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name                TEXT NOT NULL,
    cpr_cvr             TEXT NOT NULL,
    contact_type        TEXT NOT NULL CHECK (contact_type IN ('private', 'business')),
    email               TEXT,
    phone               TEXT,
    billing_street      TEXT,
    billing_house_number TEXT,
    billing_floor       TEXT,
    billing_door        TEXT,
    billing_postal_code TEXT,
    billing_city        TEXT,
    billing_dar_id      TEXT,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_payer_cpr_cvr ON portfolio.payer (cpr_cvr);

CREATE TABLE portfolio.contract (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    customer_id         UUID NOT NULL REFERENCES portfolio.customer(id),
    gsrn                TEXT NOT NULL REFERENCES portfolio.metering_point(gsrn),
    product_id          UUID NOT NULL REFERENCES portfolio.product(id),
    payer_id            UUID REFERENCES portfolio.payer(id),
    billing_frequency   TEXT NOT NULL CHECK (billing_frequency IN ('monthly', 'quarterly')),
    payment_model       TEXT NOT NULL CHECK (payment_model IN ('aconto', 'post_payment')),
    payment_term_days   INT NOT NULL DEFAULT 30,
    start_date          DATE NOT NULL,
    end_date            DATE,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT now(),

    UNIQUE (gsrn, start_date)
);

CREATE INDEX idx_contract_customer ON portfolio.contract (customer_id);
CREATE INDEX idx_contract_gsrn ON portfolio.contract (gsrn);
CREATE INDEX idx_contract_active ON portfolio.contract (gsrn) WHERE end_date IS NULL;

CREATE TABLE portfolio.signup (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    signup_number           TEXT NOT NULL UNIQUE,
    dar_id                  TEXT NOT NULL,
    gsrn                    TEXT NOT NULL,
    customer_id             UUID REFERENCES portfolio.customer(id),
    product_id              UUID NOT NULL REFERENCES portfolio.product(id),
    process_request_id      UUID,  -- FK added after lifecycle.process_request is created
    corrected_from_id       UUID REFERENCES portfolio.signup(id),
    type                    TEXT NOT NULL CHECK (type IN ('switch', 'move_in')),
    effective_date          DATE NOT NULL,
    status                  TEXT NOT NULL DEFAULT 'registered' CHECK (status IN (
                                'registered', 'processing', 'awaiting_effectuation',
                                'active', 'rejected', 'cancelled', 'cancellation_pending'
                            )),
    rejection_reason        TEXT,
    customer_name           TEXT NOT NULL,
    customer_cpr_cvr        TEXT NOT NULL,
    customer_contact_type   TEXT NOT NULL CHECK (customer_contact_type IN ('person', 'company')),
    billing_street          TEXT,
    billing_house_number    TEXT,
    billing_floor           TEXT,
    billing_door            TEXT,
    billing_postal_code     TEXT,
    billing_city            TEXT,
    billing_dar_id          TEXT,
    payer_name              TEXT,
    payer_cpr_cvr           TEXT,
    payer_contact_type      TEXT CHECK (payer_contact_type IS NULL OR payer_contact_type IN ('person', 'company')),
    payer_email             TEXT,
    payer_phone             TEXT,
    payer_billing_street    TEXT,
    payer_billing_house_number TEXT,
    payer_billing_floor     TEXT,
    payer_billing_door      TEXT,
    payer_billing_postal_code TEXT,
    payer_billing_city      TEXT,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_signup_gsrn ON portfolio.signup (gsrn);
CREATE INDEX idx_signup_customer ON portfolio.signup (customer_id);
CREATE INDEX idx_signup_status ON portfolio.signup (status) WHERE status NOT IN ('active', 'rejected', 'cancelled');
CREATE INDEX idx_signup_process ON portfolio.signup (process_request_id) WHERE process_request_id IS NOT NULL;
CREATE INDEX idx_signup_no_customer ON portfolio.signup (id) WHERE customer_id IS NULL AND status IN ('registered', 'processing');
CREATE INDEX idx_signup_corrected_from ON portfolio.signup (corrected_from_id) WHERE corrected_from_id IS NOT NULL;

CREATE SEQUENCE portfolio.signup_number_seq START 1;

CREATE TABLE portfolio.customer_data_staging (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    gsrn            TEXT NOT NULL,
    customer_name   TEXT NOT NULL,
    cpr_cvr         TEXT,
    customer_type   TEXT NOT NULL,
    phone           TEXT,
    email           TEXT,
    correlation_id  TEXT,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_customer_staging_gsrn ON portfolio.customer_data_staging (gsrn);

-- ===========================================================================
-- METERING
-- ===========================================================================

CREATE TABLE metering.metering_data (
    metering_point_id       TEXT NOT NULL,
    timestamp               TIMESTAMPTZ NOT NULL,
    resolution              TEXT NOT NULL CHECK (resolution IN ('PT15M', 'PT1H')),
    quantity_kwh            NUMERIC(12,3) NOT NULL,
    quality_code            TEXT CHECK (quality_code IS NULL OR quality_code IN ('A01', 'A02', 'A03', 'A06')),
    source_message_id       TEXT NOT NULL,
    registration_timestamp  TIMESTAMPTZ,
    received_at             TIMESTAMPTZ NOT NULL DEFAULT now(),

    PRIMARY KEY (metering_point_id, timestamp)
);

CREATE INDEX idx_metering_data_source ON metering.metering_data (source_message_id);

-- TimescaleDB hypertable
SELECT create_hypertable(
    'metering.metering_data',
    by_range('timestamp', INTERVAL '1 month')
);

-- Compression policy
ALTER TABLE metering.metering_data SET (
    timescaledb.compress,
    timescaledb.compress_segmentby = 'metering_point_id',
    timescaledb.compress_orderby = 'timestamp'
);

SELECT add_compression_policy('metering.metering_data', INTERVAL '3 months');

-- Retention policy
SELECT add_retention_policy('metering.metering_data', INTERVAL '5 years');

CREATE TABLE metering.spot_price (
    price_area      TEXT NOT NULL CHECK (price_area IN ('DK1', 'DK2')),
    "timestamp"     TIMESTAMPTZ NOT NULL,
    resolution      TEXT NOT NULL CHECK (resolution IN ('PT15M', 'PT1H')),
    price_per_kwh   NUMERIC(10,6) NOT NULL,
    source          TEXT,
    fetched_at      TIMESTAMPTZ NOT NULL DEFAULT now(),

    PRIMARY KEY (price_area, "timestamp")
);

CREATE TABLE metering.metering_data_history (
    id                  BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    metering_point_id   TEXT NOT NULL,
    timestamp           TIMESTAMPTZ NOT NULL,
    previous_kwh        NUMERIC(12,6) NOT NULL,
    new_kwh             NUMERIC(12,6) NOT NULL,
    previous_message_id TEXT,
    new_message_id      TEXT,
    changed_at          TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_metering_data_history_mp_ts
    ON metering.metering_data_history (metering_point_id, timestamp);

CREATE TABLE metering.annual_consumption_tracker (
    metering_point_id   TEXT NOT NULL,
    year                INT NOT NULL,
    cumulative_kwh      NUMERIC(12,6) NOT NULL DEFAULT 0,
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (metering_point_id, year)
);

-- ===========================================================================
-- TARIFF
-- ===========================================================================

CREATE TABLE tariff.grid_tariff (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    grid_area_code      TEXT NOT NULL REFERENCES portfolio.grid_area(code),
    charge_owner_id     TEXT NOT NULL,
    tariff_type         TEXT NOT NULL CHECK (tariff_type IN ('grid', 'system', 'transmission')),
    charge_type_code    TEXT,
    valid_from          DATE NOT NULL,
    valid_to            DATE,
    source_message_id   TEXT,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT now(),

    UNIQUE (grid_area_code, tariff_type, valid_from)
);

CREATE INDEX idx_grid_tariff_area_validity ON tariff.grid_tariff (grid_area_code, valid_from, valid_to);

CREATE TABLE tariff.tariff_rate (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    grid_tariff_id  UUID NOT NULL REFERENCES tariff.grid_tariff(id) ON DELETE CASCADE,
    hour_number     INT NOT NULL CHECK (hour_number BETWEEN 1 AND 24),
    price_per_kwh   NUMERIC(10,6) NOT NULL,

    UNIQUE (grid_tariff_id, hour_number)
);

CREATE TABLE tariff.subscription (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    grid_area_code      TEXT REFERENCES portfolio.grid_area(code),
    subscription_type   TEXT NOT NULL CHECK (subscription_type IN ('grid', 'supplier')),
    amount_kr_per_month NUMERIC(10,2) NOT NULL,
    valid_from          DATE NOT NULL,
    valid_to            DATE,
    source_message_id   TEXT,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_subscription_area ON tariff.subscription (grid_area_code, valid_from);

CREATE TABLE tariff.electricity_tax (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    rate_per_kwh    NUMERIC(10,6) NOT NULL,
    valid_from      DATE NOT NULL,
    valid_to        DATE,
    description     TEXT,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),

    UNIQUE (valid_from)
);

CREATE TABLE tariff.metering_point_tariff_attachment (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    gsrn            TEXT NOT NULL,
    tariff_id       TEXT NOT NULL,
    tariff_type     TEXT NOT NULL,
    valid_from      DATE NOT NULL,
    valid_to        DATE,
    correlation_id  TEXT,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_tariff_attachment_gsrn ON tariff.metering_point_tariff_attachment (gsrn);

-- ===========================================================================
-- SETTLEMENT
-- ===========================================================================

CREATE TABLE settlement.billing_period (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    period_start    DATE NOT NULL,
    period_end      DATE NOT NULL,
    frequency       TEXT NOT NULL CHECK (frequency IN ('monthly', 'quarterly')),
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),

    UNIQUE (period_start, period_end),
    CHECK (period_end > period_start)
);

CREATE TABLE settlement.settlement_run (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    billing_period_id       UUID NOT NULL REFERENCES settlement.billing_period(id),
    grid_area_code          TEXT,
    version                 INT NOT NULL DEFAULT 1,
    status                  TEXT NOT NULL DEFAULT 'running'
                            CHECK (status IN ('running', 'completed', 'failed')),
    executed_at             TIMESTAMPTZ NOT NULL DEFAULT now(),
    completed_at            TIMESTAMPTZ,
    error_details           TEXT,
    metering_points_count   INT,

    UNIQUE (billing_period_id, grid_area_code, version)
);

CREATE INDEX idx_settlement_run_period ON settlement.settlement_run (billing_period_id);
CREATE INDEX idx_settlement_run_status ON settlement.settlement_run (status) WHERE status = 'running';

CREATE TABLE settlement.settlement_line (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    settlement_run_id   UUID NOT NULL REFERENCES settlement.settlement_run(id) ON DELETE CASCADE,
    metering_point_id   TEXT NOT NULL,
    charge_type         TEXT NOT NULL CHECK (charge_type IN (
                            'energy', 'grid_tariff', 'system_tariff', 'transmission_tariff',
                            'electricity_tax', 'grid_subscription', 'supplier_subscription',
                            'aconto'
                        )),
    total_kwh           NUMERIC(12,3),
    total_amount        NUMERIC(12,2) NOT NULL,
    vat_amount          NUMERIC(12,2) NOT NULL,
    currency            TEXT NOT NULL DEFAULT 'DKK'
);

CREATE INDEX idx_settlement_line_run ON settlement.settlement_line (settlement_run_id);
CREATE INDEX idx_settlement_line_mp ON settlement.settlement_line (metering_point_id);

CREATE TABLE settlement.correction_settlement (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    metering_point_id       TEXT NOT NULL,
    period_start            DATE NOT NULL,
    period_end              DATE NOT NULL,
    original_run_id         UUID REFERENCES settlement.settlement_run(id),
    delta_kwh               NUMERIC(12,6) NOT NULL,
    charge_type             TEXT NOT NULL,
    delta_amount            NUMERIC(12,2) NOT NULL,
    correction_batch_id     UUID NOT NULL DEFAULT gen_random_uuid(),
    trigger_type            TEXT NOT NULL DEFAULT 'manual' CHECK (trigger_type IN ('manual', 'auto')),
    status                  TEXT NOT NULL DEFAULT 'completed' CHECK (status IN ('completed', 'failed')),
    vat_amount              NUMERIC(12,2) NOT NULL DEFAULT 0,
    total_amount            NUMERIC(12,2) NOT NULL DEFAULT 0,
    note                    TEXT,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_correction_settlement_mp ON settlement.correction_settlement (metering_point_id);
CREATE INDEX idx_correction_settlement_period ON settlement.correction_settlement (metering_point_id, period_start, period_end);
CREATE INDEX idx_correction_batch_id ON settlement.correction_settlement (correction_batch_id);
CREATE INDEX idx_correction_original_run ON settlement.correction_settlement (original_run_id) WHERE original_run_id IS NOT NULL;
CREATE INDEX idx_correction_created_at ON settlement.correction_settlement (created_at DESC);

CREATE TABLE settlement.erroneous_switch_reversal (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    metering_point_id       TEXT NOT NULL,
    original_process_id     UUID NOT NULL,
    erroneous_period_start  DATE NOT NULL,
    erroneous_period_end    DATE NOT NULL,
    total_credited          NUMERIC(12,2) NOT NULL,
    reversed_at             TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_erroneous_switch_mp ON settlement.erroneous_switch_reversal (metering_point_id);

-- ===========================================================================
-- LIFECYCLE
-- ===========================================================================

CREATE TABLE lifecycle.process_request (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    process_type            TEXT NOT NULL CHECK (process_type IN (
                                'supplier_switch', 'move_in',
                                'end_of_supply', 'move_out',
                                'erroneous_switch_reversal'
                            )),
    gsrn                    TEXT NOT NULL,
    status                  TEXT NOT NULL DEFAULT 'pending' CHECK (status IN (
                                'pending', 'sent_to_datahub', 'acknowledged', 'rejected',
                                'effectuation_pending', 'completed', 'cancelled',
                                'offboarding', 'final_settled', 'cancellation_pending'
                            )),
    effective_date          DATE,
    datahub_correlation_id  TEXT,
    requested_at            TIMESTAMPTZ NOT NULL DEFAULT now(),
    completed_at            TIMESTAMPTZ,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_process_request_gsrn ON lifecycle.process_request (gsrn);
CREATE INDEX idx_process_request_status ON lifecycle.process_request (status)
    WHERE status NOT IN ('completed', 'cancelled', 'rejected');
CREATE INDEX idx_process_request_correlation ON lifecycle.process_request (datahub_correlation_id)
    WHERE datahub_correlation_id IS NOT NULL;
CREATE UNIQUE INDEX idx_process_request_one_active ON lifecycle.process_request (gsrn)
    WHERE status NOT IN ('completed', 'cancelled', 'rejected', 'final_settled');

-- Add FK from signup to process_request now that the table exists
ALTER TABLE portfolio.signup
    ADD CONSTRAINT fk_signup_process_request
    FOREIGN KEY (process_request_id) REFERENCES lifecycle.process_request(id);

CREATE TABLE lifecycle.process_event (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    process_request_id  UUID NOT NULL REFERENCES lifecycle.process_request(id),
    occurred_at         TIMESTAMPTZ NOT NULL DEFAULT now(),
    event_type          TEXT NOT NULL,
    payload             JSONB,
    source              TEXT
);

CREATE INDEX idx_process_event_request ON lifecycle.process_event (process_request_id, occurred_at);

-- ===========================================================================
-- DATAHUB
-- ===========================================================================

CREATE TABLE datahub.inbound_message (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    datahub_message_id      TEXT NOT NULL,
    message_type            TEXT NOT NULL,
    business_reason_code    TEXT,
    correlation_id          TEXT,
    queue_name              TEXT NOT NULL,
    status                  TEXT NOT NULL DEFAULT 'received'
                            CHECK (status IN ('received', 'parsed', 'processed', 'dead_lettered')),
    raw_payload_size        INT,
    error_details           TEXT,
    received_at             TIMESTAMPTZ NOT NULL DEFAULT now(),
    processed_at            TIMESTAMPTZ
);

CREATE INDEX idx_inbound_message_datahub_id ON datahub.inbound_message (datahub_message_id);
CREATE INDEX idx_inbound_message_status ON datahub.inbound_message (status) WHERE status NOT IN ('processed');
CREATE INDEX idx_inbound_message_received ON datahub.inbound_message (received_at DESC);

CREATE TABLE datahub.processed_message_id (
    message_id      TEXT PRIMARY KEY,
    processed_at    TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE datahub.dead_letter (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    original_message_id TEXT,
    queue_name          TEXT NOT NULL,
    error_reason        TEXT NOT NULL,
    raw_payload         JSONB,
    failed_at           TIMESTAMPTZ NOT NULL DEFAULT now(),
    resolved            BOOLEAN NOT NULL DEFAULT false,
    resolved_at         TIMESTAMPTZ,
    resolved_by         TEXT
);

CREATE INDEX idx_dead_letter_unresolved ON datahub.dead_letter (failed_at DESC) WHERE NOT resolved;

CREATE TABLE datahub.outbound_request (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    process_type            TEXT NOT NULL,
    business_reason_code    TEXT,
    gsrn                    TEXT,
    status                  TEXT NOT NULL DEFAULT 'sent'
                            CHECK (status IN ('sent', 'acknowledged_ok', 'acknowledged_error', 'timed_out')),
    correlation_id          TEXT,
    sent_at                 TIMESTAMPTZ NOT NULL DEFAULT now(),
    response_at             TIMESTAMPTZ,
    error_details           TEXT
);

CREATE INDEX idx_outbound_request_correlation ON datahub.outbound_request (correlation_id) WHERE correlation_id IS NOT NULL;
CREATE INDEX idx_outbound_request_pending ON datahub.outbound_request (status) WHERE status = 'sent';

-- ===========================================================================
-- BILLING
-- ===========================================================================

CREATE TABLE billing.aconto_payment (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    gsrn            TEXT NOT NULL,
    period_start    DATE NOT NULL,
    period_end      DATE NOT NULL,
    amount          NUMERIC(12,2) NOT NULL,
    currency        TEXT NOT NULL DEFAULT 'DKK',
    paid_at         TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),

    CHECK (period_end > period_start)
);

CREATE INDEX idx_aconto_payment_gsrn ON billing.aconto_payment (gsrn);
CREATE INDEX idx_aconto_payment_period ON billing.aconto_payment (gsrn, period_start, period_end);
