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
5. Apply `Database/001_initial_schema.sql` a second time and verify it succeeds.

## Recovery and rollback

This unreleased foundation has no upgrade from existing customer data. Before
any future schema deployment, take a named protected database backup, verify
its restore inventory, and record the exact application image. Roll back the
application to that image and restore the protected backup if a non-forward-
recoverable migration fails. Never drop schemas or remove a volume as a
Production rollback procedure.

No Sandbox, DNS, Caddy route, secret, or Production deployment is approved for
version 0.1.0.
