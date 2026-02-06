CREATE TABLE lifecycle.process_request (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    process_type            TEXT NOT NULL CHECK (process_type IN (
                                'supplier_switch', 'short_notice_switch', 'move_in',
                                'end_of_supply', 'forced_end_of_supply', 'move_out',
                                'cancel_switch', 'cancel_end_of_supply',
                                'incorrect_switch', 'incorrect_move'
                            )),
    gsrn                    TEXT NOT NULL,
    status                  TEXT NOT NULL DEFAULT 'pending' CHECK (status IN (
                                'pending', 'sent_to_datahub', 'acknowledged', 'rejected',
                                'effectuation_pending', 'completed', 'cancelled'
                            )),
    effective_date          DATE,
    datahub_correlation_id  TEXT,
    requested_at            TIMESTAMPTZ NOT NULL DEFAULT now(),
    completed_at            TIMESTAMPTZ,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_process_request_gsrn ON lifecycle.process_request (gsrn);
CREATE INDEX idx_process_request_status ON lifecycle.process_request (status)
    WHERE status NOT IN ('completed', 'cancelled', 'rejected');
CREATE INDEX idx_process_request_correlation ON lifecycle.process_request (datahub_correlation_id)
    WHERE datahub_correlation_id IS NOT NULL;

CREATE TABLE lifecycle.process_event (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    process_request_id  UUID NOT NULL REFERENCES lifecycle.process_request(id),
    occurred_at         TIMESTAMPTZ NOT NULL DEFAULT now(),
    event_type          TEXT NOT NULL,
    payload             JSONB,
    source              TEXT
);

CREATE INDEX idx_process_event_request ON lifecycle.process_event (process_request_id, occurred_at);
