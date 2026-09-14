# Tournament Launch Checklist

Version: 0.5.0 candidate · updated 2026-09-14

Passing this checklist provides evidence for an owner launch decision. It does
not itself authorize DNS, secrets, Sandbox, Production, tags, or a public
GitHub Release.

## Decisions and external prerequisites

- [ ] Approve the durable Account token contract: issuer, Tournament audience,
  installation binding, signing-key storage and rotation, expiry, replay,
  outage behavior, and revocation.
- [ ] Approve the exact Tournament entitlement claim/check contract and cache
  behavior. Beta inclusion remains Account-owned; Tournament performs no
  checkout or subscription charging.
- [ ] Select the Tournament Sandbox and Production hostnames and approve DNS
  changes separately.
- [ ] Provide isolated Sandbox/Production PostgreSQL credentials through the
  host secret mechanism; never commit or print them.
- [ ] Approve the exact Sandbox deployment candidate before operating the host.

## Exact candidate gate

- [ ] Working tree is clean on a feature branch and HEAD equals the pushed
  GitHub branch.
- [ ] Release restore/build/test pass with zero warnings and all documented
  tests; the dependency advisory scan reports no vulnerable packages.
- [ ] Compose configuration resolves with a process-only fictional validation
  password and exposes PostgreSQL on no host port.
- [ ] All migrations `001` through `005` apply twice to a fresh isolated
  PostgreSQL container and the migration ledger contains each exactly once.
- [ ] Create a Tournament record, restart only the app container, and confirm
  event, draw, result revisions, status history, payouts, and audit evidence
  persist.
- [ ] Preserve a named protected database backup and verify its inventory and
  restricted permissions without printing credentials or participant data.
- [ ] Record the exact rollback application image and prove a restore in the
  isolated Sandbox stack.
- [ ] `/health/live` and `/health/ready` pass; stopping PostgreSQL leaves live
  healthy and ready unhealthy without leaking connection details.
- [ ] Anonymous organizer access, missing/expired entitlement, wrong event,
  wrong match, wildcard forgery, missing CSRF, stale result, private UUID, and
  archived publication attempts all fail safely.
- [ ] Live-link code is absent from PostgreSQL/audit evidence; activation,
  copy, local QR, anonymous viewing, expiry, rotation, manual revocation, and
  archival revocation pass, with uniform 404 responses for invalid links.
- [ ] Owner, assigned Director, and assigned Scorekeeper workflows pass at
  desktop, tablet, and 390px phone sizes with keyboard navigation, visible
  focus, no horizontal document overflow, no console errors, and a readable
  print preview.
- [ ] Single elimination, 3/5/8-player double elimination, reset final, and
  even/odd round robin complete correctly with public results and standings.
- [ ] A bounded local concurrency smoke run keeps health responsive and creates
  no duplicate results or overlapping active table assignments.
- [ ] README, Help, code outline, database, authorization/history, testing,
  deployment, third-party inventory, version, changelog, and current release
  handoff match the exact candidate.

## Owner-gated promotion

- [ ] Obtain explicit Production application/database approval after the exact
  Sandbox candidate passes.
- [ ] Promote only the Git tree and image that passed, without rebuilding or
  modifying the candidate.
- [ ] Verify schema, containers, isolation, public endpoints, version,
  authenticated organizer flows when an already-authorized account exists,
  logs, and recovery evidence.
- [ ] Obtain separate approval before creating an immutable version tag or
  GitHub Release.

Current local limitation: on 2026-09-14 the Docker API did not return from an
approved engine check on this workstation, so migration execution, container
restart persistence, PostgreSQL readiness failure, and restore testing remain
unchecked. Static migration contracts and Compose configuration are not a
substitute for these items.
