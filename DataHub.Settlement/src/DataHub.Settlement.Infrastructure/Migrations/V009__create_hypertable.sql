SELECT create_hypertable(
    'metering.metering_data',
    by_range('timestamp', INTERVAL '1 month')
);
