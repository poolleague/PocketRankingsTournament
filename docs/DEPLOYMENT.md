# Deployment Foundation

The checked-in `docker-compose.yml` creates one Tournament application, one
PostgreSQL service, one product-local internal network, and one product-local
database volume. A client operating League and Tournament runs them as
different Compose projects with different credentials, volumes, and networks.

## Local validation

1. Copy `.env.example` to an untracked `.env` and replace its placeholder with
   a random local-only database password.
2. Run `docker compose config --quiet`.
3. Run `docker compose up -d --build`.
4. Confirm both services are healthy and `GET /health` returns product
   `tournament` and status `healthy`.
5. Confirm the initializer applied `001_initial_schema.sql` followed by
   `002_administration_history.sql`; apply both a second time and verify they
   succeed without changing retained records.
6. Create a fictional Development tournament, restart only the application
   container, and verify the tournament, lifecycle history, and audit evidence
   remain available from PostgreSQL.

## Recovery and rollback

Migration `002_administration_history.sql` is additive. Its forward-recovery
path is to correct and reapply the idempotent migration; it must not drop
history tables after they contain records. Before any future schema deployment,
take a named protected database backup, verify
its restore inventory, and record the exact application image. Roll back the
application to that image and restore the protected backup if a non-forward-
recoverable migration fails. Never drop schemas or remove a volume as a
Production rollback procedure.

Production exposes no local administrator login. Administrative authorization
must remain unavailable there until the separate Account handoff is approved
and configured. No Sandbox, DNS, Caddy route, secret, or Production deployment
is approved for version 0.2.0.
