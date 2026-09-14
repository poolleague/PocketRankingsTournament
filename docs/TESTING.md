# Testing

Run the checks from the repository root:

```powershell
dotnet build PocketRankingsTournament.slnx -c Release
dotnet test PocketRankingsTournament.slnx -c Release --no-build
```

The 57-test-case suite covers bracket validation, balanced seeding, propagated
odd-field byes, winner
and loser routing, final/reset shape, unsupported-format refusal, directory
classification, readable labels, the role matrix, fail-closed entitlement
claims, one-way lifecycle transitions, retained actor/reason evidence, schema
inventory, private-draft non-disclosure, daylight-saving boundary rejection,
Production denial of the fictional login, and database-enforced audit
immutability, competition creation, registration locking, manual-draw
validation, draw revisions, score advancement, optimistic concurrency,
same-winner corrections and director-confirmed downstream resets,
live-start/completion prerequisites, waitlist/check-in filtering, table
assignment, ready/called/in-progress transitions, active-table conflicts,
round-robin pair coverage and standings, eight-player loser routing,
if-needed-final activation, informational payout locking, visibility,
formula-safe exports, explicit forfeit winners, derived completed placements,
and exact event/match assignment behavior, temporary live-code generation and
hashing, one-time disclosure, anonymous private-event viewing, rotation,
revocation, archival shutdown, malformed-code refusal, local QR rendering, and
the PostgreSQL live-link constraint contract.

Runtime checks must additionally verify anonymous organizer denial, Owner and
Tournament Director mutation access, Scorekeeper mutation denial, CSRF failure,
private-draft non-disclosure, a complete draft-to-archive history flow, and
desktop/390px layouts, Help, print styling, and CSV downloads. Docker validation
must apply all five PostgreSQL migrations
twice, verify constraints/triggers/indexes, call `/health`, and prove database
state survives an application-container restart before deployment review.
The operational walkthrough must create a competition, register at least four
fictional entrants, publish a draw, record one result, confirm downstream
advancement, and confirm a stale version is rejected.
For double elimination, run odd and power-of-two fields through completion and
verify the reset final activates only after the elimination-side finalist wins
the first championship match. For round robin, verify every pairing occurs
once and that standings update from confirmed results.
Live-link runtime validation must also prove the raw code is absent from the
database and audit output, old addresses fail after rotation, expired,
revoked, and archived addresses return the same 404, and the activation,
copy, and QR controls are usable by keyboard at desktop and 390px phone widths.

The GitHub workflow repeats Release build, tests, dependency advisories, and
Compose configuration on pushes and pull requests. It is evidence only and
does not deploy or authorize Production.
