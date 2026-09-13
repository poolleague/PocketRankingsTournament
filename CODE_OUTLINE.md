# Code Outline

- `Models/TournamentModels.cs` — product vocabulary and public view models.
- `Services/BracketBuilder.cs` — deterministic single/double elimination draw
  topology. Result mutation and slot filling remain a later approved phase.
- `Services/TournamentCatalog.cs` — fictional read-only foundation fixture;
  replace with bounded PostgreSQL reads when organizer workflows begin.
- `Services/PostgresSchemaInitializer.cs` — applies the idempotent checked-in
  schema when a Tournament database connection is configured.
- `Controllers/TournamentsController.cs` — public directory and event detail.
- `Views/Tournaments/` — event, live-bracket, participant, and payout views.
- `Database/001_initial_schema.sql` — authoritative foundation schema.
