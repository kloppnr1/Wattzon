CREATE TABLE metering.metering_data (
    metering_point_id   TEXT NOT NULL,
    timestamp           TIMESTAMPTZ NOT NULL,
    resolution          TEXT NOT NULL CHECK (resolution IN ('PT15M', 'PT1H', 'P1M')),
    quantity_kwh        NUMERIC(12,3) NOT NULL,
    quality_code        TEXT NOT NULL CHECK (quality_code IN ('A01', 'A02', 'A03', 'A06')),
    source_message_id   TEXT NOT NULL,
    received_at         TIMESTAMPTZ NOT NULL DEFAULT now(),

    PRIMARY KEY (metering_point_id, timestamp)
);

CREATE INDEX idx_metering_data_source ON metering.metering_data (source_message_id);

CREATE TABLE metering.spot_price (
    price_area      TEXT NOT NULL CHECK (price_area IN ('DK1', 'DK2')),
    hour            TIMESTAMPTZ NOT NULL,
    price_per_kwh   NUMERIC(10,6) NOT NULL,
    source          TEXT,
    fetched_at      TIMESTAMPTZ NOT NULL DEFAULT now(),

    PRIMARY KEY (price_area, hour)
);
