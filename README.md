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

The application now includes a public tournament surface plus Tournament-local
administration, retained history, competition setup, entrant registration,
versioned draw publication, score entry, and bracket advancement on
`codex/tournament-foundation`.
Development provides fictional Owner, Tournament Director, and Scorekeeper
identities for permission testing. Production administration deliberately fails
closed until the separate Account identity handoff receives approval; this repo
does not implement or store passwords. See `AGENTS.md` for repo-specific rules,
`docs/CURRENT_RELEASE_HANDOFF.md` for current state, and
`docs/COMPETITIVE_RECON.md` for the product research behind the roadmap.

Single- and double-elimination are the operational bracket engines in version
0.3.0. Round robin, Swiss, and group-to-finals remain modeled roadmap formats;
the application refuses to generate them rather than producing an incorrect
draw. No DNS or deployment has been configured.
