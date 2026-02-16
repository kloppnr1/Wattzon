-- Add 'aconto_prepayment' line type for aconto invoicing
ALTER TABLE billing.invoice_line
    DROP CONSTRAINT IF EXISTS invoice_line_line_type_check,
    ADD CONSTRAINT invoice_line_line_type_check
        CHECK (line_type IN (
            'energy', 'grid_tariff', 'system_tariff', 'transmission_tariff',
            'electricity_tax', 'grid_subscription', 'supplier_subscription',
            'aconto_charge', 'aconto_credit', 'aconto_deduction', 'aconto_prepayment',
            'settlement_difference', 'vat'
        ));
