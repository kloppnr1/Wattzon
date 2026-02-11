-- Add currency column to aconto_payment table (defaults to DKK for Danish market)
ALTER TABLE billing.aconto_payment
    ADD COLUMN IF NOT EXISTS currency TEXT NOT NULL DEFAULT 'DKK';
