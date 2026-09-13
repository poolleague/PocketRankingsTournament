BEGIN;

CREATE TABLE IF NOT EXISTS core.schema_migrations (
    migration_name text PRIMARY KEY,
    applied_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS core.local_accounts (
    local_account_id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    public_uuid uuid NOT NULL UNIQUE,
    display_name text NOT NULL CHECK (length(trim(display_name)) BETWEEN 1 AND 160),
    status text NOT NULL DEFAULT 'active' CHECK (status IN ('invited','active','revoked')),
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    revoked_at timestamptz,
    CHECK ((status = 'revoked') = (revoked_at IS NOT NULL))
);

CREATE TABLE IF NOT EXISTS integ.account_person_links (
    account_person_link_id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    local_account_id bigint NOT NULL UNIQUE REFERENCES core.local_accounts(local_account_id),
    person_uuid uuid NOT NULL UNIQUE,
    linked_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    revoked_at timestamptz
);

CREATE TABLE IF NOT EXISTS core.account_roles (
    account_role_id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    local_account_id bigint NOT NULL REFERENCES core.local_accounts(local_account_id),
    role text NOT NULL CHECK (role IN ('owner','tournament_director','scorekeeper')),
    granted_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    revoked_at timestamptz
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_active_account_roles
    ON core.account_roles(local_account_id, role) WHERE revoked_at IS NULL;

CREATE TABLE IF NOT EXISTS tourn.event_role_assignments (
    event_role_assignment_id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    event_id bigint NOT NULL REFERENCES tourn.events(event_id),
    local_account_id bigint NOT NULL REFERENCES core.local_accounts(local_account_id),
    role text NOT NULL CHECK (role IN ('tournament_director','scorekeeper')),
    assigned_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    revoked_at timestamptz
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_active_event_role_assignments
    ON tourn.event_role_assignments(event_id, local_account_id, role) WHERE revoked_at IS NULL;

CREATE TABLE IF NOT EXISTS integ.consumed_identity_handoffs (
    handoff_hash bytea PRIMARY KEY,
    local_account_id bigint REFERENCES core.local_accounts(local_account_id),
    consumed_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    expires_at timestamptz NOT NULL,
    CHECK (expires_at > consumed_at)
);

CREATE TABLE IF NOT EXISTS tourn.event_status_history (
    event_status_history_id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    event_id bigint NOT NULL REFERENCES tourn.events(event_id),
    from_status text NOT NULL CHECK (from_status IN ('draft','registration_open','check_in','in_progress','complete','archived')),
    to_status text NOT NULL CHECK (to_status IN ('draft','registration_open','check_in','in_progress','complete','archived')),
    actor_display_name text NOT NULL CHECK (length(trim(actor_display_name)) BETWEEN 1 AND 160),
    actor_role text NOT NULL CHECK (actor_role IN ('owner','tournament_director','scorekeeper')),
    reason text NOT NULL CHECK (length(trim(reason)) BETWEEN 3 AND 500),
    occurred_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CHECK (from_status <> to_status)
);

CREATE INDEX IF NOT EXISTS ix_event_status_history_event
    ON tourn.event_status_history(event_id, occurred_at DESC);

CREATE TABLE IF NOT EXISTS tourn.draw_revisions (
    draw_revision_id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    competition_id bigint NOT NULL REFERENCES tourn.competitions(competition_id),
    revision integer NOT NULL CHECK (revision > 0),
    draw_json jsonb NOT NULL,
    reason text NOT NULL CHECK (length(trim(reason)) BETWEEN 3 AND 500),
    published_by_account_id bigint REFERENCES core.local_accounts(local_account_id),
    published_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE (competition_id, revision)
);

CREATE TABLE IF NOT EXISTS tourn.match_result_revisions (
    match_result_revision_id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    match_id bigint NOT NULL REFERENCES tourn.matches(match_id),
    revision integer NOT NULL CHECK (revision > 0),
    entrant_one_score integer CHECK (entrant_one_score >= 0),
    entrant_two_score integer CHECK (entrant_two_score >= 0),
    winner_entrant_id bigint REFERENCES tourn.entrants(entrant_id),
    reason text NOT NULL CHECK (length(trim(reason)) BETWEEN 3 AND 500),
    recorded_by_account_id bigint REFERENCES core.local_accounts(local_account_id),
    recorded_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE (match_id, revision)
);

ALTER TABLE audit.entries ADD COLUMN IF NOT EXISTS actor_display_name text;

CREATE OR REPLACE FUNCTION audit.reject_entry_mutation()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'Tournament audit entries are append-only';
END;
$$;

DROP TRIGGER IF EXISTS trg_audit_entries_append_only ON audit.entries;
CREATE TRIGGER trg_audit_entries_append_only
BEFORE UPDATE OR DELETE ON audit.entries
FOR EACH ROW EXECUTE FUNCTION audit.reject_entry_mutation();

INSERT INTO core.schema_migrations (migration_name)
VALUES ('002_administration_history')
ON CONFLICT (migration_name) DO NOTHING;

COMMIT;
