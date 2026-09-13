# Testing

Run the foundation checks from the repository root:

```powershell
dotnet build PocketRankingsTournament.slnx -c Release
dotnet test PocketRankingsTournament.slnx -c Release --no-build
```

The initial suite covers bracket validation, balanced seeding, byes, winner
and loser routing, final/reset shape, unsupported-format refusal, directory
classification, and stable owner-facing labels. Docker validation must also
apply the PostgreSQL contract twice, verify constraints/indexes, call `/health`,
and inspect public desktop and phone layouts before this becomes a deployable
candidate.
