# Changelog

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
