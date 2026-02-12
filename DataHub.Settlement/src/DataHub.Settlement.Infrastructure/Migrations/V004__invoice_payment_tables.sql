-- ===========================================================================
-- INVOICING & PAYMENTS
-- ===========================================================================

CREATE TABLE billing.invoice (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    invoice_number      TEXT UNIQUE,
    customer_id         UUID NOT NULL REFERENCES portfolio.customer(id),
    payer_id            UUID REFERENCES portfolio.payer(id),
    contract_id         UUID REFERENCES portfolio.contract(id),
    settlement_run_id   UUID REFERENCES settlement.settlement_run(id),
    billing_period_id   UUID REFERENCES settlement.billing_period(id),
    invoice_type        TEXT NOT NULL
                        CHECK (invoice_type IN ('aconto', 'settlement', 'combined_quarterly', 'credit_note', 'final_settlement')),
    status              TEXT NOT NULL DEFAULT 'draft'
                        CHECK (status IN ('draft', 'sent', 'paid', 'partially_paid', 'overdue', 'cancelled', 'credited')),
    period_start        DATE NOT NULL,
    period_end          DATE NOT NULL,
    total_ex_vat        NUMERIC(12,2) NOT NULL DEFAULT 0,
    vat_amount          NUMERIC(12,2) NOT NULL DEFAULT 0,
    total_incl_vat      NUMERIC(12,2) NOT NULL DEFAULT 0,
    amount_paid         NUMERIC(12,2) NOT NULL DEFAULT 0,
    amount_outstanding  NUMERIC(12,2) NOT NULL DEFAULT 0,
    issued_at           TIMESTAMPTZ,
    due_date            DATE,
    paid_at             TIMESTAMPTZ,
    credited_invoice_id UUID REFERENCES billing.invoice(id),
    notes               TEXT,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT now(),

    CHECK (period_end > period_start)
);

CREATE INDEX idx_invoice_customer ON billing.invoice (customer_id);
CREATE INDEX idx_invoice_status ON billing.invoice (status);
CREATE INDEX idx_invoice_due_date ON billing.invoice (due_date) WHERE status = 'sent';
CREATE INDEX idx_invoice_settlement_run ON billing.invoice (settlement_run_id) WHERE settlement_run_id IS NOT NULL;
CREATE INDEX idx_invoice_number ON billing.invoice (invoice_number) WHERE invoice_number IS NOT NULL;

CREATE TABLE billing.invoice_line (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    invoice_id          UUID NOT NULL REFERENCES billing.invoice(id) ON DELETE CASCADE,
    settlement_line_id  UUID REFERENCES settlement.settlement_line(id),
    gsrn                TEXT NOT NULL,
    sort_order          INT NOT NULL DEFAULT 0,
    line_type           TEXT NOT NULL
                        CHECK (line_type IN (
                            'energy', 'grid_tariff', 'system_tariff', 'transmission_tariff',
                            'electricity_tax', 'grid_subscription', 'supplier_subscription',
                            'aconto_charge', 'aconto_credit', 'settlement_difference', 'vat'
                        )),
    description         TEXT NOT NULL,
    quantity            NUMERIC(12,4),
    unit_price          NUMERIC(12,6),
    amount_ex_vat       NUMERIC(12,2) NOT NULL DEFAULT 0,
    vat_amount          NUMERIC(12,2) NOT NULL DEFAULT 0,
    amount_incl_vat     NUMERIC(12,2) NOT NULL DEFAULT 0,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_invoice_line_invoice ON billing.invoice_line (invoice_id);
CREATE INDEX idx_invoice_line_gsrn ON billing.invoice_line (gsrn);

CREATE TABLE billing.payment (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    customer_id         UUID NOT NULL REFERENCES portfolio.customer(id),
    payment_method      TEXT NOT NULL
                        CHECK (payment_method IN ('bank_transfer', 'direct_debit', 'manual', 'refund')),
    payment_reference   TEXT,
    external_id         TEXT,
    amount              NUMERIC(12,2) NOT NULL,
    amount_allocated    NUMERIC(12,2) NOT NULL DEFAULT 0,
    amount_unallocated  NUMERIC(12,2) NOT NULL DEFAULT 0,
    received_at         TIMESTAMPTZ NOT NULL DEFAULT now(),
    value_date          DATE,
    status              TEXT NOT NULL DEFAULT 'received'
                        CHECK (status IN ('received', 'allocated', 'partially_allocated', 'refunded')),
    created_at          TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_payment_customer ON billing.payment (customer_id);
CREATE INDEX idx_payment_status ON billing.payment (status);
CREATE INDEX idx_payment_reference ON billing.payment (payment_reference) WHERE payment_reference IS NOT NULL;
CREATE INDEX idx_payment_external ON billing.payment (external_id) WHERE external_id IS NOT NULL;

CREATE TABLE billing.payment_allocation (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    payment_id          UUID NOT NULL REFERENCES billing.payment(id),
    invoice_id          UUID NOT NULL REFERENCES billing.invoice(id),
    amount              NUMERIC(12,2) NOT NULL,
    allocated_at        TIMESTAMPTZ NOT NULL DEFAULT now(),
    allocated_by        TEXT
);

CREATE INDEX idx_payment_allocation_payment ON billing.payment_allocation (payment_id);
CREATE INDEX idx_payment_allocation_invoice ON billing.payment_allocation (invoice_id);

-- Sequence for invoice numbering (INV-YYYY-NNNNN)
CREATE SEQUENCE billing.invoice_number_seq START 1;
