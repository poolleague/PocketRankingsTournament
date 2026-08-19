# AGENTS.md — PocketRankingsTournament

This repo inherits every rule in `PLATFORM_AGENTS.md`
(`PocketRankingsPlatform` repo). This file covers only what's specific to
Tournament.

## Scope of this product

- Tournament/bracket structures, matches, and results, in their own
  database, isolated from League and Player Profile.
- References people via `PersonId` and local link rows
  (PLATFORM_AGENTS.md Section 2.1) — never a direct League database read.
- Publishes/consumes cross-product events (e.g. a result affecting a
  player's record) via the outbox/inbox pattern already established in
  League's `Models/LeagueModels.cs`, once that transport is approved for
  live use.

## Explicitly out of scope here

- Owning player identity or entitlement state — that's Account's job.
- Any direct dependency that would make Tournament fail if League is down.

## Status

Scaffolding only. No controllers/services/models implemented yet — real
implementation starts next session, per owner approval, per
PLATFORM_AGENTS.md Section 8 (Approval Boundaries).
