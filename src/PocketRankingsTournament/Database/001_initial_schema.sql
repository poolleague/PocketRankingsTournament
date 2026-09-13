BEGIN;

CREATE SCHEMA IF NOT EXISTS core;
CREATE SCHEMA IF NOT EXISTS tourn;
CREATE SCHEMA IF NOT EXISTS integ;
CREATE SCHEMA IF NOT EXISTS audit;

CREATE TABLE IF NOT EXISTS core.installations (
    installation_uuid uuid PRIMARY KEY,
    product_type text NOT NULL DEFAULT 'tournament' CHECK (product_type = 'tournament'),
    display_name text NOT NULL CHECK (length(trim(display_name)) BETWEEN 1 AND 160),
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS tourn.venues (
    venue_id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    public_uuid uuid NOT NULL UNIQUE,
    name text NOT NULL CHECK (length(trim(name)) BETWEEN 1 AND 160),
    locality text NOT NULL DEFAULT '',
    timezone_id text NOT NULL DEFAULT 'America/New_York',
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS tourn.venue_tables (
    venue_table_id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    venue_id bigint NOT NULL REFERENCES tourn.venues(venue_id),
    name text NOT NULL CHECK (length(trim(name)) BETWEEN 1 AND 80),
    sort_order integer NOT NULL CHECK (sort_order >= 0),
    is_active boolean NOT NULL DEFAULT true,
    UNIQUE (venue_id, name),
    UNIQUE (venue_id, sort_order)
);

CREATE TABLE IF NOT EXISTS tourn.events (
    event_id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    public_uuid uuid NOT NULL UNIQUE,
    venue_id bigint REFERENCES tourn.venues(venue_id),
    name text NOT NULL CHECK (length(trim(name)) BETWEEN 1 AND 180),
    description text NOT NULL DEFAULT '',
    starts_at timestamptz NOT NULL,
    ends_at timestamptz,
    registration_closes_at timestamptz,
    status text NOT NULL CHECK (status IN ('draft','registration_open','check_in','in_progress','complete','archived')),
    visibility text NOT NULL DEFAULT 'public' CHECK (visibility IN ('private','unlisted','public')),
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CHECK (ends_at IS NULL OR ends_at >= starts_at),
    CHECK (registration_closes_at IS NULL OR registration_closes_at <= starts_at)
);

CREATE TABLE IF NOT EXISTS tourn.competitions (
    competition_id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    public_uuid uuid NOT NULL UNIQUE,
    event_id bigint NOT NULL REFERENCES tourn.events(event_id),
    name text NOT NULL CHECK (length(trim(name)) BETWEEN 1 AND 160),
    discipline text NOT NULL CHECK (discipline IN ('eight_ball','nine_ball','ten_ball','straight_pool','one_pocket','banks','blackball','custom')),
    custom_discipline_name text,
    entrant_type text NOT NULL CHECK (entrant_type IN ('singles','doubles','scotch_doubles','team')),
    format text NOT NULL CHECK (format IN ('single_elimination','double_elimination','round_robin','swiss','group_to_finals')),
    winners_race_to integer CHECK (winners_race_to > 0),
    losers_race_to integer CHECK (losers_race_to > 0),
    break_format text NOT NULL DEFAULT 'alternating' CHECK (break_format IN ('alternating','winner','loser','custom')),
    uses_handicap boolean NOT NULL DEFAULT false,
    rules_label text NOT NULL DEFAULT '',
    rules_url text,
    bracket_reset_enabled boolean NOT NULL DEFAULT true,
    status text NOT NULL DEFAULT 'draft' CHECK (status IN ('draft','registration_open','drawn','in_progress','complete','archived')),
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CHECK ((discipline = 'custom') = (custom_discipline_name IS NOT NULL))
);

CREATE TABLE IF NOT EXISTS tourn.participants (
    participant_id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    public_uuid uuid NOT NULL UNIQUE,
    display_name text NOT NULL CHECK (length(trim(display_name)) BETWEEN 1 AND 160),
    is_active boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS integ.person_links (
    person_link_id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    participant_id bigint NOT NULL UNIQUE REFERENCES tourn.participants(participant_id),
    person_uuid uuid NOT NULL UNIQUE,
    link_status text NOT NULL CHECK (link_status IN ('pending','verified','revoked')),
    verified_at timestamptz,
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CHECK ((link_status = 'verified') = (verified_at IS NOT NULL))
);

CREATE TABLE IF NOT EXISTS tourn.entrants (
    entrant_id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    public_uuid uuid NOT NULL UNIQUE,
    competition_id bigint NOT NULL REFERENCES tourn.competitions(competition_id),
    display_name text NOT NULL CHECK (length(trim(display_name)) BETWEEN 1 AND 160),
    seed integer CHECK (seed > 0),
    handicap_label text,
    status text NOT NULL DEFAULT 'registered' CHECK (status IN ('waitlisted','registered','checked_in','withdrawn','disqualified')),
    UNIQUE (competition_id, seed)
);

CREATE TABLE IF NOT EXISTS tourn.entrant_members (
    entrant_id bigint NOT NULL REFERENCES tourn.entrants(entrant_id) ON DELETE CASCADE,
    participant_id bigint NOT NULL REFERENCES tourn.participants(participant_id),
    member_order integer NOT NULL CHECK (member_order > 0),
    PRIMARY KEY (entrant_id, participant_id),
    UNIQUE (entrant_id, member_order)
);

CREATE TABLE IF NOT EXISTS tourn.stages (
    stage_id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    public_uuid uuid NOT NULL UNIQUE,
    competition_id bigint NOT NULL REFERENCES tourn.competitions(competition_id),
    name text NOT NULL CHECK (length(trim(name)) BETWEEN 1 AND 120),
    format text NOT NULL CHECK (format IN ('single_elimination','double_elimination','round_robin','swiss')),
    stage_order integer NOT NULL CHECK (stage_order > 0),
    advances_count integer CHECK (advances_count > 0),
    UNIQUE (competition_id, stage_order)
);

CREATE TABLE IF NOT EXISTS tourn.matches (
    match_id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    public_uuid uuid NOT NULL UNIQUE,
    stage_id bigint NOT NULL REFERENCES tourn.stages(stage_id),
    bracket_side text NOT NULL CHECK (bracket_side IN ('winners','elimination','finals','round_robin','swiss')),
    round_number integer NOT NULL CHECK (round_number > 0),
    position integer NOT NULL CHECK (position > 0),
    label text NOT NULL,
    venue_table_id bigint REFERENCES tourn.venue_tables(venue_table_id),
    status text NOT NULL CHECK (status IN ('waiting','ready','called','in_progress','complete','bye','conditional','cancelled')),
    scheduled_at timestamptz,
    called_at timestamptz,
    started_at timestamptz,
    completed_at timestamptz,
    winner_to_match_id bigint REFERENCES tourn.matches(match_id),
    loser_to_match_id bigint REFERENCES tourn.matches(match_id),
    result_version integer NOT NULL DEFAULT 0 CHECK (result_version >= 0),
    UNIQUE (stage_id, bracket_side, round_number, position)
);

CREATE TABLE IF NOT EXISTS tourn.match_entrants (
    match_id bigint NOT NULL REFERENCES tourn.matches(match_id) ON DELETE CASCADE,
    slot smallint NOT NULL CHECK (slot IN (1,2)),
    entrant_id bigint REFERENCES tourn.entrants(entrant_id),
    score integer CHECK (score >= 0),
    is_winner boolean NOT NULL DEFAULT false,
    source_match_id bigint REFERENCES tourn.matches(match_id),
    PRIMARY KEY (match_id, slot)
);

CREATE TABLE IF NOT EXISTS tourn.placements (
    competition_id bigint NOT NULL REFERENCES tourn.competitions(competition_id),
    entrant_id bigint NOT NULL REFERENCES tourn.entrants(entrant_id),
    place integer NOT NULL CHECK (place > 0),
    verified_at timestamptz NOT NULL,
    PRIMARY KEY (competition_id, entrant_id),
    UNIQUE (competition_id, place)
);

CREATE TABLE IF NOT EXISTS tourn.payout_displays (
    payout_display_id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    competition_id bigint NOT NULL REFERENCES tourn.competitions(competition_id),
    place integer NOT NULL CHECK (place > 0),
    label text NOT NULL CHECK (length(trim(label)) BETWEEN 1 AND 100),
    amount numeric(12,2) NOT NULL CHECK (amount >= 0),
    currency_code char(3) NOT NULL DEFAULT 'USD',
    paid_at timestamptz,
    UNIQUE (competition_id, place)
);

CREATE TABLE IF NOT EXISTS audit.entries (
    audit_id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    occurred_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    actor_person_uuid uuid,
    actor_role text NOT NULL,
    action text NOT NULL,
    target_type text NOT NULL,
    target_uuid uuid NOT NULL,
    reason text,
    request_id text NOT NULL,
    source text NOT NULL,
    before_json jsonb,
    after_json jsonb
);

CREATE INDEX IF NOT EXISTS ix_events_status_starts_at ON tourn.events(status, starts_at);
CREATE INDEX IF NOT EXISTS ix_competitions_event ON tourn.competitions(event_id);
CREATE INDEX IF NOT EXISTS ix_entrants_competition_status ON tourn.entrants(competition_id, status);
CREATE INDEX IF NOT EXISTS ix_matches_stage_status ON tourn.matches(stage_id, status);
CREATE UNIQUE INDEX IF NOT EXISTS ux_match_entrants_no_duplicate
    ON tourn.match_entrants(match_id, entrant_id) WHERE entrant_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_audit_target ON audit.entries(target_type, target_uuid, occurred_at DESC);

COMMIT;
