# Pocket Rankings — Tournament

Tournament product for the Pocket Rankings platform: brackets, matches,
and results, isolated from League per
`PocketRankingsPlatform/PLATFORM_AGENTS.md` Section 1.

References League players/teams via the identity/entitlement contract in
PLATFORM_AGENTS.md Section 2 — never by querying League's database
directly.

## Structure

Matches `PoolLeagueWeb`'s layout: the actual project lives under
`src/PocketRankingsTournament/`, with `docs/` and `tests/` at repo root.

## Status

Scaffolding only — no runtime code yet. See `AGENTS.md` for repo-specific
rules; platform-wide rules live in `PocketRankingsPlatform`.
