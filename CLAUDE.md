# CLAUDE.md — PocketRankingsTournament (Claude Code)

This repo inherits every rule in `PLATFORM_AGENTS.md`
(`PocketRankingsPlatform` repo). This file covers only what's specific to
Tournament, and is kept word-for-word identical to `AGENTS.md` below this
line. The owner approved the shared rules on 2026-09-13.

## Scope of this product

- Tournament configuration, venues/tables, local participants, registration,
  draws, brackets, matches, results, placements, displayed payouts, and
  history, in an isolated per-client database.
- References people via `PersonId` and local link rows
  (PLATFORM_AGENTS.md Section 2.1). Unlinked local participants remain valid
  so community events work without Account, League, or Player Profile.
- Publishes/consumes cross-product events (e.g. a result affecting a
  player's record) via the outbox/inbox pattern already established in
  League's `Models/LeagueModels.cs`, once that transport is approved for
  live use.
- Serves amateur leagues, pool rooms, bars, clubs, fundraisers, and community
  organizers first. Professional-tour broadcast and federation workflows are
  not the product's organizing principle.
- Shares Pocket Rankings visual tokens, component semantics, status language,
  accessibility, and account patterns so the platform feels like one product
  family without sharing runtime state.

## Explicitly out of scope here

- Owning player identity or entitlement state — that's Account's job.
- Any direct dependency that would make Tournament fail if League is down.
- Real-money processing, provider activation, central SSO, and live
  cross-product transport without a separately approved phase.

## Status

The owner approved the governance sync and initial Tournament foundation on
2026-09-13. `docs/CURRENT_RELEASE_HANDOFF.md` is the changing-state source for
the active branch, implementation, validation, and remaining decisions.
