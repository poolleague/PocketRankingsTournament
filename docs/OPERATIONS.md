# Tournament Operations Guide

Version: 0.5.0 · updated 2026-09-14

This guide is for volunteer directors running a local pool event. Tournament
stores no participant contact information and does not require a League,
Account, or Player Profile record for someone to compete.

## Before tournament day

1. Create the event. It begins as a Private draft.
2. Add each competition and choose single elimination, double elimination, or
   round robin. Swiss and group-to-finals cannot be selected until their
   scheduling engines are implemented and validated.
3. Add the tables available at the venue.
4. Register entrants, including optional seed and handicap labels. Use
   Waitlisted rather than deleting a record when the field is full.
5. Add the displayed payout schedule. It is informational only and locks with
   the draw; Pocket Rankings does not collect or hold funds.
6. Choose Private, Unlisted, or Public visibility. Only Public events appear in
   the directory. Unlisted events work through their direct UUID link.
7. After registration opens, activate a Live Tournament Link for 24 hours,
   three days, or seven days. Copy the one-time address or print its locally
   generated QR code immediately. Rotating it invalidates the earlier address.

## Check-in and draw

Move the event through Registration Open and Check-in. If any entrant is marked
Checked In, only checked-in entrants enter the draw; otherwise Registered
entrants form the field. Withdrawn, Disqualified, and Waitlisted entrants stay
in history and never enter the draw.

Choose Seeded, Randomized, or Manual draw publication. Manual ordering requires
one unique seed for every eligible entrant. Publishing creates an immutable
draw revision and locks registration and payouts. A corrected pre-play draw
creates a new revision rather than overwriting the first.

## Run the floor

Move the event to In Progress only after every competition has a published
draw. For each playable match:

1. Assign an active venue table.
2. Call the match. A table with a Called or In Progress match cannot accept a
   second active match.
3. Start the match, or return it to Ready when a director needs to undo a floor
   call.
4. Record the score, forfeit, or no-show with a short reason. Score forms carry
   an expected version; a stale form is rejected instead of overwriting a newer
   result.

Round-robin standings rank wins, score difference, score-for, then display
name. The table is derived from confirmed results and updates without a second
editable standing record.

The Live Tournament Link is read-only and can be displayed on a venue screen
or opened by anyone who scans its QR code. Deactivate it early if the address
is shared incorrectly. It expires automatically; archiving the event also
ends it immediately. Permanent completed results use the normal tournament
address and visibility setting.

## Corrections and completion

A score correction that keeps the same winner appends a new result version. If
the winner changes, an Owner or Tournament Director must check the downstream
reset confirmation. Dependent later matches return to Waiting or Ready,
superseded results remain retained as downstream-reset revisions, and
unaffected opponents stay in place. Scorekeepers cannot authorize this reset.

Complete a competition only when every required match is Complete or Bye and
an unused conditional reset final remains Conditional. Complete the event only
after every competition is complete; archive it when it should leave public
history. Archived events cannot be republished.

## Print, export, and recovery evidence

The public event Print action uses a dedicated print layout for brackets,
schedules, and standings. Owners and Tournament Directors can export entrants,
current results, round-robin standings, and redacted audit history as CSV. CSV
cells neutralize spreadsheet formula prefixes.

Exports are convenient operational copies, not database backups. Follow
`DEPLOYMENT.md` and `LAUNCH_CHECKLIST.md` for protected PostgreSQL backup,
restore, application-image, and release evidence.
