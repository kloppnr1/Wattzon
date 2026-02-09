-- Support quarter-hour (15-minute) spot price resolution from Energi Data Service.
-- The DayAheadPrices dataset provides PT15M prices from October 2025 onward.

ALTER TABLE metering.spot_price RENAME COLUMN hour TO "timestamp";

ALTER TABLE metering.spot_price
    ADD COLUMN resolution TEXT NOT NULL DEFAULT 'PT1H'
        CHECK (resolution IN ('PT15M', 'PT1H'));

-- Drop the old default after backfilling existing rows
ALTER TABLE metering.spot_price ALTER COLUMN resolution DROP DEFAULT;

COMMENT ON COLUMN metering.spot_price."timestamp"
    IS 'Start of the price interval (UTC). Hourly or quarter-hourly depending on resolution.';
COMMENT ON COLUMN metering.spot_price.resolution
    IS 'ISO 8601 duration: PT1H (hourly, pre-Oct 2025) or PT15M (quarter-hourly, post-Oct 2025).';
