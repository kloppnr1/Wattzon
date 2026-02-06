CREATE TABLE tariff.grid_tariff (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    grid_area_code      TEXT NOT NULL REFERENCES portfolio.grid_area(code),
    charge_owner_id     TEXT NOT NULL,
    tariff_type         TEXT NOT NULL CHECK (tariff_type IN ('grid', 'system', 'transmission')),
    charge_type_code    TEXT,
    valid_from          DATE NOT NULL,
    valid_to            DATE,
    source_message_id   TEXT,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT now(),

    UNIQUE (grid_area_code, tariff_type, valid_from)
);

CREATE INDEX idx_grid_tariff_area_validity ON tariff.grid_tariff (grid_area_code, valid_from, valid_to);

CREATE TABLE tariff.tariff_rate (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    grid_tariff_id  UUID NOT NULL REFERENCES tariff.grid_tariff(id) ON DELETE CASCADE,
    hour_number     INT NOT NULL CHECK (hour_number BETWEEN 1 AND 24),
    price_per_kwh   NUMERIC(10,6) NOT NULL,

    UNIQUE (grid_tariff_id, hour_number)
);

CREATE TABLE tariff.subscription (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    grid_area_code      TEXT REFERENCES portfolio.grid_area(code),
    subscription_type   TEXT NOT NULL CHECK (subscription_type IN ('grid', 'supplier')),
    amount_kr_per_month NUMERIC(10,2) NOT NULL,
    valid_from          DATE NOT NULL,
    valid_to            DATE,
    source_message_id   TEXT,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_subscription_area ON tariff.subscription (grid_area_code, valid_from);

CREATE TABLE tariff.electricity_tax (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    rate_per_kwh    NUMERIC(10,6) NOT NULL,
    valid_from      DATE NOT NULL,
    valid_to        DATE,
    description     TEXT,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),

    UNIQUE (valid_from)
);
