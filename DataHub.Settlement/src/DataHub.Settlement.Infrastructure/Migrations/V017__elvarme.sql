-- Add electrical heating flag to metering_point
ALTER TABLE portfolio.metering_point
    ADD COLUMN IF NOT EXISTS electrical_heating BOOLEAN NOT NULL DEFAULT false;

-- Track cumulative annual consumption per metering point for elvarme threshold
CREATE TABLE metering.annual_consumption_tracker (
    metering_point_id   TEXT NOT NULL,
    year                INT NOT NULL,
    cumulative_kwh      NUMERIC(12,6) NOT NULL DEFAULT 0,
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (metering_point_id, year)
);
