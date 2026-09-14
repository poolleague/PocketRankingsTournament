# PostgreSQL Database Layout

Version: live display links 0.5.0 · updated 2026-09-14

Each client installation owns one isolated Tournament database. It shares no
credential, volume, network, or table with League, Account, Player Profile, or
another Tournament client.

| Schema | Responsibility |
|---|---|
| `core` | Installation identity, schema ledger, local accounts, and product-local roles |
| `tourn` | Venues/tables, events, competitions, local participants, entrants, stages, matches, placements, displayed payouts, and temporary live-link hashes |
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
Version 0.4.0 uses the existing match timestamps for ready/called/in-progress
floor state and the existing `payout_displays` table for pre-draw
informational prizes.
Publishing a corrected pre-play draw adds another stage set and snapshot; the
old revision remains in PostgreSQL. Result corrections append another revision
and use `result_version` for optimistic concurrency. Migration
`004_launch_operations.sql` adds constrained `revision_kind` values:
`recorded` for a confirmed score and `downstream_reset` when a
director-confirmed winner reversal invalidates a dependent result. Reset rows
retain prior evidence while current scores, timestamps, and routed entrants
are rebuilt from the corrected path.

Round-robin standings are derived from current completed match revisions. They
are not stored as a second mutable table, which prevents rank data from
drifting from the authoritative scores.

Migration `005_live_tournament_links.sql` adds `tourn.event_live_links`.
Each row retains a public record UUID, owning event, unique 32-byte SHA-256
code hash, non-secret four-character hint, activation/expiry timestamps, and
optional revocation timestamp. A partial unique index permits only one
non-revoked link per event. The usable ten-character code and QR image are
never stored durably or placed in browser storage; a five-minute process-memory
handoff reveals and renders them only on the immediate organizer response.

Published tournaments are completed and then archived rather than deleted.
Draw and result corrections append revisions with actor/reason evidence. There
is no automatic history purge in version 0.5.0; a later privacy-retention policy
may unlink or anonymize identity without erasing competitive results.

`payout_displays` is informational bookkeeping. The foundation does not hold
funds, execute charges, or claim that a displayed amount was paid unless the
operator later records `paid_at` through an approved audited workflow.
