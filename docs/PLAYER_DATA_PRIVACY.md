# Player Data Privacy

An irreversible Account opt-out does not erase competitive history. Tournament keeps brackets, matches, scores, standings, placements, and displayed payouts intact, but replaces every linked local participant with a cryptographically random installation-local public UUID and a readable random label such as `Deleted player A7K4`. Singles entrant names become that label; multi-member entrant labels become `Team with Deleted player A7K4`. The Account `PersonId` link is deleted, matching audit identity is redacted, and no reverse mapping is retained.

Only a non-identifying Account request receipt and an HMAC-SHA-256 suppression of `PersonId` remain. The suppression uses a Tournament-installation-only secret and is intended to prevent a later integration from silently relinking the opted-out identity. Repeated request delivery is idempotent.

The PostgreSQL operation is transactional. Application rollback must preserve the anonymization and suppression tables. After backup restore, completed privacy directives must be replayed before customer access or integration traffic resumes. Losing the suppression key prevents deterministic future checks; exposing it weakens the one-way property.

The durable operation is implemented, but signed delivery, directive authentication, acknowledgements, retry/dead-letter handling, and alerting are not active. Account must continue showing processing until all product targets actually acknowledge completion.
