ALTER TABLE lifecycle.process_request
    DROP CONSTRAINT IF EXISTS process_request_process_type_check;

ALTER TABLE lifecycle.process_request
    ADD CONSTRAINT process_request_process_type_check
    CHECK (process_type IN (
        'supplier_switch', 'move_in',
        'end_of_supply', 'move_out',
        'erroneous_switch_reversal'
    ));
