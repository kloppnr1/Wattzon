-- Add 'weekly' as a supported billing frequency (Danish week: Monday–Sunday)

ALTER TABLE portfolio.contract
    DROP CONSTRAINT IF EXISTS contract_billing_frequency_check,
    ADD CONSTRAINT contract_billing_frequency_check
        CHECK (billing_frequency IN ('weekly', 'monthly', 'quarterly'));

ALTER TABLE settlement.billing_period
    DROP CONSTRAINT IF EXISTS billing_period_frequency_check,
    ADD CONSTRAINT billing_period_frequency_check
        CHECK (frequency IN ('weekly', 'monthly', 'quarterly'));
