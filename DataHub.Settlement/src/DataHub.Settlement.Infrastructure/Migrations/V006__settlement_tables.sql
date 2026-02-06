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
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    billing_period_id   UUID NOT NULL REFERENCES settlement.billing_period(id),
    grid_area_code      TEXT,
    version             INT NOT NULL DEFAULT 1,
    status              TEXT NOT NULL DEFAULT 'running'
                        CHECK (status IN ('running', 'completed', 'failed')),
    executed_at         TIMESTAMPTZ NOT NULL DEFAULT now(),
    completed_at        TIMESTAMPTZ,
    error_details       TEXT,
    metering_points_count INT,

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
                            'electricity_tax', 'grid_subscription', 'supplier_subscription'
                        )),
    total_kwh           NUMERIC(12,3),
    total_amount        NUMERIC(12,2) NOT NULL,
    vat_amount          NUMERIC(12,2) NOT NULL,
    currency            TEXT NOT NULL DEFAULT 'DKK'
);

CREATE INDEX idx_settlement_line_run ON settlement.settlement_line (settlement_run_id);
CREATE INDEX idx_settlement_line_mp ON settlement.settlement_line (metering_point_id);
