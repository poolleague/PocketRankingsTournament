# Third-Party Runtime Components

Version: Tournament 0.6.0 · updated 2026-09-15

- `Npgsql` 10.0.3 — PostgreSQL driver; used only with this product's isolated
  Tournament database.
- `QRCoder` 1.8.0 — MIT-licensed, cross-platform QR generator; used locally to
  render a self-contained SVG for a newly activated Live Tournament Link.

No browser script, hosted QR service, analytics endpoint, or other third party
receives the live address. Dependency advisories are checked by the repository
validation workflow. Package licenses remain governed by their upstream terms.
