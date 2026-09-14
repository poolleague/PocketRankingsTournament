# Pocket Rankings — Tournament

Tournament product for the Pocket Rankings platform: brackets, matches,
and results, isolated from League per
`PocketRankingsPlatform/PLATFORM_AGENTS.md` Section 1.

Keeps its own local participants and optionally links a person through the
Account-owned `PersonId` contract. It never references League teams or queries
another product's database.

## Structure

Matches `PoolLeagueWeb`'s layout: the actual project lives under
`src/PocketRankingsTournament/`, with `docs/` and `tests/` at repo root.

## Status

The application now includes a public tournament surface plus Tournament-local
administration, retained history, competition setup, entrant registration,
versioned draw publication, score entry, and bracket advancement on
`codex/tournament-foundation`.
Development provides fictional Owner, Tournament Director, and Scorekeeper
identities for permission testing. Production administration deliberately fails
closed until the separate Account identity handoff receives approval; this repo
does not implement or store passwords. See `AGENTS.md` for repo-specific rules,
`docs/CURRENT_RELEASE_HANDOFF.md` for current state, and
`docs/COMPETITIVE_RECON.md` for the product research behind the roadmap.

Single elimination, double elimination, and single-cycle round robin are the
operational competition engines in version 0.5.0. Directors can run table
calls, publish payouts for display, correct dependent bracket paths with
retained evidence, print the public draw, export safe local records, and issue
temporary `/live/{code}` venue-display links with locally generated QR codes.
Only a one-way hash of each short code is retained; links expire, can be
rotated or revoked, and end automatically on archival. Swiss
and group-to-finals remain modeled roadmap formats; the application refuses to
generate them rather than producing an incorrect schedule.

Start with `docs/OPERATIONS.md` for the tournament-day workflow and
`docs/LAUNCH_CHECKLIST.md` for the remaining environment decisions and launch
gate. Runtime dependency purpose and licensing are recorded in
`docs/THIRD_PARTY.md`. No DNS or deployment has been configured.
