ALTER TABLE metering.metering_data SET (
    timescaledb.compress,
    timescaledb.compress_segmentby = 'metering_point_id',
    timescaledb.compress_orderby = 'timestamp'
);

SELECT add_compression_policy('metering.metering_data', INTERVAL '3 months');

SELECT add_retention_policy('metering.metering_data', INTERVAL '5 years');
