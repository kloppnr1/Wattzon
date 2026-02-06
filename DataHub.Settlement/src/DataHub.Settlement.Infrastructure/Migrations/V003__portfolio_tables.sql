-- grid_area must be created first (referenced by metering_point)
CREATE TABLE portfolio.grid_area (
    code                TEXT PRIMARY KEY,
    grid_operator_gln   TEXT NOT NULL,
    grid_operator_name  TEXT NOT NULL,
    price_area          TEXT NOT NULL CHECK (price_area IN ('DK1', 'DK2')),
    created_at          TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE portfolio.customer (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name            TEXT NOT NULL,
    cpr_cvr         TEXT NOT NULL,
    contact_type    TEXT NOT NULL CHECK (contact_type IN ('private', 'business')),
    email           TEXT,
    phone           TEXT,
    status          TEXT NOT NULL DEFAULT 'active' CHECK (status IN ('active', 'inactive', 'archived')),
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT now()
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
    binding_period_months   INT,
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

CREATE TABLE portfolio.contract (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    customer_id         UUID NOT NULL REFERENCES portfolio.customer(id),
    gsrn                TEXT NOT NULL REFERENCES portfolio.metering_point(gsrn),
    product_id          UUID NOT NULL REFERENCES portfolio.product(id),
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
