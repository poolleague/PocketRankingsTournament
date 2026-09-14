BEGIN;

CREATE TABLE IF NOT EXISTS tourn.event_live_links (
    event_live_link_id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    public_uuid uuid NOT NULL UNIQUE,
    event_id bigint NOT NULL REFERENCES tourn.events(event_id),
    token_hash bytea NOT NULL UNIQUE CHECK (octet_length(token_hash) = 32),
    code_hint char(4) NOT NULL CHECK (code_hint ~ '^[23456789ABCDEFGHJKLMNPQRSTUVWXYZ]{4}$'),
    activated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    expires_at timestamptz NOT NULL,
    revoked_at timestamptz,
    CHECK (expires_at > activated_at),
    CHECK (revoked_at IS NULL OR revoked_at >= activated_at)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_event_live_links_one_active
    ON tourn.event_live_links(event_id) WHERE revoked_at IS NULL;
CREATE INDEX IF NOT EXISTS ix_event_live_links_expiry
    ON tourn.event_live_links(expires_at) WHERE revoked_at IS NULL;

INSERT INTO core.schema_migrations (migration_name)
VALUES ('005_live_tournament_links')
ON CONFLICT (migration_name) DO NOTHING;

COMMIT;
