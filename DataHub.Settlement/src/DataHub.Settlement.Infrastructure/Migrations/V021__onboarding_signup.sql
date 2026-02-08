-- V021: Onboarding signup table + product table fixes
-- Track B1: Onboarding API

-- 1. Product table: drop binding_period_months (not allowed in DK electricity market),
--    add columns for product listing API
ALTER TABLE portfolio.product
    DROP COLUMN IF EXISTS binding_period_months,
    ADD COLUMN IF NOT EXISTS description TEXT,
    ADD COLUMN IF NOT EXISTS green_energy BOOLEAN NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS display_order INT NOT NULL DEFAULT 0;

-- 2. Signup table: tracks onboarding from signup to activation
CREATE TABLE portfolio.signup (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    signup_number       TEXT NOT NULL UNIQUE,
    dar_id              TEXT NOT NULL,
    gsrn                TEXT NOT NULL,
    customer_id         UUID NOT NULL REFERENCES portfolio.customer(id),
    product_id          UUID NOT NULL REFERENCES portfolio.product(id),
    process_request_id  UUID REFERENCES lifecycle.process_request(id),
    type                TEXT NOT NULL CHECK (type IN ('switch', 'move_in')),
    effective_date      DATE NOT NULL,
    status              TEXT NOT NULL DEFAULT 'registered' CHECK (status IN (
                            'registered', 'processing', 'active', 'rejected', 'cancelled'
                        )),
    rejection_reason    TEXT,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_signup_gsrn ON portfolio.signup (gsrn);
CREATE INDEX idx_signup_customer ON portfolio.signup (customer_id);
CREATE INDEX idx_signup_status ON portfolio.signup (status)
    WHERE status NOT IN ('active', 'rejected', 'cancelled');
CREATE INDEX idx_signup_process ON portfolio.signup (process_request_id)
    WHERE process_request_id IS NOT NULL;

-- Signup number sequence (year-prefixed: SGN-2026-00001)
CREATE SEQUENCE portfolio.signup_number_seq START 1;
