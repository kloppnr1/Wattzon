-- Fix versioning: versions are per metering point per billing period, not per grid area.
-- Also allow multiple completed versions (corrections create version 2, 3, etc.)

-- Drop the old grid-area-scoped unique constraint
ALTER TABLE settlement.settlement_run
    DROP CONSTRAINT settlement_run_billing_period_id_grid_area_code_version_key;

-- Drop the index that only allows one completed run per metering point per period
DROP INDEX settlement.idx_settlement_run_mp_period;

-- Version is per (metering_point_id, billing_period_id)
ALTER TABLE settlement.settlement_run
    ADD CONSTRAINT uq_settlement_run_mp_period_version
    UNIQUE (metering_point_id, billing_period_id, version);
