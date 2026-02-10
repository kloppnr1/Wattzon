-- Add DAR ID for billing addresses (all addresses must reference a DAR ID)
ALTER TABLE portfolio.signup ADD COLUMN IF NOT EXISTS billing_dar_id TEXT;
ALTER TABLE portfolio.customer ADD COLUMN IF NOT EXISTS billing_dar_id TEXT;
ALTER TABLE portfolio.payer ADD COLUMN IF NOT EXISTS billing_dar_id TEXT;
