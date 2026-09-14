# Current Release Handoff

Last verified: 2026-09-13 (America/New_York)

## Active foundation

- Repository: `poolleague/PocketRankingsTournament`
- Branch: `codex/tournament-foundation`
- Competition-operations phase baseline: `e5907b8`
- Owner approved the shared Platform rules and the initial Tournament
  foundation on 2026-09-13.
- Owner approved the Tournament-only administration/history phase on
  2026-09-13 and explicitly prohibited changes to PoolLeagueWeb or other
  product repositories.
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
- Tournament-local Owner, Tournament Director, and Scorekeeper policies;
  responsive organizer dashboard; private draft creation; one-way lifecycle;
  completed/archived history; and append-only audit evidence.
- PostgreSQL-backed event creation and lifecycle/audit reads and writes, with a
  fictional in-memory store only when no connection exists in Development.
- Additive administration/history schema covering local accounts/roles,
  per-event assignments, consumed handoffs, status history, draw revisions,
  result revisions, and database-enforced audit immutability.
- Real Account token consumption, organizer invitations, live cross-product
  transport, real payments, DNS, SB, and Production remain deferred and require
  their own approvals. Production administration fails closed meanwhile.
- Owner approved the Tournament-only competition-operations phase on
  2026-09-13, including commit/push, while confirming Tournament DNS has not
  been updated and must remain out of scope.
- Competition setup, local registration/waitlist/check-in/withdrawal state,
  venue table inventory/assignment, seeded/randomized/manual draw publication,
  score/forfeit/no-show recording, optimistic result versions, and winner/loser
  bracket advancement are implemented for single- and double-elimination.
  Later scheduling engines remain explicit roadmap work.
- Delegated Tournament Director and Scorekeeper access now requires an exact
  event assignment; scorekeepers additionally require a stable match-key
  assignment. Only marked fictional Development identities accept local
  all-event/all-match assignments.
- Additive migration `003_competition_operations.sql` retains draw-stage
  revisions and adds current-draw, bracket-key, strategy, outcome, and entrant
  integrity contracts.

## Current validation

- Release build: PASS, zero warnings/errors.
- Automated tests: PASS, 36/36.
- Docker Compose configuration: PASS with a local placeholder supplied only to
  the validation process.
- Browser: public directory and bracket render; no browser console errors;
  390px layout has no horizontal document overflow; phone menu opens and closes.
- Local administration runtime: anonymous redirect PASS; Owner create and full
  draft-to-archive history PASS; Scorekeeper create denial PASS; retained five
  status transitions and five matching audit entries PASS.
- Security runtime: missing-CSRF mutation rejected with 400; private draft UUID
  returned 404 publicly; CSP, no-referrer, no-sniff, and organizer no-store
  response headers present.
- 390px Development role-selection and organizer dashboard layouts: PASS by
  rendered browser inspection; organizer document width remains within the
  viewport and the browser console has no errors.
- PostgreSQL container/schema execution: unavailable because the local Docker
  engine did not become ready. Do not treat static schema/Compose validation as
  an executed migration.
- Competition operations: automated create/register/publish/result/advance,
  stale-version refusal, correction boundary, registration lock, check-in field
  filtering, table assignment, completion prerequisites, and exact event/match
  assignment checks PASS.
- Updated organizer browser: competition setup, four-entrant registration,
  draw publication, table inventory, score/table controls, audit evidence, and
  corrected draw-version label PASS. The 390x844 organizer view has no
  horizontal document overflow (`390` viewport / `375` document), and the
  browser console reports no errors.

## Product intent

Serve amateur leagues, rooms, bars, clubs, fundraisers, and community events.
Keep the Pocket Rankings family resemblance while optimizing Tournament for
venue operations and player phone use. See `COMPETITIVE_RECON.md`.
