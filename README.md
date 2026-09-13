# Pocket Rankings — Tournament

Tournament product for the Pocket Rankings platform: brackets, matches,
and results, isolated from League per
`PocketRankingsPlatform/PLATFORM_AGENTS.md` Section 1.

Keeps its own local participants and optionally links a person through the
Account-owned `PersonId` contract. It never references League teams or queries
another product's database.

## Structure

Matches `PoolLeagueWeb`'s layout: the actual project lives under
`src/PocketRankingsTournament/`, with `docs/` and `tests/` at repo root.

## Status

The initial application foundation is under active development on
`codex/tournament-foundation`. See `AGENTS.md` for repo-specific rules,
`docs/CURRENT_RELEASE_HANDOFF.md` for current state, and
`docs/COMPETITIVE_RECON.md` for the product research behind the roadmap.
