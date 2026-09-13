# Current Release Handoff

Last verified: 2026-09-13 (America/New_York)

## Active foundation

- Repository: `poolleague/PocketRankingsTournament`
- Branch: `codex/tournament-foundation`
- Baseline: `925c4413445d970c1c5002f66aa41feaefbb4ee2`
- Owner approved the shared Platform rules and the initial Tournament
  foundation on 2026-09-13.
- League is being changed separately by Claude. This repository must not
  modify, switch, clean, or depend on that workspace.

## Approved runtime/schema phase

- ASP.NET Core MVC on .NET 8, PostgreSQL, Docker, and an isolated Compose
  project/network.
- Core event, local participant, optional PersonId-link, registration, bracket,
  match/result, placement, displayed-payout, and append-only audit data.
- Public tournament list/detail, responsive live bracket, results, placements,
  and payout views.
- Extensible discipline and stage vocabulary; single/double elimination are
  the first implemented engines.
- Deterministic fictional development data and no real external delivery.
- Account token consumption, organizer mutation UI, live cross-product
  transport, real payments, DNS, SB, and Production remain deferred and require
  their own approvals.

## Current validation

- Release build: PASS, zero warnings/errors.
- Automated tests: PASS, 12/12.
- Docker Compose configuration: PASS with a local placeholder supplied only to
  the validation process.
- Browser: public directory and bracket render; no browser console errors;
  390px layout has no horizontal document overflow; phone menu opens and closes.
- PostgreSQL container/schema execution: unavailable because the local Docker
  engine did not become ready. Do not treat static schema/Compose validation as
  an executed migration.

## Product intent

Serve amateur leagues, rooms, bars, clubs, fundraisers, and community events.
Keep the Pocket Rankings family resemblance while optimizing Tournament for
venue operations and player phone use. See `COMPETITIVE_RECON.md`.
