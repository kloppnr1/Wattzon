-- V022: Add correction chain support for rejected signups (Option B)
-- A rejected signup is immutable. Customer service creates a new signup
-- with corrected_from_id pointing to the original rejected signup.

ALTER TABLE portfolio.signup
    ADD COLUMN corrected_from_id UUID REFERENCES portfolio.signup(id);

CREATE INDEX idx_signup_corrected_from ON portfolio.signup (corrected_from_id)
    WHERE corrected_from_id IS NOT NULL;

COMMENT ON COLUMN portfolio.signup.corrected_from_id IS
    'Links to the rejected signup this correction replaces. NULL for original signups.';
