BEGIN;

ALTER TABLE tourn.match_result_revisions
    ADD COLUMN IF NOT EXISTS revision_kind text NOT NULL DEFAULT 'recorded';

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_match_result_revision_kind') THEN
        ALTER TABLE tourn.match_result_revisions
            ADD CONSTRAINT ck_match_result_revision_kind
            CHECK (revision_kind IN ('recorded','downstream_reset'));
    END IF;
END;
$$;

INSERT INTO core.schema_migrations (migration_name)
VALUES ('004_launch_operations')
ON CONFLICT (migration_name) DO NOTHING;

COMMIT;
