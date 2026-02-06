CREATE TABLE datahub.inbound_message (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    datahub_message_id  TEXT NOT NULL,
    message_type        TEXT NOT NULL,
    correlation_id      TEXT,
    queue_name          TEXT NOT NULL,
    status              TEXT NOT NULL DEFAULT 'received'
                        CHECK (status IN ('received', 'parsed', 'processed', 'dead_lettered')),
    raw_payload_size    INT,
    error_details       TEXT,
    received_at         TIMESTAMPTZ NOT NULL DEFAULT now(),
    processed_at        TIMESTAMPTZ
);

CREATE INDEX idx_inbound_message_datahub_id ON datahub.inbound_message (datahub_message_id);
CREATE INDEX idx_inbound_message_status ON datahub.inbound_message (status)
    WHERE status NOT IN ('processed');
CREATE INDEX idx_inbound_message_received ON datahub.inbound_message (received_at DESC);

CREATE TABLE datahub.processed_message_id (
    message_id      TEXT PRIMARY KEY,
    processed_at    TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE datahub.dead_letter (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    original_message_id TEXT,
    queue_name          TEXT NOT NULL,
    error_reason        TEXT NOT NULL,
    raw_payload         JSONB,
    failed_at           TIMESTAMPTZ NOT NULL DEFAULT now(),
    resolved            BOOLEAN NOT NULL DEFAULT false,
    resolved_at         TIMESTAMPTZ,
    resolved_by         TEXT
);

CREATE INDEX idx_dead_letter_unresolved ON datahub.dead_letter (failed_at DESC)
    WHERE NOT resolved;

CREATE TABLE datahub.outbound_request (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    process_type        TEXT NOT NULL,
    gsrn                TEXT,
    status              TEXT NOT NULL DEFAULT 'sent'
                        CHECK (status IN ('sent', 'acknowledged_ok', 'acknowledged_error', 'timed_out')),
    correlation_id      TEXT,
    sent_at             TIMESTAMPTZ NOT NULL DEFAULT now(),
    response_at         TIMESTAMPTZ,
    error_details       TEXT
);

CREATE INDEX idx_outbound_request_correlation ON datahub.outbound_request (correlation_id)
    WHERE correlation_id IS NOT NULL;
CREATE INDEX idx_outbound_request_pending ON datahub.outbound_request (status)
    WHERE status = 'sent';
