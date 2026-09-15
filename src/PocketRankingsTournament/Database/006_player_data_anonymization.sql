BEGIN;

CREATE SCHEMA IF NOT EXISTS privacy;

CREATE TABLE IF NOT EXISTS privacy.identity_suppressions (
    suppression_hash char(64) PRIMARY KEY,
    first_request_id uuid NOT NULL UNIQUE,
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT ck_identity_suppression_hash CHECK (suppression_hash ~ '^[0-9a-f]{64}$')
);

CREATE TABLE IF NOT EXISTS privacy.anonymization_receipts (
    request_id uuid PRIMARY KEY,
    participants_anonymized integer NOT NULL CHECK (participants_anonymized >= 0),
    completed_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
);

-- Audit remains append-only except inside the single transaction that removes the opted-out person's identifiers.
CREATE OR REPLACE FUNCTION audit.reject_entry_mutation()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF current_setting('pocketrankings.privacy_anonymization', true) = 'on' THEN
        RETURN NEW;
    END IF;
    RAISE EXCEPTION 'Tournament audit entries are append-only';
END;
$$;

CREATE OR REPLACE FUNCTION privacy.reject_receipt_mutation()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'Tournament privacy receipts are append-only';
END;
$$;
DROP TRIGGER IF EXISTS trg_privacy_receipts_append_only ON privacy.anonymization_receipts;
CREATE TRIGGER trg_privacy_receipts_append_only BEFORE UPDATE OR DELETE ON privacy.anonymization_receipts
FOR EACH ROW EXECUTE FUNCTION privacy.reject_receipt_mutation();

INSERT INTO core.schema_migrations (migration_name)
VALUES ('006_player_data_anonymization')
ON CONFLICT (migration_name) DO NOTHING;

COMMIT;
