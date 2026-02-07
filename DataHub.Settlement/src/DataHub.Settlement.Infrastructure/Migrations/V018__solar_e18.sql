-- Link E17 (consumption) and E18 (production) metering points for solar customers
ALTER TABLE portfolio.metering_point
    ADD COLUMN IF NOT EXISTS linked_gsrn TEXT,
    ADD COLUMN IF NOT EXISTS metering_point_type TEXT NOT NULL DEFAULT 'E17';

-- Production data stored in the same metering_data table, keyed by E18 GSRN
-- No additional table needed — the linked_gsrn on E17 points to the E18 GSRN
