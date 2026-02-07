CREATE TABLE metering.metering_data_history (
    id                  BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    metering_point_id   TEXT NOT NULL,
    timestamp           TIMESTAMPTZ NOT NULL,
    previous_kwh        NUMERIC(12,6) NOT NULL,
    new_kwh             NUMERIC(12,6) NOT NULL,
    previous_message_id TEXT,
    new_message_id      TEXT,
    changed_at          TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_metering_data_history_mp_ts
    ON metering.metering_data_history (metering_point_id, timestamp);
