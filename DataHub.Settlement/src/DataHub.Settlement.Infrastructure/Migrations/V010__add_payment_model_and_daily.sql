-- Add payment_model to signup (aconto vs post-payment)
ALTER TABLE portfolio.signup ADD COLUMN payment_model TEXT NOT NULL DEFAULT 'post_payment'
    CHECK (payment_model IN ('aconto', 'post_payment'));

-- Add 'daily' billing frequency
ALTER TABLE portfolio.contract
    DROP CONSTRAINT IF EXISTS contract_billing_frequency_check,
    ADD CONSTRAINT contract_billing_frequency_check
        CHECK (billing_frequency IN ('daily', 'weekly', 'monthly', 'quarterly'));

ALTER TABLE settlement.billing_period
    DROP CONSTRAINT IF EXISTS billing_period_frequency_check,
    ADD CONSTRAINT billing_period_frequency_check
        CHECK (frequency IN ('daily', 'weekly', 'monthly', 'quarterly'));

ALTER TABLE portfolio.signup
    DROP CONSTRAINT IF EXISTS signup_billing_frequency_check,
    ADD CONSTRAINT signup_billing_frequency_check
        CHECK (billing_frequency IN ('daily', 'weekly', 'monthly', 'quarterly'));

-- Add 'aconto_deduction' line type for reconciliation invoices
ALTER TABLE billing.invoice_line
    DROP CONSTRAINT IF EXISTS invoice_line_line_type_check,
    ADD CONSTRAINT invoice_line_line_type_check
        CHECK (line_type IN (
            'energy', 'grid_tariff', 'system_tariff', 'transmission_tariff',
            'electricity_tax', 'grid_subscription', 'supplier_subscription',
            'aconto_charge', 'aconto_credit', 'aconto_deduction', 'settlement_difference', 'vat'
        ));
