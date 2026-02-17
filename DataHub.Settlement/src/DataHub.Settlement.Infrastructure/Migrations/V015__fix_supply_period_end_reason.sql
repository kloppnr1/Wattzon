-- =============================================================================
-- V015: Expand supply_period.end_reason to support all DataHub 3 scenarios
-- 
-- The losing supplier receives RSM-004 notifications when:
--   E03 → another supplier takes over (BRS-001 supplier switch)
--   E01 → move-in by another supplier / move-out (BRS-009/BRS-010)
--   D31 → forced transfer (BRS-044)
--   E20 → end of supply stop
-- 
-- Previous CHECK only allowed: supplier_switch, move_out, non_payment
-- This was blocking all losing-supplier offboarding scenarios.
-- =============================================================================

ALTER TABLE portfolio.supply_period
    DROP CONSTRAINT IF EXISTS supply_period_end_reason_check;

ALTER TABLE portfolio.supply_period
    ADD CONSTRAINT supply_period_end_reason_check
    CHECK (end_reason IN (
        'supplier_switch',          -- BRS-001: we initiated switch (gaining)
        'move_out',                 -- BRS-010: customer moves out
        'non_payment',              -- BRS-002: end of supply due to non-payment
        'stop_of_supply',           -- RSM-004/E03: losing supplier in BRS-001
        'other_supplier_takeover',  -- RSM-004/E01: another supplier's move-in
        'forced_transfer',          -- RSM-004/D31: forced transfer (BRS-044)
        'end_of_supply',            -- BRS-002: general end of supply
        'end_of_supply_stop'        -- RSM-004/E20: end of supply stop notification
    ));
