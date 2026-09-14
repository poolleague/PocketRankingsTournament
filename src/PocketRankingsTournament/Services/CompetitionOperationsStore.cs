using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Npgsql;
using PocketRankingsTournament.Models;
using PocketRankingsTournament.Security;

namespace PocketRankingsTournament.Services;

// Extends the fictional Development store with the same guarded competition workflow as PostgreSQL.
public sealed partial class DevelopmentTournamentStore
{
    // Keeps setup inside the private/editable portion of the tournament lifecycle.
    public Task<OperationResult> CreateCompetitionAsync(CreateCompetitionInput input, ClaimsPrincipal actor, CancellationToken cancellationToken = default)
    {
        if (!CanManage(actor, input.TournamentId))
        {
            return Task.FromResult(OperationResult.Failure("Competition access is unavailable."));
        }
        lock (_sync)
        {
            var index = _events.FindIndex(item => item.Id == input.TournamentId);
            if (index < 0 || _events[index].Status is not (TournamentStatus.Draft or TournamentStatus.RegistrationOpen))
            {
                return Task.FromResult(OperationResult.Failure("Competitions can be added only before check-in."));
            }
            if (input.Format is not (CompetitionFormat.SingleElimination or CompetitionFormat.DoubleElimination or CompetitionFormat.RoundRobin))
            {
                return Task.FromResult(OperationResult.Failure("That scheduling engine is not available yet."));
            }

            var competition = new Competition(Guid.NewGuid(), input.Name.Trim(), input.Discipline,
                DisciplineLabel(input.Discipline), input.Format, input.EntrantType, input.WinnersRaceTo,
                input.Format == CompetitionFormat.DoubleElimination ? input.LosersRaceTo : null,
                input.UsesHandicap, input.RulesLabel.Trim(), Array.Empty<Participant>(), Array.Empty<BracketRound>());
            _events[index] = _events[index] with { Competitions = _events[index].Competitions.Append(competition).ToArray() };
            _audit[input.TournamentId].Add(NewAudit(actor, "competition_created", input.TournamentId, competition.Name));
            return Task.FromResult(OperationResult.Success("Competition created as a draft."));
        }
    }

    // Rejects duplicate names and seeds because both make a published local draw ambiguous.
    public Task<OperationResult> RegisterEntrantAsync(RegisterEntrantInput input, ClaimsPrincipal actor, CancellationToken cancellationToken = default)
    {
        if (!CanManage(actor, input.TournamentId))
        {
            return Task.FromResult(OperationResult.Failure("Registration access is unavailable."));
        }
        lock (_sync)
        {
            var eventIndex = _events.FindIndex(item => item.Id == input.TournamentId);
            if (eventIndex < 0)
            {
                return Task.FromResult(OperationResult.Failure("Tournament not found."));
            }
            var eventItem = _events[eventIndex];
            var competitionIndex = eventItem.Competitions.ToList().FindIndex(item => item.Id == input.CompetitionId);
            if (competitionIndex < 0 || eventItem.Status is not (TournamentStatus.Draft or TournamentStatus.RegistrationOpen or TournamentStatus.CheckIn))
            {
                return Task.FromResult(OperationResult.Failure("Registration is not available for this competition."));
            }
            var competition = eventItem.Competitions[competitionIndex];
            if (competition.Status is not (CompetitionStatus.Draft or CompetitionStatus.RegistrationOpen))
            {
                return Task.FromResult(OperationResult.Failure("The published draw has locked registration."));
            }
            if (competition.Participants.Any(item => string.Equals(item.DisplayName, input.DisplayName.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                return Task.FromResult(OperationResult.Failure("An entrant with that display name is already registered."));
            }
            if (input.Seed.HasValue && competition.Participants.Any(item => item.Seed == input.Seed))
            {
                return Task.FromResult(OperationResult.Failure("That seed is already assigned."));
            }

            var participant = new Participant(Guid.NewGuid(), input.DisplayName.Trim(), input.Seed, input.Handicap?.Trim(), input.Status);
            var updated = competition with
            {
                Participants = competition.Participants.Append(participant).ToArray(),
                Status = CompetitionStatus.RegistrationOpen
            };
            var competitions = eventItem.Competitions.ToArray();
            competitions[competitionIndex] = updated;
            _events[eventIndex] = eventItem with { Competitions = competitions };
            _audit[input.TournamentId].Add(NewAudit(actor, "entrant_registered", input.TournamentId, participant.DisplayName));
            return Task.FromResult(OperationResult.Success("Entrant registered."));
        }
    }

    // Stores a new immutable in-memory projection instead of letting mutable registration order become the draw.
    public Task<OperationResult> PublishDrawAsync(PublishDrawInput input, ClaimsPrincipal actor, CancellationToken cancellationToken = default)
    {
        if (!CanManage(actor, input.TournamentId))
        {
            return Task.FromResult(OperationResult.Failure("Draw access is unavailable."));
        }
        lock (_sync)
        {
            var location = FindCompetition(input.TournamentId, input.CompetitionId);
            if (location is null)
            {
                return Task.FromResult(OperationResult.Failure("Competition not found."));
            }
            var (eventIndex, competitionIndex, competition) = location.Value;
            if (competition.Status is CompetitionStatus.InProgress or CompetitionStatus.Complete or CompetitionStatus.Archived)
            {
                return Task.FromResult(OperationResult.Failure("A started competition cannot receive a new draw."));
            }
            if (competition.Rounds.SelectMany(item => item.Matches).Any(item => item.ResultVersion > 0))
            {
                return Task.FromResult(OperationResult.Failure("A draw with recorded results cannot be replaced."));
            }
            var eligible = EligibleForDraw(competition.Participants);
            if (eligible.Count < 2)
            {
                return Task.FromResult(OperationResult.Failure("Register at least two entrants before publishing."));
            }

            var ordered = OrderForDraw(eligible, input.Strategy);
            if (ordered is null)
            {
                return Task.FromResult(OperationResult.Failure("Manual draws require a unique seed for every entrant."));
            }
            var rounds = AdvanceByes(_bracketBuilder.Build(competition.Format, ordered, competition.Format == CompetitionFormat.DoubleElimination));
            ReplaceCompetition(eventIndex, competitionIndex, competition with
            {
                Participants = ordered.Concat(competition.Participants.Where(item => !eligible.Any(eligibleItem => eligibleItem.Id == item.Id))).ToArray(),
                Rounds = rounds,
                Status = CompetitionStatus.Drawn,
                DrawRevision = competition.DrawRevision + 1,
                DrawPublishedAt = DateTimeOffset.UtcNow
            });
            _audit[input.TournamentId].Add(NewAudit(actor, "draw_published", input.TournamentId, input.Reason.Trim()));
            return Task.FromResult(OperationResult.Success($"Draw revision {competition.DrawRevision + 1} published."));
        }
    }

    // Changes operational registration state only before the draw is frozen, retaining the same entrant UUID.
    public Task<OperationResult> UpdateEntrantStatusAsync(UpdateEntrantStatusInput input, ClaimsPrincipal actor, CancellationToken cancellationToken = default)
    {
        if (!CanManage(actor, input.TournamentId)) return Task.FromResult(OperationResult.Failure("Registration access is unavailable."));
        lock (_sync)
        {
            var location = FindCompetition(input.TournamentId, input.CompetitionId);
            if (location is null || location.Value.Competition.DrawRevision > 0) return Task.FromResult(OperationResult.Failure("The published draw has locked registration."));
            var (eventIndex, competitionIndex, competition) = location.Value;
            var entrantIndex = competition.Participants.ToList().FindIndex(item => item.Id == input.EntrantId);
            if (entrantIndex < 0) return Task.FromResult(OperationResult.Failure("Entrant not found."));
            var participants = competition.Participants.ToArray();
            participants[entrantIndex] = participants[entrantIndex] with { RegistrationStatus = input.ToStatus };
            ReplaceCompetition(eventIndex, competitionIndex, competition with { Participants = participants });
            _audit[input.TournamentId].Add(NewAudit(actor, "entrant_status_changed", input.TournamentId, input.Reason.Trim()));
            return Task.FromResult(OperationResult.Success($"Entrant moved to {input.ToStatus}."));
        }
    }

    // Returns a copy so callers cannot mutate the shared fictional floor inventory.
    public Task<IReadOnlyList<TournamentTable>> GetTablesAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        lock (_sync) return Task.FromResult<IReadOnlyList<TournamentTable>>(_tables.TryGetValue(eventId, out var tables) ? tables.OrderBy(item => item.SortOrder).ToArray() : Array.Empty<TournamentTable>());
    }

    // Adds a unique table name while retaining stable UUIDs for later assignments.
    public Task<OperationResult> CreateTableAsync(CreateTournamentTableInput input, ClaimsPrincipal actor, CancellationToken cancellationToken = default)
    {
        if (!CanManage(actor, input.TournamentId)) return Task.FromResult(OperationResult.Failure("Table access is unavailable."));
        lock (_sync)
        {
            if (!_tables.TryGetValue(input.TournamentId, out var tables)) return Task.FromResult(OperationResult.Failure("Tournament not found."));
            if (tables.Any(item => string.Equals(item.Name, input.Name.Trim(), StringComparison.OrdinalIgnoreCase))) return Task.FromResult(OperationResult.Failure("That table already exists."));
            tables.Add(new TournamentTable(Guid.NewGuid(), input.Name.Trim(), tables.Count == 0 ? 1 : tables.Max(item => item.SortOrder) + 1));
            _audit[input.TournamentId].Add(NewAudit(actor, "venue_table_created", input.TournamentId, input.Name.Trim()));
            return Task.FromResult(OperationResult.Success("Table added."));
        }
    }

    // Updates only the current match projection; the stable match key and result history remain unchanged.
    public Task<OperationResult> AssignMatchTableAsync(AssignMatchTableInput input, ClaimsPrincipal actor, CancellationToken cancellationToken = default)
    {
        if (!CanManage(actor, input.TournamentId)) return Task.FromResult(OperationResult.Failure("Table assignment access is unavailable."));
        lock (_sync)
        {
            var location = FindCompetition(input.TournamentId, input.CompetitionId);
            var table = _tables.GetValueOrDefault(input.TournamentId)?.SingleOrDefault(item => item.Id == input.TableId && item.IsActive);
            if (location is null || table is null) return Task.FromResult(OperationResult.Failure("Match or table not found."));
            var (eventIndex, competitionIndex, competition) = location.Value;
            var found = false;
            var rounds = competition.Rounds.Select(round => round with { Matches = round.Matches.Select(match => { if (match.Id != input.MatchId) return match; found = true; return match with { TableName = table.Name }; }).ToArray() }).ToArray();
            if (!found) return Task.FromResult(OperationResult.Failure("Match or table not found."));
            ReplaceCompetition(eventIndex, competitionIndex, competition with { Rounds = rounds });
            _audit[input.TournamentId].Add(NewAudit(actor, "match_table_assigned", input.TournamentId, $"{input.MatchId} · {table.Name}"));
            return Task.FromResult(OperationResult.Success($"{input.MatchId} assigned to {table.Name}."));
        }
    }

    // Enforces the visible call sequence and prevents two active matches from occupying one physical table.
    public Task<OperationResult> UpdateMatchStatusAsync(UpdateMatchStatusInput input, ClaimsPrincipal actor, CancellationToken cancellationToken = default)
    {
        if (!CanScore(actor, input.TournamentId) || !TournamentMatchAccess.CanRecord(actor, input.MatchId))
        {
            return Task.FromResult(OperationResult.Failure("Match access is unavailable."));
        }
        lock (_sync)
        {
            var location = FindCompetition(input.TournamentId, input.CompetitionId);
            if (location is null || _events[location.Value.EventIndex].Status != TournamentStatus.InProgress)
            {
                return Task.FromResult(OperationResult.Failure("The tournament must be in progress before calling matches."));
            }

            var (eventIndex, competitionIndex, competition) = location.Value;
            var match = competition.Rounds.SelectMany(round => round.Matches).SingleOrDefault(item => item.Id == input.MatchId);
            if (match is null || match.EntrantOne is null || match.EntrantTwo is null || match.ResultVersion > 0)
            {
                return Task.FromResult(OperationResult.Failure("That match is not available for a floor-status change."));
            }
            var forward = (match.Status, input.ToStatus) is (MatchStatus.Ready, MatchStatus.Called)
                or (MatchStatus.Ready, MatchStatus.InProgress)
                or (MatchStatus.Called, MatchStatus.InProgress);
            var managerReopen = CanManage(actor, input.TournamentId)
                && input.ToStatus == MatchStatus.Ready
                && match.Status is MatchStatus.Called or MatchStatus.InProgress;
            if (!forward && !managerReopen)
            {
                return Task.FromResult(OperationResult.Failure("That match status change is not allowed."));
            }
            if (input.ToStatus is MatchStatus.Called or MatchStatus.InProgress && string.IsNullOrWhiteSpace(match.TableName))
            {
                return Task.FromResult(OperationResult.Failure("Assign a table before calling or starting this match."));
            }
            if (input.ToStatus is MatchStatus.Called or MatchStatus.InProgress
                && _events[eventIndex].Competitions.SelectMany(item => item.Rounds).SelectMany(round => round.Matches)
                    .Any(item => item.Id != match.Id && item.TableName == match.TableName && item.Status is MatchStatus.Called or MatchStatus.InProgress))
            {
                return Task.FromResult(OperationResult.Failure($"{match.TableName} already has an active match."));
            }

            var rounds = competition.Rounds.Select(round => round with
            {
                Matches = round.Matches.Select(item => item.Id == match.Id ? item with { Status = input.ToStatus } : item).ToArray()
            }).ToArray();
            ReplaceCompetition(eventIndex, competitionIndex, competition with { Rounds = rounds });
            _audit[input.TournamentId].Add(NewAudit(actor, "match_status_changed", input.TournamentId, $"{match.Id}: {input.Reason.Trim()}"));
            return Task.FromResult(OperationResult.Success($"{match.Id} moved to {TournamentLabels.MatchStatus(input.ToStatus)}."));
        }
    }

    // Finalizes only a fully resolved bracket; an unused conditional reset does not block completion.
    public Task<OperationResult> CompleteCompetitionAsync(CompleteCompetitionInput input, ClaimsPrincipal actor, CancellationToken cancellationToken = default)
    {
        if (!CanManage(actor, input.TournamentId)) return Task.FromResult(OperationResult.Failure("Completion access is unavailable."));
        lock (_sync)
        {
            var location = FindCompetition(input.TournamentId, input.CompetitionId);
            if (location is null) return Task.FromResult(OperationResult.Failure("Competition not found."));
            var (eventIndex, competitionIndex, competition) = location.Value;
            var matches = competition.Rounds.SelectMany(item => item.Matches).ToArray();
            if (matches.Length == 0 || !matches.Any(item => item.Status == MatchStatus.Complete)
                || matches.Any(item => item.Status is not (MatchStatus.Complete or MatchStatus.Bye or MatchStatus.Conditional)))
            {
                return Task.FromResult(OperationResult.Failure("Every required match must have a result before completion."));
            }
            ReplaceCompetition(eventIndex, competitionIndex, competition with { Status = CompetitionStatus.Complete });
            _audit[input.TournamentId].Add(NewAudit(actor, "competition_completed", input.TournamentId, input.Reason.Trim()));
            return Task.FromResult(OperationResult.Success("Competition completed and retained."));
        }
    }

    // Keeps the displayed prize schedule editable only while the field and draw are still private to operations.
    public Task<OperationResult> UpsertPayoutDisplayAsync(UpsertPayoutDisplayInput input, ClaimsPrincipal actor, CancellationToken cancellationToken = default)
    {
        if (!CanManage(actor, input.TournamentId)) return Task.FromResult(OperationResult.Failure("Payout access is unavailable."));
        lock (_sync)
        {
            var location = FindCompetition(input.TournamentId, input.CompetitionId);
            if (location is null || location.Value.Competition.DrawRevision > 0)
                return Task.FromResult(OperationResult.Failure("Displayed payouts lock when the draw is published."));
            var (eventIndex, competitionIndex, competition) = location.Value;
            var payouts = (competition.Payouts ?? Array.Empty<PayoutDisplay>()).Where(item => item.Place != input.Place)
                .Append(new PayoutDisplay(input.Place, input.Label.Trim(), input.Amount)).OrderBy(item => item.Place).ToArray();
            ReplaceCompetition(eventIndex, competitionIndex, competition with { Payouts = payouts });
            _audit[input.TournamentId].Add(NewAudit(actor, "payout_display_updated", input.TournamentId, $"Place {input.Place}: {input.Label.Trim()}"));
            return Task.FromResult(OperationResult.Success("Displayed payout saved."));
        }
    }

    // Applies the result and downstream slot changes under one lock, preserving every earlier result version.
    public Task<OperationResult> RecordMatchResultAsync(RecordMatchResultInput input, ClaimsPrincipal actor, CancellationToken cancellationToken = default)
    {
        if (!CanScore(actor, input.TournamentId) || !TournamentMatchAccess.CanRecord(actor, input.MatchId))
        {
            return Task.FromResult(OperationResult.Failure("Score access is unavailable."));
        }
        lock (_sync)
        {
            var location = FindCompetition(input.TournamentId, input.CompetitionId);
            if (location is null)
            {
                return Task.FromResult(OperationResult.Failure("Competition not found."));
            }
            var (eventIndex, competitionIndex, competition) = location.Value;
            if (_events[eventIndex].Status != TournamentStatus.InProgress)
            {
                return Task.FromResult(OperationResult.Failure("Move the tournament to In progress before recording scores."));
            }
            var matches = competition.Rounds.SelectMany(item => item.Matches).ToDictionary(item => item.Id, StringComparer.Ordinal);
            if (!matches.TryGetValue(input.MatchId, out var match) || match.EntrantOne is null || match.EntrantTwo is null)
            {
                return Task.FromResult(OperationResult.Failure("That match is not ready for a result."));
            }
            if (match.ResultVersion != input.ExpectedVersion)
            {
                return Task.FromResult(OperationResult.Failure("This match changed. Refresh before recording another result."));
            }
            var winner = ResolveWinner(match, input);
            if (winner is null)
            {
                return Task.FromResult(OperationResult.Failure("A played match cannot end in a tie."));
            }
            var loser = winner.Id == match.EntrantOne.Id ? match.EntrantTwo : match.EntrantOne;
            var priorWinner = match.WinnerId.HasValue
                ? new[] { match.EntrantOne, match.EntrantTwo }.SingleOrDefault(item => item?.Id == match.WinnerId)
                : match.EntrantOneScore > match.EntrantTwoScore ? match.EntrantOne
                    : match.EntrantTwoScore > match.EntrantOneScore ? match.EntrantTwo : null;
            if (match.ResultVersion > 0 && priorWinner?.Id != winner.Id
                && (!input.ResetAffectedMatches || !CanManage(actor, input.TournamentId)))
            {
                return Task.FromResult(OperationResult.Failure("A director must confirm resetting affected downstream matches before changing the winner."));
            }
            if (match.ResultVersion > 0 && priorWinner?.Id != winner.Id)
            {
                var priorLoser = priorWinner?.Id == match.EntrantOne.Id ? match.EntrantTwo : match.EntrantOne;
                ReconcileRoute(matches, match.WinnerTo, priorWinner, winner);
                ReconcileRoute(matches, match.LoserTo, priorLoser, loser);
            }

            matches[match.Id] = match with
            {
                EntrantOneScore = input.EntrantOneScore,
                EntrantTwoScore = input.EntrantTwoScore,
                Status = MatchStatus.Complete,
                ResultVersion = match.ResultVersion + 1,
                WinnerId = winner.Id
            };
            if (match.Id == "GF1" && (match.ResultVersion == 0 || priorWinner?.Id != winner.Id) && matches.TryGetValue("GF2", out var reset))
            {
                var winnersFinal = matches.Values.Where(item => item.Bracket == "Winners").OrderByDescending(item => item.Round).First();
                var winnersChampion = new[] { winnersFinal.EntrantOne, winnersFinal.EntrantTwo }.SingleOrDefault(item => item?.Id == winnersFinal.WinnerId);
                if (winnersChampion?.Id != winner.Id)
                {
                    matches["GF2"] = reset with { EntrantOne = match.EntrantOne, EntrantTwo = match.EntrantTwo, Status = MatchStatus.Ready };
                }
            }
            else if (match.ResultVersion == 0)
            {
                AdvanceTo(matches, match.WinnerTo, winner);
                AdvanceTo(matches, match.LoserTo, loser);
            }
            NormalizeResolvedSources(matches);
            var rounds = competition.Rounds.Select(round => round with
            {
                Matches = round.Matches.Select(item => matches[item.Id]).ToArray()
            }).ToArray();
            ReplaceCompetition(eventIndex, competitionIndex, competition with { Rounds = rounds, Status = CompetitionStatus.InProgress });
            _audit[input.TournamentId].Add(NewAudit(actor, "match_result_recorded", input.TournamentId, input.Reason.Trim()));
            return Task.FromResult(OperationResult.Success($"{match.Id} result version {match.ResultVersion + 1} recorded."));
        }
    }

    // Resolves both immutable-record indexes so a replacement never updates the wrong parent event.
    private (int EventIndex, int CompetitionIndex, Competition Competition)? FindCompetition(Guid eventId, Guid competitionId)
    {
        var eventIndex = _events.FindIndex(item => item.Id == eventId);
        if (eventIndex < 0) return null;
        var competitionIndex = _events[eventIndex].Competitions.ToList().FindIndex(item => item.Id == competitionId);
        return competitionIndex < 0 ? null : (eventIndex, competitionIndex, _events[eventIndex].Competitions[competitionIndex]);
    }

    // Replaces nested record snapshots atomically while the caller holds the store lock.
    private void ReplaceCompetition(int eventIndex, int competitionIndex, Competition competition)
    {
        var competitions = _events[eventIndex].Competitions.ToArray();
        competitions[competitionIndex] = competition;
        _events[eventIndex] = _events[eventIndex] with { Competitions = competitions };
    }

    // Makes ordering intent explicit and uses cryptographic sampling for a fair randomized draw.
    internal static IReadOnlyList<Participant>? OrderForDraw(IReadOnlyList<Participant> participants, DrawStrategy strategy)
    {
        if (strategy == DrawStrategy.Manual && (participants.Any(item => item.Seed is null) || participants.Select(item => item.Seed).Distinct().Count() != participants.Count))
        {
            return null;
        }
        if (strategy == DrawStrategy.Randomized)
        {
            var shuffled = participants.ToArray();
            for (var index = shuffled.Length - 1; index > 0; index--)
            {
                var swapIndex = RandomNumberGenerator.GetInt32(index + 1);
                (shuffled[index], shuffled[swapIndex]) = (shuffled[swapIndex], shuffled[index]);
            }
            return shuffled.Select((item, index) => item with { Seed = index + 1 }).ToArray();
        }
        return participants.OrderBy(item => item.Seed ?? int.MaxValue).ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    // Treats explicit check-in as authoritative once used; otherwise registered entrants remain eligible for small informal events.
    internal static IReadOnlyList<Participant> EligibleForDraw(IReadOnlyList<Participant> participants)
    {
        var checkedIn = participants.Where(item => item.RegistrationStatus == RegistrationStatus.CheckedIn).ToArray();
        return checkedIn.Length > 0
            ? checkedIn
            : participants.Where(item => item.RegistrationStatus == RegistrationStatus.Registered).ToArray();
    }

    // Repeats until cascaded byes have reached the first playable downstream match.
    internal static IReadOnlyList<BracketRound> AdvanceByes(IReadOnlyList<BracketRound> rounds)
    {
        var matches = rounds.SelectMany(item => item.Matches).ToDictionary(
            item => item.Id,
            item => item.EntrantOne is not null && item.EntrantTwo is not null && !item.IsConditional
                ? item with { Status = MatchStatus.Ready }
                : item,
            StringComparer.Ordinal);
        foreach (var bye in matches.Values.Where(item => item.Status == MatchStatus.Bye))
        {
            AdvanceTo(matches, bye.WinnerTo, bye.EntrantOne ?? bye.EntrantTwo);
        }
        NormalizeResolvedSources(matches);
        return rounds.Select(round => round with { Matches = round.Matches.Select(item => matches[item.Id]).ToArray() }).ToArray();
    }

    // Resolves bracket paths whose possible incoming matches have all finished, including empty loser paths created by opening byes.
    private static void NormalizeResolvedSources(IDictionary<string, BracketMatch> matches)
    {
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var target in matches.Values.Where(item => item.Status == MatchStatus.Waiting).ToArray())
            {
                var sources = matches.Values.Where(item => item.WinnerTo == target.Id || item.LoserTo == target.Id).ToArray();
                if (sources.Length == 0 || sources.Any(item => item.Status is not (MatchStatus.Complete or MatchStatus.Bye)))
                {
                    continue;
                }
                var entrants = new[] { target.EntrantOne, target.EntrantTwo }.Where(item => item is not null).ToArray();
                if (entrants.Length == 2)
                {
                    matches[target.Id] = target with { Status = MatchStatus.Ready };
                }
                else
                {
                    matches[target.Id] = target with { Status = MatchStatus.Bye };
                    AdvanceTo(matches, target.WinnerTo, entrants.SingleOrDefault());
                }
                changed = true;
            }
        }
    }

    // Resolves non-play outcomes without requiring misleading artificial scores.
    private static Participant? ResolveWinner(BracketMatch match, RecordMatchResultInput input) => input.Outcome switch
    {
        MatchOutcome.EntrantOneForfeit or MatchOutcome.EntrantOneNoShow => match.EntrantTwo,
        MatchOutcome.EntrantTwoForfeit or MatchOutcome.EntrantTwoNoShow => match.EntrantOne,
        _ when input.EntrantOneScore > input.EntrantTwoScore => match.EntrantOne,
        _ when input.EntrantTwoScore > input.EntrantOneScore => match.EntrantTwo,
        _ => null
    };

    // Fills only the next open slot and marks the target ready once both sides are known.
    private static void AdvanceTo(IDictionary<string, BracketMatch> matches, string? targetId, Participant? entrant)
    {
        if (targetId is null || entrant is null || !matches.TryGetValue(targetId, out var target)) return;
        var updated = target.EntrantOne is null ? target with { EntrantOne = entrant } : target with { EntrantTwo = entrant };
        if (updated.EntrantOne is not null && updated.EntrantTwo is not null && !updated.IsConditional)
        {
            updated = updated with { Status = MatchStatus.Ready };
        }
        matches[targetId] = updated;
    }

    // Invalidates only the dependent path and keeps unaffected opponents in place when a director reverses a winner.
    private static void ReconcileRoute(IDictionary<string, BracketMatch> matches, string? targetId, Participant? previousEntrant, Participant? replacement)
    {
        if (targetId is null || previousEntrant is null || !matches.TryGetValue(targetId, out var target))
        {
            return;
        }
        var firstSlot = target.EntrantOne?.Id == previousEntrant.Id;
        var secondSlot = target.EntrantTwo?.Id == previousEntrant.Id;
        if (!firstSlot && !secondSlot)
        {
            return;
        }

        if (target.ResultVersion > 0)
        {
            var priorWinner = new[] { target.EntrantOne, target.EntrantTwo }.SingleOrDefault(item => item?.Id == target.WinnerId);
            var priorLoser = priorWinner?.Id == target.EntrantOne?.Id ? target.EntrantTwo : target.EntrantOne;
            ReconcileRoute(matches, target.WinnerTo, priorWinner, null);
            ReconcileRoute(matches, target.LoserTo, priorLoser, null);
        }

        var entrantOne = firstSlot ? replacement : target.EntrantOne;
        var entrantTwo = secondSlot ? replacement : target.EntrantTwo;
        matches[targetId] = target with
        {
            EntrantOne = entrantOne,
            EntrantTwo = entrantTwo,
            EntrantOneScore = null,
            EntrantTwoScore = null,
            Status = entrantOne is not null && entrantTwo is not null ? MatchStatus.Ready : MatchStatus.Waiting,
            ResultVersion = target.ResultVersion > 0 ? target.ResultVersion + 1 : target.ResultVersion,
            WinnerId = null
        };
    }

    // Keeps familiar pool naming in public projections while storage retains stable enum vocabulary.
    private static string DisciplineLabel(PoolDiscipline discipline) => discipline switch
    {
        PoolDiscipline.EightBall => "8-ball",
        PoolDiscipline.NineBall => "9-ball",
        PoolDiscipline.TenBall => "10-ball",
        PoolDiscipline.StraightPool => "Straight pool",
        PoolDiscipline.OnePocket => "One-pocket",
        _ => discipline.ToString()
    };

    // Requires both the manager role and the specific event assignment at the storage boundary.
    internal static bool CanManage(ClaimsPrincipal actor, Guid eventId) =>
        (actor.IsInRole(TournamentRoles.Owner) || actor.IsInRole(TournamentRoles.TournamentDirector))
        && TournamentEventAccess.CanAccess(actor, eventId);

    // Limits scoring to the three operational roles and their explicitly assigned event.
    internal static bool CanScore(ClaimsPrincipal actor, Guid eventId) =>
        (actor.IsInRole(TournamentRoles.Owner) || actor.IsInRole(TournamentRoles.TournamentDirector) || actor.IsInRole(TournamentRoles.Scorekeeper))
        && TournamentEventAccess.CanAccess(actor, eventId);
}

// Persists competition operations exclusively inside the installation-local Tournament database.
public sealed partial class PostgresTournamentStore
{
    // Durable operations are implemented against the additive 003 contract; every mutation is transactional and audited.
    public async Task<OperationResult> CreateCompetitionAsync(CreateCompetitionInput input, ClaimsPrincipal actor, CancellationToken cancellationToken = default)
    {
        if (!DevelopmentTournamentStore.CanManage(actor, input.TournamentId)) return OperationResult.Failure("Competition access is unavailable.");
        if (input.Format is not (CompetitionFormat.SingleElimination or CompetitionFormat.DoubleElimination or CompetitionFormat.RoundRobin))
            return OperationResult.Failure("That scheduling engine is not available yet.");
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            INSERT INTO tourn.competitions (public_uuid,event_id,name,discipline,entrant_type,format,winners_race_to,losers_race_to,uses_handicap,rules_label,bracket_reset_enabled)
            SELECT @uuid,e.event_id,@name,@discipline,@entrant_type,@format,@winners,@losers,@handicap,@rules,@reset
            FROM tourn.events e WHERE e.public_uuid=@event_id AND e.status IN ('draft','registration_open')
            RETURNING competition_id
            """, connection, transaction);
        command.Parameters.AddWithValue("uuid", Guid.NewGuid()); command.Parameters.AddWithValue("event_id", input.TournamentId);
        command.Parameters.AddWithValue("name", input.Name.Trim()); command.Parameters.AddWithValue("discipline", DbEnum(input.Discipline));
        command.Parameters.AddWithValue("entrant_type", DbEnum(input.EntrantType)); command.Parameters.AddWithValue("format", DbEnum(input.Format));
        command.Parameters.AddWithValue("winners", input.WinnersRaceTo); command.Parameters.AddWithValue("losers", (object?)input.LosersRaceTo ?? DBNull.Value);
        command.Parameters.AddWithValue("handicap", input.UsesHandicap); command.Parameters.AddWithValue("rules", input.RulesLabel.Trim()); command.Parameters.AddWithValue("reset", input.BracketResetEnabled);
        if (await command.ExecuteScalarAsync(cancellationToken) is null) { await transaction.RollbackAsync(cancellationToken); return OperationResult.Failure("Competitions can be added only before check-in."); }
        await InsertAuditAsync(connection, transaction, actor, "competition_created", input.TournamentId, input.Name.Trim(), cancellationToken);
        await transaction.CommitAsync(cancellationToken); return OperationResult.Success("Competition created as a draft.");
    }

    // Creates the local participant and competition entrant in one audited transaction.
    public async Task<OperationResult> RegisterEntrantAsync(RegisterEntrantInput input, ClaimsPrincipal actor, CancellationToken cancellationToken = default)
    {
        if (!DevelopmentTournamentStore.CanManage(actor, input.TournamentId)) return OperationResult.Failure("Registration access is unavailable.");
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        long participantId;
        await using (var participant = new NpgsqlCommand("INSERT INTO tourn.participants(public_uuid,display_name) VALUES(@uuid,@name) RETURNING participant_id", connection, transaction))
        { participant.Parameters.AddWithValue("uuid", Guid.NewGuid()); participant.Parameters.AddWithValue("name", input.DisplayName.Trim()); participantId = (long)(await participant.ExecuteScalarAsync(cancellationToken))!; }
        await using var entrant = new NpgsqlCommand("""
            INSERT INTO tourn.entrants(public_uuid,competition_id,display_name,seed,handicap_label,status)
            SELECT @uuid,c.competition_id,@name,@seed,@handicap,@status FROM tourn.competitions c
            JOIN tourn.events e ON e.event_id=c.event_id
            WHERE c.public_uuid=@competition_id AND e.public_uuid=@event_id AND e.status IN ('draft','registration_open','check_in') AND c.status IN ('draft','registration_open')
            ON CONFLICT DO NOTHING
            RETURNING entrant_id
            """, connection, transaction);
        entrant.Parameters.AddWithValue("uuid", Guid.NewGuid()); entrant.Parameters.AddWithValue("name", input.DisplayName.Trim());
        entrant.Parameters.AddWithValue("seed", (object?)input.Seed ?? DBNull.Value); entrant.Parameters.AddWithValue("handicap", (object?)input.Handicap?.Trim() ?? DBNull.Value);
        entrant.Parameters.AddWithValue("status", DbEnum(input.Status));
        entrant.Parameters.AddWithValue("competition_id", input.CompetitionId); entrant.Parameters.AddWithValue("event_id", input.TournamentId);
        var entrantId = await entrant.ExecuteScalarAsync(cancellationToken);
        if (entrantId is null) { await transaction.RollbackAsync(cancellationToken); return OperationResult.Failure("Registration is closed for this competition."); }
        await using (var member = new NpgsqlCommand("INSERT INTO tourn.entrant_members(entrant_id,participant_id,member_order) VALUES(@entrant,@participant,1)", connection, transaction))
        { member.Parameters.AddWithValue("entrant", (long)entrantId); member.Parameters.AddWithValue("participant", participantId); await member.ExecuteNonQueryAsync(cancellationToken); }
        await using (var state = new NpgsqlCommand("UPDATE tourn.competitions SET status='registration_open',updated_at=CURRENT_TIMESTAMP WHERE public_uuid=@id AND status='draft'", connection, transaction))
        { state.Parameters.AddWithValue("id", input.CompetitionId); await state.ExecuteNonQueryAsync(cancellationToken); }
        await InsertAuditAsync(connection, transaction, actor, "entrant_registered", input.TournamentId, input.DisplayName.Trim(), cancellationToken);
        await transaction.CommitAsync(cancellationToken); return OperationResult.Success("Entrant registered.");
    }

    // Preserves entrants while changing waitlist/check-in/withdrawal state before draw publication.
    public async Task<OperationResult> UpdateEntrantStatusAsync(UpdateEntrantStatusInput input, ClaimsPrincipal actor, CancellationToken cancellationToken = default)
    {
        if (!DevelopmentTournamentStore.CanManage(actor, input.TournamentId)) return OperationResult.Failure("Registration access is unavailable.");
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            UPDATE tourn.entrants entrant SET status=@status
            FROM tourn.competitions competition JOIN tourn.events event_item ON event_item.event_id=competition.event_id
            WHERE entrant.competition_id=competition.competition_id AND entrant.public_uuid=@entrant AND competition.public_uuid=@competition
              AND event_item.public_uuid=@event AND competition.current_draw_revision=0
            """, connection, transaction);
        command.Parameters.AddWithValue("status", DbEnum(input.ToStatus)); command.Parameters.AddWithValue("entrant", input.EntrantId);
        command.Parameters.AddWithValue("competition", input.CompetitionId); command.Parameters.AddWithValue("event", input.TournamentId);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1) { await transaction.RollbackAsync(cancellationToken); return OperationResult.Failure("Entrant not found or the draw has locked registration."); }
        await InsertAuditAsync(connection, transaction, actor, "entrant_status_changed", input.TournamentId, input.Reason.Trim(), cancellationToken);
        await transaction.CommitAsync(cancellationToken); return OperationResult.Success($"Entrant moved to {input.ToStatus}.");
    }

    // Reads table identities only from the venue attached to the requested event.
    public async Task<IReadOnlyList<TournamentTable>> GetTablesAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        var result = new List<TournamentTable>();
        await using var command = _dataSource.CreateCommand("SELECT t.public_uuid,t.name,t.sort_order,t.is_active FROM tourn.venue_tables t JOIN tourn.events e ON e.venue_id=t.venue_id WHERE e.public_uuid=@event ORDER BY t.sort_order");
        command.Parameters.AddWithValue("event", eventId); await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(new TournamentTable(reader.GetGuid(0), reader.GetString(1), reader.GetInt32(2), reader.GetBoolean(3)));
        return result;
    }

    // Allocates the next floor order and writes audit evidence in the same transaction.
    public async Task<OperationResult> CreateTableAsync(CreateTournamentTableInput input, ClaimsPrincipal actor, CancellationToken cancellationToken = default)
    {
        if (!DevelopmentTournamentStore.CanManage(actor, input.TournamentId)) return OperationResult.Failure("Table access is unavailable.");
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken); await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            INSERT INTO tourn.venue_tables(venue_id,public_uuid,name,sort_order)
            SELECT e.venue_id,@uuid,@name,COALESCE((SELECT max(t.sort_order)+1 FROM tourn.venue_tables t WHERE t.venue_id=e.venue_id),1)
            FROM tourn.events e WHERE e.public_uuid=@event AND e.status<>'archived' ON CONFLICT DO NOTHING
            """, connection, transaction);
        command.Parameters.AddWithValue("uuid", Guid.NewGuid()); command.Parameters.AddWithValue("name", input.Name.Trim()); command.Parameters.AddWithValue("event", input.TournamentId);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1) { await transaction.RollbackAsync(cancellationToken); return OperationResult.Failure("The table could not be added."); }
        await InsertAuditAsync(connection, transaction, actor, "venue_table_created", input.TournamentId, input.Name.Trim(), cancellationToken); await transaction.CommitAsync(cancellationToken);
        return OperationResult.Success("Table added.");
    }

    // Limits assignment to the current draw and a table belonging to the same event venue.
    public async Task<OperationResult> AssignMatchTableAsync(AssignMatchTableInput input, ClaimsPrincipal actor, CancellationToken cancellationToken = default)
    {
        if (!DevelopmentTournamentStore.CanManage(actor, input.TournamentId)) return OperationResult.Failure("Table assignment access is unavailable.");
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken); await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            UPDATE tourn.matches match_item SET venue_table_id=table_item.venue_table_id
            FROM tourn.stages stage JOIN tourn.competitions competition ON competition.competition_id=stage.competition_id
            JOIN tourn.events event_item ON event_item.event_id=competition.event_id
            JOIN tourn.venue_tables table_item ON table_item.venue_id=event_item.venue_id
            WHERE match_item.stage_id=stage.stage_id AND stage.draw_revision=competition.current_draw_revision
              AND match_item.bracket_key=@match AND competition.public_uuid=@competition AND event_item.public_uuid=@event
              AND table_item.public_uuid=@table AND table_item.is_active
            """, connection, transaction);
        command.Parameters.AddWithValue("match", input.MatchId); command.Parameters.AddWithValue("competition", input.CompetitionId); command.Parameters.AddWithValue("event", input.TournamentId); command.Parameters.AddWithValue("table", input.TableId);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1) { await transaction.RollbackAsync(cancellationToken); return OperationResult.Failure("Match or table not found."); }
        await InsertAuditAsync(connection, transaction, actor, "match_table_assigned", input.TournamentId, input.MatchId, cancellationToken); await transaction.CommitAsync(cancellationToken);
        return OperationResult.Success("Match table assigned.");
    }

    // Locks the target match and its table while enforcing the same floor sequence as the Development store.
    public async Task<OperationResult> UpdateMatchStatusAsync(UpdateMatchStatusInput input, ClaimsPrincipal actor, CancellationToken cancellationToken = default)
    {
        if (!DevelopmentTournamentStore.CanScore(actor, input.TournamentId) || !TournamentMatchAccess.CanRecord(actor, input.MatchId))
            return OperationResult.Failure("Match access is unavailable.");
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        long matchId; long? tableId; MatchStatus current;
        await using (var read = new NpgsqlCommand("""
            SELECT match_item.match_id,match_item.venue_table_id,match_item.status
            FROM tourn.matches match_item
            JOIN tourn.stages stage ON stage.stage_id=match_item.stage_id
            JOIN tourn.competitions competition ON competition.competition_id=stage.competition_id
            JOIN tourn.events event_item ON event_item.event_id=competition.event_id
            WHERE event_item.public_uuid=@event AND event_item.status='in_progress'
              AND competition.public_uuid=@competition AND stage.draw_revision=competition.current_draw_revision
              AND match_item.bracket_key=@match AND match_item.result_version=0
              AND EXISTS (SELECT 1 FROM tourn.match_entrants entrant WHERE entrant.match_id=match_item.match_id AND entrant.entrant_id IS NOT NULL HAVING count(*)=2)
            FOR UPDATE OF match_item
            """, connection, transaction))
        {
            read.Parameters.AddWithValue("event", input.TournamentId); read.Parameters.AddWithValue("competition", input.CompetitionId); read.Parameters.AddWithValue("match", input.MatchId);
            await using var reader = await read.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) { await transaction.RollbackAsync(cancellationToken); return OperationResult.Failure("That match is not available for a floor-status change."); }
            matchId = reader.GetInt64(0); tableId = reader.IsDBNull(1) ? null : reader.GetInt64(1); current = ParseEnum<MatchStatus>(reader.GetString(2));
        }
        var forward = (current, input.ToStatus) is (MatchStatus.Ready, MatchStatus.Called)
            or (MatchStatus.Ready, MatchStatus.InProgress) or (MatchStatus.Called, MatchStatus.InProgress);
        var managerReopen = DevelopmentTournamentStore.CanManage(actor, input.TournamentId)
            && input.ToStatus == MatchStatus.Ready && current is MatchStatus.Called or MatchStatus.InProgress;
        if (!forward && !managerReopen) { await transaction.RollbackAsync(cancellationToken); return OperationResult.Failure("That match status change is not allowed."); }
        if (input.ToStatus is MatchStatus.Called or MatchStatus.InProgress && tableId is null)
        { await transaction.RollbackAsync(cancellationToken); return OperationResult.Failure("Assign a table before calling or starting this match."); }
        if (input.ToStatus is MatchStatus.Called or MatchStatus.InProgress)
        {
            await using var conflict = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM tourn.matches WHERE venue_table_id=@table AND match_id<>@match AND status IN ('called','in_progress'))", connection, transaction);
            conflict.Parameters.AddWithValue("table", tableId!.Value); conflict.Parameters.AddWithValue("match", matchId);
            if ((bool)(await conflict.ExecuteScalarAsync(cancellationToken))!) { await transaction.RollbackAsync(cancellationToken); return OperationResult.Failure("That table already has an active match."); }
        }
        await using (var update = new NpgsqlCommand("""
            UPDATE tourn.matches SET status=@status,
              called_at=CASE WHEN @status='ready' THEN NULL ELSE COALESCE(called_at,CURRENT_TIMESTAMP) END,
              started_at=CASE WHEN @status='ready' OR @status='called' THEN NULL ELSE COALESCE(started_at,CURRENT_TIMESTAMP) END
            WHERE match_id=@match
            """, connection, transaction))
        { update.Parameters.AddWithValue("status", DbEnum(input.ToStatus)); update.Parameters.AddWithValue("match", matchId); await update.ExecuteNonQueryAsync(cancellationToken); }
        await InsertAuditAsync(connection, transaction, actor, "match_status_changed", input.TournamentId, $"{input.MatchId}: {input.Reason.Trim()}", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return OperationResult.Success($"{input.MatchId} moved to {TournamentLabels.MatchStatus(input.ToStatus)}.");
    }

    // Uses the current retained stage set and refuses completion while any required match is unresolved.
    public async Task<OperationResult> CompleteCompetitionAsync(CompleteCompetitionInput input, ClaimsPrincipal actor, CancellationToken cancellationToken = default)
    {
        if (!DevelopmentTournamentStore.CanManage(actor, input.TournamentId)) return OperationResult.Failure("Completion access is unavailable.");
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken); await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            UPDATE tourn.competitions competition SET status='complete',updated_at=CURRENT_TIMESTAMP
            FROM tourn.events event_item
            WHERE competition.event_id=event_item.event_id AND competition.public_uuid=@competition AND event_item.public_uuid=@event
              AND competition.status='in_progress'
              AND EXISTS (SELECT 1 FROM tourn.stages stage JOIN tourn.matches match_item ON match_item.stage_id=stage.stage_id WHERE stage.competition_id=competition.competition_id AND stage.draw_revision=competition.current_draw_revision AND match_item.status='complete')
              AND NOT EXISTS (SELECT 1 FROM tourn.stages stage JOIN tourn.matches match_item ON match_item.stage_id=stage.stage_id WHERE stage.competition_id=competition.competition_id AND stage.draw_revision=competition.current_draw_revision AND match_item.status NOT IN ('complete','bye','conditional'))
            """, connection, transaction);
        command.Parameters.AddWithValue("competition", input.CompetitionId); command.Parameters.AddWithValue("event", input.TournamentId);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1) { await transaction.RollbackAsync(cancellationToken); return OperationResult.Failure("Every required match must have a result before completion."); }
        await InsertAuditAsync(connection, transaction, actor, "competition_completed", input.TournamentId, input.Reason.Trim(), cancellationToken); await transaction.CommitAsync(cancellationToken);
        return OperationResult.Success("Competition completed and retained.");
    }

    // Upserts only an informational USD display row before publication; no funds or payment state enter Tournament.
    public async Task<OperationResult> UpsertPayoutDisplayAsync(UpsertPayoutDisplayInput input, ClaimsPrincipal actor, CancellationToken cancellationToken = default)
    {
        if (!DevelopmentTournamentStore.CanManage(actor, input.TournamentId)) return OperationResult.Failure("Payout access is unavailable.");
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            INSERT INTO tourn.payout_displays(competition_id,place,label,amount,currency_code)
            SELECT competition.competition_id,@place,@label,@amount,'USD'
            FROM tourn.competitions competition JOIN tourn.events event_item ON event_item.event_id=competition.event_id
            WHERE competition.public_uuid=@competition AND event_item.public_uuid=@event
              AND competition.current_draw_revision=0 AND event_item.status IN ('draft','registration_open','check_in')
            ON CONFLICT (competition_id,place) DO UPDATE SET label=EXCLUDED.label,amount=EXCLUDED.amount
            RETURNING payout_display_id
            """, connection, transaction);
        command.Parameters.AddWithValue("place", input.Place); command.Parameters.AddWithValue("label", input.Label.Trim()); command.Parameters.AddWithValue("amount", input.Amount);
        command.Parameters.AddWithValue("competition", input.CompetitionId); command.Parameters.AddWithValue("event", input.TournamentId);
        if (await command.ExecuteScalarAsync(cancellationToken) is null) { await transaction.RollbackAsync(cancellationToken); return OperationResult.Failure("Displayed payouts lock when the draw is published."); }
        await InsertAuditAsync(connection, transaction, actor, "payout_display_updated", input.TournamentId, $"Place {input.Place}: {input.Label.Trim()}", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return OperationResult.Success("Displayed payout saved.");
    }

    // Appends a complete draw snapshot and a new retained stage set before moving the current pointer.
    public async Task<OperationResult> PublishDrawAsync(PublishDrawInput input, ClaimsPrincipal actor, CancellationToken cancellationToken = default)
    {
        if (!DevelopmentTournamentStore.CanManage(actor, input.TournamentId)) return OperationResult.Failure("Draw access is unavailable.");
        var tournament = await FindAsync(input.TournamentId, cancellationToken);
        var competition = tournament?.Competitions.SingleOrDefault(item => item.Id == input.CompetitionId);
        var eligible = competition is null ? Array.Empty<Participant>() : DevelopmentTournamentStore.EligibleForDraw(competition.Participants).ToArray();
        if (competition is null || eligible.Length < 2) return OperationResult.Failure("Register at least two entrants before publishing.");
        if (competition.Status is CompetitionStatus.InProgress or CompetitionStatus.Complete or CompetitionStatus.Archived) return OperationResult.Failure("A started competition cannot receive a new draw.");
        var ordered = DevelopmentTournamentStore.OrderForDraw(eligible, input.Strategy);
        if (ordered is null) return OperationResult.Failure("Manual draws require a unique seed for every entrant.");
        var rounds = DevelopmentTournamentStore.AdvanceByes(
            new BracketBuilder().Build(competition.Format, ordered, competition.Format == CompetitionFormat.DoubleElimination));
        var revision = competition.DrawRevision + 1;
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        long competitionId;
        await using (var locked = new NpgsqlCommand("SELECT competition_id,current_draw_revision FROM tourn.competitions WHERE public_uuid=@id AND status IN ('draft','registration_open','drawn') FOR UPDATE", connection, transaction))
        {
            locked.Parameters.AddWithValue("id", input.CompetitionId);
            await using var reader = await locked.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken) || reader.GetInt32(1) != competition.DrawRevision)
            {
                await transaction.RollbackAsync(cancellationToken);
                return OperationResult.Failure("The competition changed. Refresh before publishing another draw.");
            }
            competitionId = reader.GetInt64(0);
        }
        await using (var draw = new NpgsqlCommand("INSERT INTO tourn.draw_revisions(competition_id,revision,draw_json,reason,draw_strategy) VALUES(@id,@revision,CAST(@json AS jsonb),@reason,@strategy)", connection, transaction))
        { draw.Parameters.AddWithValue("id", competitionId); draw.Parameters.AddWithValue("revision", revision); draw.Parameters.AddWithValue("json", JsonSerializer.Serialize(rounds)); draw.Parameters.AddWithValue("reason", input.Reason.Trim()); draw.Parameters.AddWithValue("strategy", DbEnum(input.Strategy)); await draw.ExecuteNonQueryAsync(cancellationToken); }
        await PersistDrawAsync(connection, transaction, competitionId, revision, competition.Format, rounds, cancellationToken);
        await using (var state = new NpgsqlCommand("UPDATE tourn.competitions SET status='drawn',current_draw_revision=@revision,draw_published_at=CURRENT_TIMESTAMP,updated_at=CURRENT_TIMESTAMP WHERE competition_id=@id", connection, transaction))
        { state.Parameters.AddWithValue("revision", revision); state.Parameters.AddWithValue("id", competitionId); await state.ExecuteNonQueryAsync(cancellationToken); }
        await InsertAuditAsync(connection, transaction, actor, "draw_published", input.TournamentId, input.Reason.Trim(), cancellationToken);
        await transaction.CommitAsync(cancellationToken); return OperationResult.Success($"Draw revision {revision} published.");
    }

    // Locks the match version so concurrent operators cannot overwrite a confirmed result.
    public async Task<OperationResult> RecordMatchResultAsync(RecordMatchResultInput input, ClaimsPrincipal actor, CancellationToken cancellationToken = default)
    {
        if (!DevelopmentTournamentStore.CanScore(actor, input.TournamentId) || !TournamentMatchAccess.CanRecord(actor, input.MatchId)) return OperationResult.Failure("Score access is unavailable.");
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var read = new NpgsqlCommand("""
            SELECT m.match_id,m.public_uuid,m.result_version,e1.entrant_id,e2.entrant_id,
                   (SELECT entrant_id FROM tourn.match_entrants winner WHERE winner.match_id=m.match_id AND winner.is_winner)
            FROM tourn.matches m JOIN tourn.stages s ON s.stage_id=m.stage_id JOIN tourn.competitions c ON c.competition_id=s.competition_id JOIN tourn.events e ON e.event_id=c.event_id
            LEFT JOIN tourn.match_entrants e1 ON e1.match_id=m.match_id AND e1.slot=1 LEFT JOIN tourn.match_entrants e2 ON e2.match_id=m.match_id AND e2.slot=2
            WHERE e.public_uuid=@event AND e.status='in_progress' AND c.public_uuid=@competition AND c.status IN ('drawn','in_progress') AND s.draw_revision=c.current_draw_revision AND m.bracket_key=@match FOR UPDATE OF m
            """, connection, transaction);
        read.Parameters.AddWithValue("event", input.TournamentId); read.Parameters.AddWithValue("competition", input.CompetitionId); read.Parameters.AddWithValue("match", input.MatchId);
        long matchId; Guid matchUuid; int version; long entrantOne; long entrantTwo; long? priorWinner;
        await using (var reader = await read.ExecuteReaderAsync(cancellationToken))
        { if (!await reader.ReadAsync(cancellationToken) || reader.IsDBNull(3) || reader.IsDBNull(4)) { await transaction.RollbackAsync(cancellationToken); return OperationResult.Failure("That match is not ready for a result."); } matchId = reader.GetInt64(0); matchUuid = reader.GetGuid(1); version = reader.GetInt32(2); entrantOne = reader.GetInt64(3); entrantTwo = reader.GetInt64(4); priorWinner = reader.IsDBNull(5) ? null : reader.GetInt64(5); }
        if (version != input.ExpectedVersion) { await transaction.RollbackAsync(cancellationToken); return OperationResult.Failure("This match changed. Refresh before recording another result."); }
        var winner = input.Outcome switch { MatchOutcome.EntrantOneForfeit or MatchOutcome.EntrantOneNoShow => entrantTwo, MatchOutcome.EntrantTwoForfeit or MatchOutcome.EntrantTwoNoShow => entrantOne, _ when input.EntrantOneScore > input.EntrantTwoScore => entrantOne, _ when input.EntrantTwoScore > input.EntrantOneScore => entrantTwo, _ => 0 };
        if (winner == 0) { await transaction.RollbackAsync(cancellationToken); return OperationResult.Failure("A played match cannot end in a tie."); }
        if (version > 0 && priorWinner != winner
            && (!input.ResetAffectedMatches || !DevelopmentTournamentStore.CanManage(actor, input.TournamentId)))
        { await transaction.RollbackAsync(cancellationToken); return OperationResult.Failure("A director must confirm resetting affected downstream matches before changing the winner."); }
        if (version > 0 && priorWinner != winner)
        {
            await ResetDescendantsAsync(connection, transaction, matchId, input.Reason.Trim(), cancellationToken);
        }
        await using (var revision = new NpgsqlCommand("INSERT INTO tourn.match_result_revisions(match_id,revision,entrant_one_score,entrant_two_score,winner_entrant_id,reason,outcome) VALUES(@match,@revision,@one,@two,@winner,@reason,@outcome)", connection, transaction))
        { revision.Parameters.AddWithValue("match", matchId); revision.Parameters.AddWithValue("revision", version + 1); revision.Parameters.AddWithValue("one", input.EntrantOneScore); revision.Parameters.AddWithValue("two", input.EntrantTwoScore); revision.Parameters.AddWithValue("winner", winner); revision.Parameters.AddWithValue("reason", input.Reason.Trim()); revision.Parameters.AddWithValue("outcome", DbEnum(input.Outcome)); await revision.ExecuteNonQueryAsync(cancellationToken); }
        await using (var slots = new NpgsqlCommand("UPDATE tourn.match_entrants SET score=CASE slot WHEN 1 THEN @one ELSE @two END,is_winner=(entrant_id=@winner) WHERE match_id=@match", connection, transaction))
        { slots.Parameters.AddWithValue("one", input.EntrantOneScore); slots.Parameters.AddWithValue("two", input.EntrantTwoScore); slots.Parameters.AddWithValue("winner", winner); slots.Parameters.AddWithValue("match", matchId); await slots.ExecuteNonQueryAsync(cancellationToken); }
        await using (var update = new NpgsqlCommand("UPDATE tourn.matches SET status='complete',completed_at=CURRENT_TIMESTAMP,result_version=result_version+1 WHERE match_id=@match", connection, transaction))
        { update.Parameters.AddWithValue("match", matchId); await update.ExecuteNonQueryAsync(cancellationToken); }
        await using (var state = new NpgsqlCommand("UPDATE tourn.competitions SET status='in_progress',updated_at=CURRENT_TIMESTAMP WHERE public_uuid=@id AND status='drawn'", connection, transaction))
        { state.Parameters.AddWithValue("id", input.CompetitionId); await state.ExecuteNonQueryAsync(cancellationToken); }
        if (version == 0 || priorWinner != winner) await AdvanceResultAsync(connection, transaction, matchId, winner, winner == entrantOne ? entrantTwo : entrantOne, cancellationToken);
        await InsertAuditAsync(connection, transaction, actor, "match_result_recorded", input.TournamentId, input.Reason.Trim(), cancellationToken);
        await transaction.CommitAsync(cancellationToken); return OperationResult.Success($"{input.MatchId} result version {version + 1} recorded.");
    }

    // Rehydrates the current published revision; older stages remain retained for audit and never leak into the live projection.
    private async Task<IReadOnlyList<Competition>> LoadCompetitionsAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var competitions = new List<Competition>();
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var list = new NpgsqlCommand("SELECT c.public_uuid,c.name,c.discipline,c.format,c.entrant_type,c.winners_race_to,c.losers_race_to,c.uses_handicap,c.rules_label,c.status,c.current_draw_revision,c.draw_published_at FROM tourn.competitions c JOIN tourn.events e ON e.event_id=c.event_id WHERE e.public_uuid=@id ORDER BY c.competition_id", connection);
        list.Parameters.AddWithValue("id", eventId);
        var rows = new List<object[]>();
        await using (var reader = await list.ExecuteReaderAsync(cancellationToken)) while (await reader.ReadAsync(cancellationToken)) rows.Add(Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? null! : reader.GetValue(i)).ToArray());
        foreach (var row in rows)
        {
            var id = (Guid)row[0]; var participants = await LoadParticipantsAsync(connection, id, cancellationToken); var rounds = await LoadRoundsAsync(connection, id, (int)row[10], cancellationToken); var payouts = await LoadPayoutsAsync(connection, id, cancellationToken);
            competitions.Add(new Competition(id, (string)row[1], ParseEnum<PoolDiscipline>((string)row[2]), ((string)row[2]).Replace('_', ' '), ParseEnum<CompetitionFormat>((string)row[3]), ParseEnum<EntrantType>((string)row[4]), Convert.ToInt32(row[5]), row[6] is null ? null : Convert.ToInt32(row[6]), (bool)row[7], (string)row[8], participants, rounds, ParseEnum<CompetitionStatus>((string)row[9]), (int)row[10], row[11] as DateTimeOffset?, payouts));
        }
        return competitions;
    }

    // Loads only public-facing payout labels and amounts; payment or settlement data is intentionally absent.
    private static async Task<IReadOnlyList<PayoutDisplay>> LoadPayoutsAsync(NpgsqlConnection connection, Guid competitionId, CancellationToken token)
    {
        var result = new List<PayoutDisplay>();
        await using var command = new NpgsqlCommand("SELECT place,label,amount FROM tourn.payout_displays WHERE competition_id=(SELECT competition_id FROM tourn.competitions WHERE public_uuid=@id) ORDER BY place", connection);
        command.Parameters.AddWithValue("id", competitionId);
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) result.Add(new PayoutDisplay(reader.GetInt32(0), reader.GetString(1), reader.GetDecimal(2)));
        return result;
    }

    // Reads only active competitive identities; optional Account links remain outside the projection.
    private static async Task<IReadOnlyList<Participant>> LoadParticipantsAsync(NpgsqlConnection connection, Guid competitionId, CancellationToken token)
    {
        var result = new List<Participant>(); await using var command = new NpgsqlCommand("SELECT public_uuid,display_name,seed,handicap_label,status FROM tourn.entrants WHERE competition_id=(SELECT competition_id FROM tourn.competitions WHERE public_uuid=@id) ORDER BY seed NULLS LAST,display_name", connection); command.Parameters.AddWithValue("id", competitionId);
        await using var reader = await command.ExecuteReaderAsync(token); while (await reader.ReadAsync(token)) result.Add(new Participant(reader.GetGuid(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetInt32(2), reader.IsDBNull(3) ? null : reader.GetString(3), ParseEnum<RegistrationStatus>(reader.GetString(4)))); return result;
    }

    // Reconstructs current match cards from normalized rows without loading older draw revisions.
    private static async Task<IReadOnlyList<BracketRound>> LoadRoundsAsync(NpgsqlConnection connection, Guid competitionId, int revision, CancellationToken token)
    {
        if (revision == 0) return Array.Empty<BracketRound>(); var rounds = new Dictionary<string, List<BracketMatch>>(); var names = new Dictionary<string, (string Name, string Bracket, int Number)>();
        await using var command = new NpgsqlCommand("""
            SELECT s.public_uuid,s.name,s.format,s.stage_order,m.bracket_key,m.bracket_side,m.round_number,m.position,m.label,m.status,m.result_version,
                   e1.public_uuid,e1.display_name,e1.seed,e1.handicap_label,me1.score,e2.public_uuid,e2.display_name,e2.seed,e2.handicap_label,me2.score,
                   wt.bracket_key,lt.bracket_key,table_item.name,
                   CASE WHEN me1.is_winner THEN e1.public_uuid WHEN me2.is_winner THEN e2.public_uuid END
            FROM tourn.stages s JOIN tourn.competitions c ON c.competition_id=s.competition_id JOIN tourn.matches m ON m.stage_id=s.stage_id
            LEFT JOIN tourn.match_entrants me1 ON me1.match_id=m.match_id AND me1.slot=1 LEFT JOIN tourn.entrants e1 ON e1.entrant_id=me1.entrant_id
            LEFT JOIN tourn.match_entrants me2 ON me2.match_id=m.match_id AND me2.slot=2 LEFT JOIN tourn.entrants e2 ON e2.entrant_id=me2.entrant_id
            LEFT JOIN tourn.matches wt ON wt.match_id=m.winner_to_match_id LEFT JOIN tourn.matches lt ON lt.match_id=m.loser_to_match_id
            LEFT JOIN tourn.venue_tables table_item ON table_item.venue_table_id=m.venue_table_id
            WHERE c.public_uuid=@id AND s.draw_revision=@revision ORDER BY s.stage_order,m.round_number,m.position
            """, connection); command.Parameters.AddWithValue("id", competitionId); command.Parameters.AddWithValue("revision", revision);
        await using var reader = await command.ExecuteReaderAsync(token); while (await reader.ReadAsync(token)) { var roundId = reader.GetGuid(0).ToString(); if (!rounds.ContainsKey(roundId)) { rounds[roundId] = new(); names[roundId] = (reader.GetString(1), Title(reader.GetString(5)), reader.GetInt32(3)); } Participant? one = reader.IsDBNull(11) ? null : new Participant(reader.GetGuid(11), reader.GetString(12), reader.IsDBNull(13) ? null : reader.GetInt32(13), reader.IsDBNull(14) ? null : reader.GetString(14)); Participant? two = reader.IsDBNull(16) ? null : new Participant(reader.GetGuid(16), reader.GetString(17), reader.IsDBNull(18) ? null : reader.GetInt32(18), reader.IsDBNull(19) ? null : reader.GetString(19)); rounds[roundId].Add(new BracketMatch(reader.GetString(4), Title(reader.GetString(5)), reader.GetInt32(6), reader.GetInt32(7), reader.GetString(8), one, two, reader.IsDBNull(15) ? null : reader.GetInt32(15), reader.IsDBNull(20) ? null : reader.GetInt32(20), ParseEnum<MatchStatus>(reader.GetString(9)), reader.IsDBNull(23) ? null : reader.GetString(23), reader.IsDBNull(21) ? null : reader.GetString(21), reader.IsDBNull(22) ? null : reader.GetString(22), reader.GetString(9) == "conditional", reader.GetInt32(10), reader.IsDBNull(24) ? null : reader.GetGuid(24))); }
        return rounds.Select(pair => new BracketRound(pair.Key, names[pair.Key].Name, names[pair.Key].Bracket, names[pair.Key].Number, pair.Value)).ToArray();
    }

    // Writes all stages, stable match keys, routes, and opening slots inside the publication transaction.
    private static async Task PersistDrawAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, long competitionId, int revision, CompetitionFormat competitionFormat, IReadOnlyList<BracketRound> rounds, CancellationToken token)
    {
        var entrantIds = new Dictionary<Guid, long>(); await using (var q = new NpgsqlCommand("SELECT public_uuid,entrant_id FROM tourn.entrants WHERE competition_id=@id", connection, transaction)) { q.Parameters.AddWithValue("id", competitionId); await using var r = await q.ExecuteReaderAsync(token); while (await r.ReadAsync(token)) entrantIds[r.GetGuid(0)] = r.GetInt64(1); }
        var matchIds = new Dictionary<string, long>(); var stageOrder = 0; foreach (var round in rounds) { stageOrder++; long stageId; await using (var stage = new NpgsqlCommand("INSERT INTO tourn.stages(public_uuid,competition_id,name,format,stage_order,draw_revision) VALUES(@uuid,@competition,@name,@format,@order,@revision) RETURNING stage_id", connection, transaction)) { stage.Parameters.AddWithValue("uuid", Guid.NewGuid()); stage.Parameters.AddWithValue("competition", competitionId); stage.Parameters.AddWithValue("name", round.Name); stage.Parameters.AddWithValue("format", DbEnum(competitionFormat)); stage.Parameters.AddWithValue("order", stageOrder); stage.Parameters.AddWithValue("revision", revision); stageId = (long)(await stage.ExecuteScalarAsync(token))!; } foreach (var match in round.Matches) { await using var cmd = new NpgsqlCommand("INSERT INTO tourn.matches(public_uuid,stage_id,bracket_key,bracket_side,round_number,position,label,status) VALUES(@uuid,@stage,@key,@side,@round,@position,@label,@status) RETURNING match_id", connection, transaction); cmd.Parameters.AddWithValue("uuid", Guid.NewGuid()); cmd.Parameters.AddWithValue("stage", stageId); cmd.Parameters.AddWithValue("key", match.Id); cmd.Parameters.AddWithValue("side", match.Bracket == "Round robin" ? "round_robin" : match.Bracket.ToLowerInvariant()); cmd.Parameters.AddWithValue("round", match.Round); cmd.Parameters.AddWithValue("position", match.Position); cmd.Parameters.AddWithValue("label", match.Label); cmd.Parameters.AddWithValue("status", DbEnum(match.Status)); matchIds[match.Id] = (long)(await cmd.ExecuteScalarAsync(token))!; } }
        foreach (var match in rounds.SelectMany(r => r.Matches)) { await using (var links = new NpgsqlCommand("UPDATE tourn.matches SET winner_to_match_id=@winner,loser_to_match_id=@loser WHERE match_id=@id", connection, transaction)) { links.Parameters.AddWithValue("winner", match.WinnerTo is null ? DBNull.Value : matchIds[match.WinnerTo]); links.Parameters.AddWithValue("loser", match.LoserTo is null ? DBNull.Value : matchIds[match.LoserTo]); links.Parameters.AddWithValue("id", matchIds[match.Id]); await links.ExecuteNonQueryAsync(token); } for (short slot = 1; slot <= 2; slot++) { var p = slot == 1 ? match.EntrantOne : match.EntrantTwo; await using var entry = new NpgsqlCommand("INSERT INTO tourn.match_entrants(match_id,slot,entrant_id) VALUES(@match,@slot,@entrant)", connection, transaction); entry.Parameters.AddWithValue("match", matchIds[match.Id]); entry.Parameters.AddWithValue("slot", slot); entry.Parameters.AddWithValue("entrant", p is null ? DBNull.Value : entrantIds[p.Id]); await entry.ExecuteNonQueryAsync(token); } }
    }

    // Routes winner and loser identities through database-owned match links before the result commits.
    private static async Task AdvanceResultAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, long matchId, long winner, long loser, CancellationToken token)
    {
        await using (var final = new NpgsqlCommand("""
            SELECT match_item.bracket_key,match_item.winner_to_match_id,
              (SELECT entrant.entrant_id FROM tourn.match_entrants entrant
               JOIN tourn.matches source ON source.match_id=entrant.source_match_id
               WHERE entrant.match_id=match_item.match_id AND source.bracket_side='winners' LIMIT 1)
            FROM tourn.matches match_item WHERE match_item.match_id=@match
            """, connection, transaction))
        {
            final.Parameters.AddWithValue("match", matchId);
            await using var reader = await final.ExecuteReaderAsync(token);
            if (await reader.ReadAsync(token) && reader.GetString(0) == "GF1")
            {
                long? resetMatchId = reader.IsDBNull(1) ? null : reader.GetInt64(1);
                long? winnersChampion = reader.IsDBNull(2) ? null : reader.GetInt64(2);
                await reader.DisposeAsync();
                if (resetMatchId.HasValue && winnersChampion != winner)
                {
                    await using var activate = new NpgsqlCommand("""
                        UPDATE tourn.match_entrants reset_slot
                        SET entrant_id=final_slot.entrant_id,source_match_id=@match
                        FROM tourn.match_entrants final_slot
                        WHERE reset_slot.match_id=@reset AND final_slot.match_id=@match AND reset_slot.slot=final_slot.slot;
                        UPDATE tourn.matches SET status='ready' WHERE match_id=@reset;
                        """, connection, transaction);
                    activate.Parameters.AddWithValue("match", matchId); activate.Parameters.AddWithValue("reset", resetMatchId.Value);
                    await activate.ExecuteNonQueryAsync(token);
                }
                return;
            }
        }
        await using var command = new NpgsqlCommand("""
            WITH routes AS (
              SELECT 1 AS route_order,winner_to_match_id AS target,@winner::bigint AS entrant FROM tourn.matches WHERE match_id=@match AND winner_to_match_id IS NOT NULL
              UNION ALL
              SELECT 2,loser_to_match_id,@loser::bigint FROM tourn.matches WHERE match_id=@match AND loser_to_match_id IS NOT NULL
            ), ranked_routes AS (
              SELECT target,entrant,row_number() OVER (PARTITION BY target ORDER BY route_order) AS open_order FROM routes
            ), open_slots AS (
              SELECT targets.target,slot,row_number() OVER (PARTITION BY targets.target ORDER BY slot) AS open_order
              FROM (SELECT DISTINCT target FROM routes) targets
              CROSS JOIN generate_series(1,2) slot
              WHERE NOT EXISTS (SELECT 1 FROM tourn.match_entrants occupied WHERE occupied.match_id=targets.target AND occupied.slot=slot AND occupied.entrant_id IS NOT NULL)
            ), assignments AS (
              SELECT route.target,route.entrant,open_slot.slot FROM ranked_routes route
              JOIN open_slots open_slot ON open_slot.target=route.target AND open_slot.open_order=route.open_order
            )
            UPDATE tourn.match_entrants target SET entrant_id=assignment.entrant,source_match_id=@match
            FROM assignments assignment WHERE target.match_id=assignment.target AND target.slot=assignment.slot
            """, connection, transaction); command.Parameters.AddWithValue("match", matchId); command.Parameters.AddWithValue("winner", winner); command.Parameters.AddWithValue("loser", loser); await command.ExecuteNonQueryAsync(token);
        await using var ready = new NpgsqlCommand("""
            UPDATE tourn.matches target SET status='ready'
            WHERE target.match_id IN (SELECT winner_to_match_id FROM tourn.matches WHERE match_id=@match UNION SELECT loser_to_match_id FROM tourn.matches WHERE match_id=@match)
              AND target.status='waiting'
              AND 2=(SELECT count(*) FROM tourn.match_entrants me WHERE me.match_id=target.match_id AND me.entrant_id IS NOT NULL)
            """, connection, transaction);
        ready.Parameters.AddWithValue("match", matchId);
        await ready.ExecuteNonQueryAsync(token);
        await NormalizeDatabaseByesAsync(connection, transaction, token);
    }

    // Advances a lone entrant only after every possible source of its match has resolved, preventing odd fields from deadlocking.
    private static async Task NormalizeDatabaseByesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken token)
    {
        while (true)
        {
            var candidates = new List<(long MatchId, long? WinnerTo, long? Entrant)>();
            await using (var read = new NpgsqlCommand("""
                SELECT target.match_id,target.winner_to_match_id,min(slot.entrant_id)
                FROM tourn.matches target
                LEFT JOIN tourn.match_entrants slot ON slot.match_id=target.match_id AND slot.entrant_id IS NOT NULL
                WHERE target.status='waiting'
                  AND EXISTS (SELECT 1 FROM tourn.matches source WHERE source.winner_to_match_id=target.match_id OR source.loser_to_match_id=target.match_id)
                  AND NOT EXISTS (SELECT 1 FROM tourn.matches source WHERE (source.winner_to_match_id=target.match_id OR source.loser_to_match_id=target.match_id) AND source.status NOT IN ('complete','bye'))
                GROUP BY target.match_id,target.winner_to_match_id
                HAVING count(slot.entrant_id)<2
                """, connection, transaction))
            {
                await using var reader = await read.ExecuteReaderAsync(token);
                while (await reader.ReadAsync(token)) candidates.Add((reader.GetInt64(0), reader.IsDBNull(1) ? null : reader.GetInt64(1), reader.IsDBNull(2) ? null : reader.GetInt64(2)));
            }
            if (candidates.Count == 0) return;
            foreach (var candidate in candidates)
            {
                await using (var mark = new NpgsqlCommand("UPDATE tourn.matches SET status='bye' WHERE match_id=@match AND status='waiting'", connection, transaction))
                { mark.Parameters.AddWithValue("match", candidate.MatchId); await mark.ExecuteNonQueryAsync(token); }
                if (candidate.WinnerTo.HasValue && candidate.Entrant.HasValue)
                {
                    await using var advance = new NpgsqlCommand("""
                        UPDATE tourn.match_entrants target SET entrant_id=@entrant,source_match_id=@source
                        WHERE target.match_id=@target AND target.slot=COALESCE((SELECT min(slot) FROM generate_series(1,2) slot WHERE NOT EXISTS(SELECT 1 FROM tourn.match_entrants occupied WHERE occupied.match_id=@target AND occupied.slot=slot AND occupied.entrant_id IS NOT NULL)),1)
                        """, connection, transaction);
                    advance.Parameters.AddWithValue("entrant", candidate.Entrant.Value); advance.Parameters.AddWithValue("source", candidate.MatchId); advance.Parameters.AddWithValue("target", candidate.WinnerTo.Value);
                    await advance.ExecuteNonQueryAsync(token);
                }
            }
            await using var ready = new NpgsqlCommand("UPDATE tourn.matches target SET status='ready' WHERE target.status='waiting' AND 2=(SELECT count(*) FROM tourn.match_entrants slot WHERE slot.match_id=target.match_id AND slot.entrant_id IS NOT NULL)", connection, transaction);
            await ready.ExecuteNonQueryAsync(token);
        }
    }

    // Retains a reset revision, clears dependent scores, and removes only entrants routed from the invalidated path.
    private static async Task ResetDescendantsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, long sourceMatchId, string reason, CancellationToken token)
    {
        const string descendants = """
            WITH RECURSIVE affected(match_id) AS (
              SELECT route.target
              FROM tourn.matches source
              CROSS JOIN LATERAL (VALUES (source.winner_to_match_id),(source.loser_to_match_id)) route(target)
              WHERE source.match_id=@source AND route.target IS NOT NULL
              UNION
              SELECT route.target
              FROM affected parent
              JOIN tourn.matches child ON child.match_id=parent.match_id
              CROSS JOIN LATERAL (VALUES (child.winner_to_match_id),(child.loser_to_match_id)) route(target)
              WHERE route.target IS NOT NULL
            )
            """;
        await using (var revisions = new NpgsqlCommand(descendants + """
            INSERT INTO tourn.match_result_revisions(match_id,revision,reason,revision_kind)
            SELECT match_item.match_id,match_item.result_version+1,@reason,'downstream_reset'
            FROM tourn.matches match_item JOIN affected ON affected.match_id=match_item.match_id
            WHERE match_item.result_version>0
            """, connection, transaction))
        { revisions.Parameters.AddWithValue("source", sourceMatchId); revisions.Parameters.AddWithValue("reason", reason); await revisions.ExecuteNonQueryAsync(token); }
        await using (var slots = new NpgsqlCommand(descendants + """
            UPDATE tourn.match_entrants entrant SET entrant_id=NULL,score=NULL,is_winner=false,source_match_id=NULL
            WHERE entrant.source_match_id=@source OR entrant.source_match_id IN (SELECT match_id FROM affected)
            """, connection, transaction))
        { slots.Parameters.AddWithValue("source", sourceMatchId); await slots.ExecuteNonQueryAsync(token); }
        await using (var matches = new NpgsqlCommand(descendants + """
            UPDATE tourn.matches match_item SET
              status=CASE WHEN 2=(SELECT count(*) FROM tourn.match_entrants entrant WHERE entrant.match_id=match_item.match_id AND entrant.entrant_id IS NOT NULL) THEN 'ready' ELSE 'waiting' END,
              called_at=NULL,started_at=NULL,completed_at=NULL,
              result_version=CASE WHEN match_item.result_version>0 THEN match_item.result_version+1 ELSE match_item.result_version END
            FROM affected WHERE match_item.match_id=affected.match_id
            """, connection, transaction))
        { matches.Parameters.AddWithValue("source", sourceMatchId); await matches.ExecuteNonQueryAsync(token); }
    }

    // Maintains one snake-case convention across every constrained PostgreSQL vocabulary.
    private static string DbEnum<T>(T value) where T : Enum => string.Concat(value.ToString().Select((character, index) => char.IsUpper(character) && index > 0 ? "_" + char.ToLowerInvariant(character) : char.ToLowerInvariant(character).ToString()));
    // Fails on storage vocabulary drift instead of mapping an unknown value to a default.
    private static T ParseEnum<T>(string value) where T : struct, Enum => Enum.Parse<T>(string.Concat(value.Split('_').Select(part => char.ToUpperInvariant(part[0]) + part[1..])), true);
    // Converts constrained storage labels to concise organizer-facing text.
    private static string Title(string value) => string.Join(' ', value.Split('_').Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
}
