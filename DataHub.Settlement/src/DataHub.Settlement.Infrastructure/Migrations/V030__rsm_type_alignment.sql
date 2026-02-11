-- V030: Align RSM message types with official Energinet BRS specification
-- RSM-009 → RSM-001 (request/response acknowledgement)
-- RSM-007 → RSM-022 (master data / målepunktsstamdata)
-- RSM-003 → RSM-024 (cancellation / annullering)

-- Add business_reason_code to track the official Energinet reason code per message
ALTER TABLE datahub.outbound_request ADD COLUMN IF NOT EXISTS business_reason_code TEXT;
ALTER TABLE datahub.inbound_message ADD COLUMN IF NOT EXISTS business_reason_code TEXT;

-- Rename RSM message types in existing inbound data
UPDATE datahub.inbound_message SET message_type = 'RSM-001' WHERE message_type = 'RSM-009';
UPDATE datahub.inbound_message SET message_type = 'RSM-022' WHERE message_type = 'RSM-007';

-- Rename RSM message types in existing outbound data
UPDATE datahub.outbound_request SET process_type = 'RSM-024' WHERE process_type = 'RSM-003';
