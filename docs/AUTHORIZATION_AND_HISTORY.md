# Authorization, Entitlement, And History Contract

Version: Tournament 0.3.0 · 2026-09-13

## Product boundary

Tournament owns its local roles, event assignments, sessions, competitive
records, and audit evidence. It never stores a password or queries Account's
database. Account remains the owner of `PersonId`, authentication, and the
Tournament entitlement. PoolLeagueWeb is neither a dependency nor a data
source for this workflow.

Version 0.3.0 intentionally implements no Production login route. Missing
Account configuration therefore leaves public brackets/results operational
while every organizer policy fails closed. Development-only role selection
uses fictional identities and is unreachable outside Development.

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
Complete events remain on the public history surface; Archived events remain
available to authorized organizers.

## History and correction policy

- Published tournaments are never hard-deleted by an organizer workflow.
- Event status changes append `tourn.event_status_history` rows.
- Every published draw appends a complete JSON snapshot in
  `tourn.draw_revisions`; resetting a draw creates another revision.
- Every score/result write appends `tourn.match_result_revisions` before the
  current match projection changes. Corrections require a reason and matching
  expected version. The ordinary correction path may fix a score while keeping
  the same winner; winner reversal requires a separately designed downstream
  resolution workflow so later matches cannot be silently invalidated.
- Authenticated mutations append redacted `audit.entries` evidence. A database
  trigger rejects update or delete of audit rows.
- Version 0.3.0 performs no automatic history purge. A later approved privacy
  phase may unlink or anonymize identity while preserving the factual event,
  bracket, score, and placement record.

This is an operational design, not a legal retention commitment. Any fixed
retention period, customer export/deletion promise, or jurisdiction-specific
privacy policy requires a separately approved legal/product decision.

## Recovery

The `002_administration_history.sql` and
`003_competition_operations.sql` migrations are additive and idempotent.
Forward recovery is preferred after a failed application rollout. Before any
deployed schema change, preserve a named protected backup and exact application
image. Never roll back by dropping history tables or deleting the product-local
volume.
