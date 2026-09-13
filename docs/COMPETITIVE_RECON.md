# Tournament Software Reconnaissance

Last verified: 2026-09-13

## Product direction

Pocket Rankings Tournament is a universal **amateur pool competition** tool.
Its primary users are volunteer tournament directors, league operators, pool
rooms, bars, clubs, fundraisers, and players using phones at the venue. It
should feel like the League product through shared tokens, controls, status
language, accessibility, and responsive behavior, while retaining its own
navigation and isolated runtime.

The market evidence favors a modular competition model rather than a single
hard-coded bracket. Elimination brackets remain the default, but discipline,
participant format, stage format, draw method, race/scoring rules, handicap,
tables, and payouts are separate choices.

## Official-product findings

### DigitalPool

DigitalPool's pool-specific builder exposes singles, doubles, Scotch doubles,
and teams; multiple disciplines; winner/loser race lengths; random/seeded draw;
registration cutoff; table assignment; payout shapes; public/private state;
and a true-double-elimination final reset. Its live brackets surface scores,
table assignments, and match times. This is the clearest evidence that a local
pool director needs operational tools around the bracket, not only the diagram.

Sources:

- https://docs.digitalpool.com/tournament-builder-getting-started-guide-old
- https://digitalpool.com/dpl

### CueScore

CueScore separates discipline, break order, race length, sets, handicap, and
comments from the competition format. It supports single elimination, double
elimination, Swiss, round robin/league, and custom structures; transparent
random or seeded draws; participant swaps; printable match cards; table
planning; per-round race changes; handicap values; and live score/bracket
updates. Its seeding UI also handles tied seeds and club separation.

Sources:

- https://cuescore.com/cuescore/posts/Cuescore%2Bmanual%2B-%2Bindex/56176114
- https://cuescore.com/cuescore/posts/Basic%2Bsetup/57611143
- https://cuescore.com/cuescore/posts/Seeding%2Band%2Bseeding%2Boptions/45156466
- https://cuescore.com/cuescore/posts/Tournament%2B-%2BCreate%2BDraw%2B%2526%2BTools/57621772
- https://cuescore.com/cuescore/posts/Scoreboard%2Band%2Bhandicap%2Btournaments/60639115

### CompuSport / CSI

Published CompuSport events organize one event into many divisions for
singles, teams, and Scotch doubles, each with its own bracket and live results.
CSI's participant guidance demonstrates both double-elimination and two-stage
group-play-to-final-bracket events. Pocket Rankings should therefore model an
event containing multiple independently configured competitions.

Sources:

- https://manager.compusport.ca/GeneralSearch/Index/17243?callFromHomePage=True
- https://www.playcsipool.com/tournament-system.html

### Challonge

Challonge demonstrates the useful general-purpose format range: single and
double elimination, round robin, Swiss, leaderboard, and two-stage group play
followed by finals. It also combines registration, teams, seeding, standings,
and public tournament pages. We should support the pool-relevant subset through
one stage model, not separate tournament implementations.

Sources:

- https://challonge.com/
- https://kb.challonge.com/en/article/learn-about-challonge-competition-formats-1f8j1cf/

### Playpass

Playpass shows the value of editable matchups, public sharing, printable
schedules, multiple tables/venues, score tracking, staff access, and flexible
custom scoring. It also supports large brackets and mixed scheduling formats.
The relevant lesson for small operators is quick setup with progressive
advanced options rather than forcing every choice up front.

Source: https://playpass.com/sports-software/tournament-scheduler

## Recommended capability model

### First-class dimensions

- Disciplines: 8-ball, 9-ball, 10-ball, straight pool, one-pocket, banks,
  blackball, and a clearly labeled custom discipline.
- Entrants: singles first, with the data model ready for doubles/Scotch doubles
  and teams without treating a team as a person.
- Competition stages: single elimination, double elimination, round robin,
  Swiss, and group/pool play feeding an elimination final.
- Draws: random, seeded, manual, and mixed seeded/random; record the immutable
  draw evidence and make resets explicit and audited.
- Match rules: race-to, race lengths by bracket side or round, sets, alternate
  or winner break, handicap/start score, and a published ruleset label/link.
- Venue operations: check-in, waitlist, table inventory, table assignment,
  called/ready/in-progress/complete status, conflict detection, and estimated
  start/order rather than unreliable exact promises.
- Results: participant confirmation where configured, tournament-director
  correction with reason, forfeits, no-shows, byes, and a visible correction
  history.
- Money display: entry fee and prize/payout schedule as informational records;
  no processor, wallet, Calcutta accounting, or real-money custody in the
  approved foundation.

### Experience principles

- Default setup should create a local single- or double-elimination event in a
  few minutes; advanced format and rules options unfold only when needed.
- The public phone view prioritizes “where do I play, against whom, and when?”
  before the full bracket.
- Tournament directors need an operations queue: waiting matches, available
  tables, conflicts, delayed players, and the next safe action.
- Never force an Account, League membership, rating, or Professional Player
  subscription to participate. Optional verified links enrich identity without
  becoming a runtime dependency.
- Professional broadcast overlays, federation licensing, and streaming tools
  are later extensions, not MVP drivers.

## Deliberate roadmap order

1. Foundation: isolated app/database, event/competition model, public sample
   experience, single/double elimination engine, tests, and documentation.
2. Organizer access and mutations after the Account token/key-rotation contract
   is approved.
3. Registration, check-in, waitlist, table operations, score entry, and audited
   corrections.
4. Round robin, Swiss, and group-to-finals stages using the same competition
   model.
5. Doubles/Scotch doubles/team entrants, configurable handicaps, imports, and
   optional cross-product events.
6. Payment-provider work only after pricing, legal, refund, and provider phases
   receive separate approval.
