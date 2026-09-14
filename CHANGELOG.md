# Changelog

## 0.5.0 — Unreleased

- Added auditable temporary Live Tournament Links with cryptographically random
  ten-character addresses, one-way SHA-256 storage, bounded expiry, rotation,
  manual revocation, and automatic archival revocation.
- Added anonymous read-only `/live/{code}` tournament views without changing
  permanent visibility or directory discovery.
- Added one-time organizer copy controls and self-contained local SVG QR codes;
  no external QR service receives a Tournament address.
- Added migration `005_live_tournament_links.sql`, focused lifecycle/security
  tests, and synchronized Help, operations, schema, deployment, and launch
  documentation.
- League, Account, DNS, Sandbox, Production, secrets, tags, and releases remain
  unchanged.

## 0.4.0 — Unreleased

- Added single-cycle round-robin scheduling for even and odd fields with live,
  deterministic standings and CSV export.
- Added ready/called/in-progress floor controls, assigned-table requirements,
  and prevention of concurrent active matches on one physical table.
- Corrected larger-field double-elimination loser routing, propagated odd-field
  byes through resolved paths, and activated the if-needed final only when the
  elimination-side finalist wins the first championship match.
- Added director-confirmed winner reversals that reset only dependent later
  matches, retain superseded result versions, and invalidate stale score forms.
- Added private/unlisted/public visibility controls, informational payout
  administration, printable public views, formula-safe entrant/result/
  standings/audit CSV exports, and an in-product tournament-day Help guide.
- Added fail-closed Production database configuration, database-aware readiness,
  process liveness, loopback-only Compose publishing, service restart policies,
  and a bounded GitHub validation workflow.
- Added migration `004_launch_operations.sql` to distinguish retained
  downstream-reset revisions from confirmed result revisions.
- DNS, external Account integration, live payments, Sandbox, Production,
  release tags, and GitHub Releases remain unchanged.

## 0.3.0 — Unreleased

- Added organizer competition setup for pool discipline, entrant type,
  single/double-elimination format, race lengths, handicap use, rules, and an
  optional double-elimination final reset.
- Added local registration, waitlist/check-in/withdrawal status, venue-table
  inventory and assignment, plus seeded, randomized, and manual draw
  publication with immutable revision snapshots.
- Added score, forfeit, and no-show recording with optimistic versions,
  append-only result corrections, and transactional bracket advancement.
- Added exact per-event authorization for delegated roles and per-match scope
  for scorekeepers; only fictional Development identities may use local
  all-event/all-match assignments.
- Added additive PostgreSQL migration `003_competition_operations.sql`,
  organizer/mobile UI, and focused operational/schema/security tests.
- Refreshed the test runner and coverage packages after the advisory scan found
  legacy vulnerable transitive libraries; the final graph reports none.
- DNS, external Account integration, subscriptions, payments, Sandbox, and
  Production deployment remain deliberately unchanged.

## 0.2.0 — Unreleased

- Added Tournament-local Owner, Tournament Director, and Scorekeeper policies,
  fail-closed entitlement checks, and fictional Development-only access.
- Added private event creation, one-way lifecycle controls, retained history,
  append-only audit evidence, and the administration/history schema.

## 0.1.0 — Unreleased

- Established the owner-approved Tournament and shared-platform working rules.
- Added an ASP.NET Core MVC/.NET 8 application and isolated PostgreSQL/Docker
  foundation.
- Added a responsive Pocket Rankings tournament directory and live bracket
  experience using fictional data.
- Added pool-first discipline, entrant, race, handicap, stage, table, result,
  placement, payout-display, identity-link, and audit vocabulary.
- Added deterministic single- and double-elimination bracket topology with
  balanced seeding, byes, loser routing, and an optional final reset.
- Documented competitive reconnaissance and the amateur/community product
  direction. No payments, external delivery, Account token consumption,
  cross-product transport, DNS, or deployment is included.
