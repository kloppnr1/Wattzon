-- Ensure only one active (open-ended) supply period per GSRN.
-- This prevents duplicate onboarding at the database level.
CREATE UNIQUE INDEX idx_supply_period_one_active
ON portfolio.supply_period (gsrn)
WHERE end_date IS NULL;
