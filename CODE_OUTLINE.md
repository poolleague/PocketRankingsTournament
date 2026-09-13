# Code Outline

- `Models/TournamentModels.cs` — product vocabulary and public view models.
- `Services/BracketBuilder.cs` — deterministic single/double elimination draw
  topology. Result mutation and slot filling remain a later approved phase.
- `Security/TournamentAuthorization.cs` — Tournament-local roles, policies,
  and fail-closed entitlement checks.
- `Services/TournamentStore.cs` — PostgreSQL event/lifecycle/audit persistence
  plus the explicitly Development-only fictional store.
- `Services/PostgresSchemaInitializer.cs` — applies the idempotent checked-in
  migrations in filename order when a Tournament database is configured.
- `Controllers/TournamentsController.cs` — public directory and event detail.
- `Controllers/OrganizerController.cs` — authenticated event creation,
  lifecycle, retained history, and audit views.
- `Controllers/DevelopmentAccessController.cs` — fictional local role testing;
  every route returns 404 outside Development.
- `Views/Tournaments/` — event, live-bracket, participant, and payout views.
- `Views/Organizer/` — responsive organizer dashboard and workflow.
- `Database/001_initial_schema.sql` — authoritative foundation schema.
- `Database/002_administration_history.sql` — accounts/roles, event
  assignments, replay evidence, lifecycle, draw/result revisions, and enforced
  append-only audit behavior.
- `docs/AUTHORIZATION_AND_HISTORY.md` — durable role, entitlement, lifecycle,
  retention, correction, privacy, and recovery decisions.
