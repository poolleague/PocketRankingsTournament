# Authorization, Entitlement, And History Contract

Version: Tournament 0.7.0 · 2026-09-15

## Product boundary

Tournament owns its local roles, event assignments, sessions, competitive
records, and audit evidence. It never stores a password or queries Account's
database. Account remains the owner of `PersonId`, authentication, and the
Tournament entitlement. PoolLeagueWeb is neither a dependency nor a data
source for this workflow.

Version 0.4.0 intentionally implements no Production login route. A
non-Development process also refuses to start without its isolated PostgreSQL
connection. With the database configured, public brackets/results remain
operational while every organizer policy fails closed until Account identity
is approved. Development-only role selection uses fictional identities and is
unreachable outside Development.

## Future Account handoff boundary

The later Account phase must provide a short-lived, signed, single-purpose
handoff containing issuer, Tournament audience, target installation, `PersonId`,
token identifier, issue/expiry times, and a current Tournament entitlement.
Tournament must validate all fields and the signature, hash and atomically
consume the token identifier, link `PersonId` only through
`integ.account_person_links`, load local roles/event assignments, and then issue
its own bounded secure cookie. Unknown keys, replay, wrong installation,
missing entitlement, expiry, or Account uncertainty deny organizer access.

The local authorization claims already require `entitlement=active` and an
unexpired entitlement timestamp. Pricing, checkout, entitlement creation,
refunds, and payment providers remain outside this repository.

## Roles

| Role | Installation access | Event access |
|---|---|---|
| Owner | Settings, access administration, and every tournament | Full operation and history |
| Tournament Director | Create tournaments | Operate explicitly assigned tournaments |
| Scorekeeper | View the operations queue | Record results only for explicitly assigned matches in explicitly assigned tournaments |

Public viewers require no account and cannot mutate data. Role revocation is a
new append-only role row plus revocation timestamp, followed by local-session
revocation once real Account sessions exist; rows are not erased to hide who
previously held access.

Delegated identities carry exact event UUID assignments. Scorekeepers also
carry stable match-key assignments. Wildcard assignments are accepted only on
the marked fictional Development identity and are rejected for any other
identity source.

Owners and Tournament Directors may export entrants, current results,
round-robin standings, and redacted audit history for an assigned event.
Scorekeepers cannot use these director-level export routes. Exported cells
neutralize spreadsheet formula prefixes before RFC-style CSV quoting.

## Tournament lifecycle

The allowed one-way paths are:

- Draft → Registration Open or Archived
- Registration Open → Check-in or Archived
- Check-in → In Progress or Archived
- In Progress → Complete
- Complete → Archived
- Archived → no later state

Every transition requires an authenticated authorized actor and a reason.
Draft and Archived events are private even if a public UUID is guessed.
Separately audited visibility is Private, Unlisted, or Public: Private is
organizer-only, Unlisted is readable by direct UUID link but absent from the
directory, and Public is discoverable. Complete Public events remain on the
public history surface; Archived events remain available only to authorized
organizers and cannot be republished.

An authorized Owner or assigned Tournament Director may activate a separate
temporary Live Tournament Link only after registration opens. It grants
anonymous read-only access at `/live/{code}` even when the event is Private or
Unlisted; it never grants organizer access or changes directory visibility.
Codes are random, purpose-bound to one event, stored only as SHA-256 hashes,
shown once, and limited to 1–168 hours. Rotation revokes the earlier address,
manual deactivation ends it immediately, and archival revokes it in the same
lifecycle transaction. Malformed, unknown, expired, revoked, draft, and
archived lookups return the same not-found response.
Live-link responses are marked no-store so a browser or intermediary does not
retain the temporary view after expiry or revocation.

## History and correction policy

- Published tournaments are never hard-deleted by an organizer workflow.
- Event status changes append `tourn.event_status_history` rows.
- Every published draw appends a complete JSON snapshot in
  `tourn.draw_revisions`; resetting a draw creates another revision.
- Every score/result write appends `tourn.match_result_revisions` before the
  current match projection changes. Corrections require a reason and matching
  expected version. The ordinary correction path may fix a score while keeping
  the same winner. A winner reversal additionally requires an Owner or
  Tournament Director to confirm the reset; every dependent later result is
  retained as a `downstream_reset` revision, its current projection is cleared,
  unaffected opponents remain in place, and stale score forms are invalidated.
- Authenticated mutations append redacted `audit.entries` evidence. A database
  trigger rejects update or delete of audit rows.
- Version 0.6.0 performs no automatic customer-history purge. Player opt-out now
  anonymizes linked participant identity while retaining competition history; customer
  cancellation follows the separate 61-day platform lifecycle.

This is an operational design, not legal advice. The 61-day product lifecycle is
owner-approved; customer notices and jurisdiction-specific obligations still require
formal legal/privacy review before launch.

## Recovery

The `002_administration_history.sql`, `003_competition_operations.sql`, and
`004_launch_operations.sql`, and `005_live_tournament_links.sql` migrations
are additive and idempotent.
Forward recovery is preferred after a failed application rollout. Before any
deployed schema change, preserve a named protected backup and exact application
image. Never roll back by dropping history tables or deleting the product-local
volume.
