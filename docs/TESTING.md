# Testing

Run the checks from the repository root:

```powershell
dotnet build PocketRankingsTournament.slnx -c Release
dotnet test PocketRankingsTournament.slnx -c Release --no-build
```

The 36-test-case suite covers bracket validation, balanced seeding, byes, winner
and loser routing, final/reset shape, unsupported-format refusal, directory
classification, readable labels, the role matrix, fail-closed entitlement
claims, one-way lifecycle transitions, retained actor/reason evidence, schema
inventory, private-draft non-disclosure, daylight-saving boundary rejection,
Production denial of the fictional login, and database-enforced audit
immutability, competition creation, registration locking, manual-draw
validation, draw revisions, score advancement, optimistic concurrency,
correction limits, live-start/completion prerequisites, waitlist/check-in filtering, table assignment, and exact
event/match assignment behavior.

Runtime checks must additionally verify anonymous organizer denial, Owner and
Tournament Director mutation access, Scorekeeper mutation denial, CSRF failure,
private-draft non-disclosure, a complete draft-to-archive history flow, and
desktop/390px layouts. Docker validation must apply all three PostgreSQL migrations
twice, verify constraints/triggers/indexes, call `/health`, and prove database
state survives an application-container restart before deployment review.
The operational walkthrough must create a competition, register at least four
fictional entrants, publish a draw, record one result, confirm downstream
advancement, and confirm a stale version is rejected.
