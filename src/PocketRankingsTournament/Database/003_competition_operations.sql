BEGIN;

ALTER TABLE tourn.competitions ADD COLUMN IF NOT EXISTS current_draw_revision integer NOT NULL DEFAULT 0 CHECK (current_draw_revision >= 0);
ALTER TABLE tourn.competitions ADD COLUMN IF NOT EXISTS draw_published_at timestamptz;
ALTER TABLE tourn.stages ADD COLUMN IF NOT EXISTS draw_revision integer NOT NULL DEFAULT 0 CHECK (draw_revision >= 0);
ALTER TABLE tourn.matches ADD COLUMN IF NOT EXISTS bracket_key text;
ALTER TABLE tourn.draw_revisions ADD COLUMN IF NOT EXISTS draw_strategy text NOT NULL DEFAULT 'seeded';
ALTER TABLE tourn.match_result_revisions ADD COLUMN IF NOT EXISTS outcome text NOT NULL DEFAULT 'played';
ALTER TABLE tourn.venue_tables ADD COLUMN IF NOT EXISTS public_uuid uuid;
UPDATE tourn.venue_tables SET public_uuid = md5('tournament-table:' || venue_table_id::text)::uuid WHERE public_uuid IS NULL;
ALTER TABLE tourn.venue_tables ALTER COLUMN public_uuid SET NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS ux_venue_tables_public_uuid ON tourn.venue_tables(public_uuid);

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_draw_revision_strategy') THEN
        ALTER TABLE tourn.draw_revisions ADD CONSTRAINT ck_draw_revision_strategy
            CHECK (draw_strategy IN ('seeded','randomized','manual'));
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_match_result_outcome') THEN
        ALTER TABLE tourn.match_result_revisions ADD CONSTRAINT ck_match_result_outcome
            CHECK (outcome IN ('played','entrant_one_forfeit','entrant_two_forfeit','entrant_one_no_show','entrant_two_no_show'));
    END IF;
END;
$$;

ALTER TABLE tourn.stages DROP CONSTRAINT IF EXISTS stages_competition_id_stage_order_key;
CREATE UNIQUE INDEX IF NOT EXISTS ux_stages_draw_order
    ON tourn.stages(competition_id, draw_revision, stage_order);
CREATE UNIQUE INDEX IF NOT EXISTS ux_matches_bracket_key
    ON tourn.matches(stage_id, bracket_key) WHERE bracket_key IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS ux_active_entrant_display_name
    ON tourn.entrants(competition_id, lower(display_name))
    WHERE status NOT IN ('withdrawn','disqualified');
CREATE INDEX IF NOT EXISTS ix_stages_current_draw
    ON tourn.stages(competition_id, draw_revision);

CREATE TABLE IF NOT EXISTS tourn.match_scorekeeper_assignments (
    match_scorekeeper_assignment_id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    match_id bigint NOT NULL REFERENCES tourn.matches(match_id),
    local_account_id bigint NOT NULL REFERENCES core.local_accounts(local_account_id),
    assigned_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    revoked_at timestamptz
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_active_match_scorekeeper_assignments
    ON tourn.match_scorekeeper_assignments(match_id, local_account_id)
    WHERE revoked_at IS NULL;

INSERT INTO core.schema_migrations (migration_name)
VALUES ('003_competition_operations')
ON CONFLICT (migration_name) DO NOTHING;

COMMIT;
