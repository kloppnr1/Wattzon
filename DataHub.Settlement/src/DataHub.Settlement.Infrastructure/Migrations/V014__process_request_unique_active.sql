-- Ensure only one active (non-terminal) process per GSRN.
-- This prevents duplicate concurrent processes at the database level.
CREATE UNIQUE INDEX idx_process_request_one_active
ON lifecycle.process_request (gsrn)
WHERE status NOT IN ('completed', 'cancelled', 'rejected', 'final_settled');
