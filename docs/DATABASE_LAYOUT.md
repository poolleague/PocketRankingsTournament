# PostgreSQL Database Layout

Version: competition operations 0.3.0 · updated 2026-09-13

Each client installation owns one isolated Tournament database. It shares no
credential, volume, network, or table with League, Account, Player Profile, or
another Tournament client.

| Schema | Responsibility |
|---|---|
| `core` | Installation identity, schema ledger, local accounts, and product-local roles |
| `tourn` | Venues/tables, events, competitions, local participants, entrants, stages, matches, placements, and displayed payouts |
| `integ` | Optional Account-owned `PersonId` links and one-way-hashed handoff-consumption evidence; no contacts or credentials |
| `audit` | Append-only redacted mutation evidence protected from update/delete by a database trigger |

The canonical idempotent contracts are the ordered SQL files under
`src/PocketRankingsTournament/Database/`. Internal
identity columns are database-local `bigint`; public and integration-facing
identifiers are UUIDs. Operational instants use `timestamptz`. A local
participant remains valid without an `integ.person_links` row.

Migration `002_administration_history.sql` adds local administrative accounts,
Owner/Tournament Director/Scorekeeper roles, per-event assignments, consumed
identity-handoff hashes, event-status history, immutable published-draw
revisions, and match-result revisions. `PersonId` appears only in an integration
link; no Tournament table stores a password, contact, subscription charge, or
payment credential.

Migration `003_competition_operations.sql` adds the current draw revision and
publication instant, retained stage revision numbers, stable bracket keys,
seeded/randomized/manual draw strategy, played/forfeit/no-show outcomes, and
venue-table public UUIDs, match scorekeeper assignments, and indexes that
prevent duplicate active display names or bracket identities.
Publishing a corrected pre-play draw adds another stage set and snapshot; the
old revision remains in PostgreSQL. Result corrections append another revision
and use `result_version` for optimistic concurrency. A recorded winner cannot
be changed by the ordinary correction path because that would invalidate
downstream matches and requires a future explicit resolution workflow.

Published tournaments are completed and then archived rather than deleted.
Draw and result corrections append revisions with actor/reason evidence. There
is no automatic history purge in version 0.3.0; a later privacy-retention policy
may unlink or anonymize identity without erasing competitive results.

`payout_displays` is informational bookkeeping. The foundation does not hold
funds, execute charges, or claim that a displayed amount was paid unless the
operator later records `paid_at` through an approved audited workflow.
