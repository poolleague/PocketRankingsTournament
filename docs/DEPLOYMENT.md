# Deployment Foundation

The checked-in `docker-compose.yml` creates one Tournament application, one
PostgreSQL service, one product-local internal network, and one product-local
database volume. A client operating League and Tournament runs them as
different Compose projects with different credentials, volumes, and networks.

## Local validation

1. Copy `.env.example` to an untracked `.env` and replace its placeholder with
   a random local-only database password and a distinct random privacy suppression key of at least 32 characters.
2. Set `TOURNAMENT_ENVIRONMENT=Development` only for a fictional local
   organizer exercise. Compose defaults to Production and must never use the
   Development selector on a deployed host.
3. Run `docker compose config --quiet`.
4. Run `docker compose up -d --build`.
5. Confirm PostgreSQL is healthy, `/health/live` reports the web process, and
   `/health/ready` plus `/health` report product `tournament` and status
   `healthy`. A Production process without `TournamentDatabase` must fail to
   start rather than serve fictional state.
6. Confirm the initializer applied `001_initial_schema.sql` through
   `006_player_data_anonymization.sql`; apply all six a second time and verify they
   succeed without changing retained records.
7. Create a fictional Development tournament, restart only the application
   container, and verify the tournament, lifecycle history, and audit evidence
   remain available from PostgreSQL.

## Recovery and rollback

Migrations `002_administration_history.sql`,
`003_competition_operations.sql`, `004_launch_operations.sql`, and
`005_live_tournament_links.sql`, and `006_player_data_anonymization.sql` are additive.
Their forward-recovery
path is to correct and reapply the idempotent migration; it must not drop
history tables after they contain records. Before any future schema deployment,
take a named protected database backup, verify
its restore inventory, and record the exact application image. Roll back the
application to that image and restore the protected backup if a non-forward-
recoverable migration fails. Never drop schemas or remove a volume as a
Production rollback procedure. A restore must replay completed privacy directives before customer access or integration traffic resumes.

The application port binds to loopback only; an approved host-level reverse
proxy will terminate public TLS and join only this product's network. The
PostgreSQL service exposes no host port. Production exposes no local
administrator login. Administrative authorization
must remain unavailable there until the separate Account handoff is approved
and configured. The reverse proxy must preserve the original HTTPS scheme and
host so one-time live addresses and QR codes use the public origin. No Sandbox,
DNS, Caddy route, secret, or Production deployment is approved for version
0.6.0. DNS has not been updated for Tournament. Follow
`LAUNCH_CHECKLIST.md`; passing code checks does not authorize deployment.
