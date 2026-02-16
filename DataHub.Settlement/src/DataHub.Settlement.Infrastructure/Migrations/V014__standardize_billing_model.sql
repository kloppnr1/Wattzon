-- =============================================================================
-- V014: Standardize billing model
--
-- Simplifies invoice types from 5 to 2 (invoice, credit_note).
-- Removes shadow ledger (billing.aconto_payment table).
-- Cleans up line types: drops aconto_charge, aconto_credit, settlement_difference.
-- Keeps aconto_prepayment, aconto_deduction for standard aconto flow.
-- =============================================================================

-- 1. Normalize existing invoice types → 'invoice' or 'credit_note'
UPDATE billing.invoice
SET invoice_type = 'invoice'
WHERE invoice_type IN ('aconto', 'settlement', 'combined_quarterly', 'final_settlement');

-- 2. Simplify invoice_type CHECK constraint
ALTER TABLE billing.invoice
    DROP CONSTRAINT IF EXISTS invoice_invoice_type_check,
    ADD CONSTRAINT invoice_invoice_type_check
        CHECK (invoice_type IN ('invoice', 'credit_note'));

-- 3. Normalize existing line types
UPDATE billing.invoice_line SET line_type = 'aconto_prepayment' WHERE line_type = 'aconto_charge';
UPDATE billing.invoice_line SET line_type = 'aconto_deduction'  WHERE line_type = 'aconto_credit';

-- 4. Drop settlement_difference lines (if any exist, they should have been settlement lines)
DELETE FROM billing.invoice_line WHERE line_type = 'settlement_difference';

-- 5. Simplify line_type CHECK constraint
ALTER TABLE billing.invoice_line
    DROP CONSTRAINT IF EXISTS invoice_line_line_type_check,
    ADD CONSTRAINT invoice_line_line_type_check
        CHECK (line_type IN (
            'energy', 'grid_tariff', 'system_tariff', 'transmission_tariff',
            'electricity_tax', 'grid_subscription', 'supplier_subscription',
            'aconto_prepayment', 'aconto_deduction', 'vat',
            'production_credit'
        ));

-- 6. Drop the shadow ledger table
DROP TABLE IF EXISTS billing.aconto_payment;

-- 7. Update uniqueness constraint: now that both prepayment and settlement invoices
-- share invoice_type = 'invoice', differentiate by settlement_run_id.
-- Prepayment invoices have settlement_run_id IS NULL; settlement invoices have a value.
DROP INDEX IF EXISTS billing.idx_invoice_unique_period;

CREATE UNIQUE INDEX idx_invoice_unique_settlement
ON billing.invoice (contract_id, period_start, period_end, settlement_run_id)
WHERE status != 'cancelled' AND contract_id IS NOT NULL AND settlement_run_id IS NOT NULL;

CREATE UNIQUE INDEX idx_invoice_unique_prepayment
ON billing.invoice (contract_id, period_start, period_end)
WHERE status != 'cancelled' AND contract_id IS NOT NULL AND settlement_run_id IS NULL
  AND invoice_type = 'invoice';
