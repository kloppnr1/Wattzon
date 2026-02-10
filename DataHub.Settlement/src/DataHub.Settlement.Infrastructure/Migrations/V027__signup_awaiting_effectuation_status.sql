-- Add 'awaiting_effectuation' signup status for when DataHub has acknowledged
-- but the effective date hasn't been reached yet
ALTER TABLE portfolio.signup
    DROP CONSTRAINT IF EXISTS signup_status_check;

ALTER TABLE portfolio.signup
    ADD CONSTRAINT signup_status_check CHECK (status IN (
        'registered', 'processing', 'awaiting_effectuation',
        'active', 'rejected', 'cancelled'
    ));
