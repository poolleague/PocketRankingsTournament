# PostgreSQL Database Layout

Version: foundation 0.1.0 · introduced 2026-09-13

Each client installation owns one isolated Tournament database. It shares no
credential, volume, network, or table with League, Account, Player Profile, or
another Tournament client.

| Schema | Responsibility |
|---|---|
| `core` | Installation identity |
| `tourn` | Venues/tables, events, competitions, local participants, entrants, stages, matches, placements, and displayed payouts |
| `integ` | Optional Account-owned `PersonId` links only; no contacts or credentials |
| `audit` | Append-only redacted mutation evidence |

The canonical idempotent contract is
`src/PocketRankingsTournament/Database/001_initial_schema.sql`. Internal
identity columns are database-local `bigint`; public and integration-facing
identifiers are UUIDs. Operational instants use `timestamptz`. A local
participant remains valid without an `integ.person_links` row.

`payout_displays` is informational bookkeeping. The foundation does not hold
funds, execute charges, or claim that a displayed amount was paid unless the
operator later records `paid_at` through an approved audited workflow.
