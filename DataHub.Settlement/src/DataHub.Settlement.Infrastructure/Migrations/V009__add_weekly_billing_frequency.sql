-- Add 'weekly' as a supported billing frequency (Danish week: Monday–Sunday)

ALTER TABLE portfolio.contract
    DROP CONSTRAINT IF EXISTS contract_billing_frequency_check,
    ADD CONSTRAINT contract_billing_frequency_check
        CHECK (billing_frequency IN ('weekly', 'monthly', 'quarterly'));

ALTER TABLE settlement.billing_period
    DROP CONSTRAINT IF EXISTS billing_period_frequency_check,
    ADD CONSTRAINT billing_period_frequency_check
        CHECK (frequency IN ('weekly', 'monthly', 'quarterly'));

-- Store the chosen billing frequency on signup so it flows through to contract creation
ALTER TABLE portfolio.signup
    ADD COLUMN billing_frequency TEXT NOT NULL DEFAULT 'monthly'
        CHECK (billing_frequency IN ('weekly', 'monthly', 'quarterly'));
