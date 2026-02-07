CREATE TABLE settlement.correction_settlement (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    metering_point_id       TEXT NOT NULL,
    period_start            DATE NOT NULL,
    period_end              DATE NOT NULL,
    original_run_id         UUID REFERENCES settlement.settlement_run(id),
    delta_kwh               NUMERIC(12,6) NOT NULL,
    charge_type             TEXT NOT NULL,
    delta_amount            NUMERIC(12,2) NOT NULL,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_correction_settlement_mp
    ON settlement.correction_settlement (metering_point_id);

CREATE INDEX idx_correction_settlement_period
    ON settlement.correction_settlement (metering_point_id, period_start, period_end);
