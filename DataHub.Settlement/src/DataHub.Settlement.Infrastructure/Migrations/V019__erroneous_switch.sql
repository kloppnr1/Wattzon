CREATE TABLE settlement.erroneous_switch_reversal (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    metering_point_id       TEXT NOT NULL,
    original_process_id     UUID NOT NULL,
    erroneous_period_start  DATE NOT NULL,
    erroneous_period_end    DATE NOT NULL,
    total_credited          NUMERIC(12,2) NOT NULL,
    reversed_at             TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_erroneous_switch_mp
    ON settlement.erroneous_switch_reversal (metering_point_id);
