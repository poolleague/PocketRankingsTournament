# Code Outline

- `Models/TournamentModels.cs` — product vocabulary and public view models.
- `Services/BracketBuilder.cs` — deterministic single/double elimination and
  round-robin topology with balanced seed, bye, and pair rotation handling.
- `Security/TournamentAuthorization.cs` — Tournament-local roles, policies,
  and fail-closed entitlement checks.
- `Services/TournamentStore.cs` — PostgreSQL event/lifecycle/audit persistence
  plus the explicitly Development-only fictional store.
- `Services/CompetitionOperationsStore.cs` — competition setup, local entrant
  registration, versioned draw publication, score/result revision, current
  draw rehydration, floor states, payout display, safe winner reversal, and
  bracket advancement for both stores.
- `Services/TournamentCsvExporter.cs` — formula-safe, privacy-bounded entrant,
  result, standings, and audit exports.
- `Services/TournamentReadinessCheck.cs` — database-aware readiness without
  leaking connection details.
- `Services/PostgresSchemaInitializer.cs` — applies the idempotent checked-in
  migrations in filename order when a Tournament database is configured.
- `Controllers/TournamentsController.cs` — public directory and event detail.
- `Controllers/OrganizerController.cs` — authenticated event creation,
  lifecycle, competition operations, retained history, and audit views.
- `Controllers/DevelopmentAccessController.cs` — fictional local role testing;
  every route returns 404 outside Development.
- `Controllers/HelpController.cs` and `Views/Help/` — product-local operating
  guidance that remains available without another platform product.
- `Views/Tournaments/` — event, live-bracket, participant, and payout views.
- `Views/Organizer/` — responsive organizer dashboard and workflow.
- `Database/001_initial_schema.sql` — authoritative foundation schema.
- `Database/002_administration_history.sql` — accounts/roles, event
  assignments, replay evidence, lifecycle, draw/result revisions, and enforced
  append-only audit behavior.
- `Database/003_competition_operations.sql` — current draw pointers, retained
  stage revisions, stable bracket keys, draw strategy, result outcome, and
  active-entrant integrity indexes.
- `Database/004_launch_operations.sql` — retained result revision kinds for
  downstream-reset evidence.
- `.github/workflows/validate.yml` — bounded Release build, test, advisory, and
  isolated Compose validation.
- `docs/AUTHORIZATION_AND_HISTORY.md` — durable role, entitlement, lifecycle,
  retention, correction, privacy, and recovery decisions.
- `docs/OPERATIONS.md` and `docs/LAUNCH_CHECKLIST.md` — tournament-day operation
  and the exact pre-launch gate.
