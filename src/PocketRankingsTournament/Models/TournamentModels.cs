namespace PocketRankingsTournament.Models;

public enum PoolDiscipline
{
    EightBall,
    NineBall,
    TenBall,
    StraightPool,
    OnePocket,
    Banks,
    Blackball,
    Custom
}

public enum CompetitionFormat
{
    SingleElimination,
    DoubleElimination,
    RoundRobin,
    Swiss,
    GroupToFinals
}

public enum EntrantType
{
    Singles,
    Doubles,
    ScotchDoubles,
    Team
}

public enum TournamentStatus
{
    Draft,
    RegistrationOpen,
    CheckIn,
    InProgress,
    Complete,
    Archived
}

public enum TournamentVisibility
{
    Private,
    Unlisted,
    Public
}

public enum MatchStatus
{
    Waiting,
    Ready,
    Called,
    InProgress,
    Complete,
    Bye,
    Conditional
}

public enum CompetitionStatus
{
    Draft,
    RegistrationOpen,
    Drawn,
    InProgress,
    Complete,
    Archived
}

public enum DrawStrategy
{
    Seeded,
    Randomized,
    Manual
}

public enum MatchOutcome
{
    Played,
    EntrantOneForfeit,
    EntrantTwoForfeit,
    EntrantOneNoShow,
    EntrantTwoNoShow
}

public enum RegistrationStatus
{
    Waitlisted,
    Registered,
    CheckedIn,
    Withdrawn,
    Disqualified
}

public sealed record Participant(
    Guid Id,
    string DisplayName,
    int? Seed = null,
    string? Handicap = null,
    RegistrationStatus RegistrationStatus = RegistrationStatus.Registered);

public sealed record TournamentTable(Guid Id, string Name, int SortOrder, bool IsActive = true);

public sealed record BracketMatch(
    string Id,
    string Bracket,
    int Round,
    int Position,
    string Label,
    Participant? EntrantOne,
    Participant? EntrantTwo,
    int? EntrantOneScore,
    int? EntrantTwoScore,
    MatchStatus Status,
    string? TableName = null,
    string? WinnerTo = null,
    string? LoserTo = null,
    bool IsConditional = false,
    int ResultVersion = 0,
    Guid? WinnerId = null);

public sealed record BracketRound(string Id, string Name, string Bracket, int Number, IReadOnlyList<BracketMatch> Matches);

public sealed record Competition(
    Guid Id,
    string Name,
    PoolDiscipline Discipline,
    string DisciplineLabel,
    CompetitionFormat Format,
    EntrantType EntrantType,
    int WinnersRaceTo,
    int? LosersRaceTo,
    bool UsesHandicap,
    string RulesLabel,
    IReadOnlyList<Participant> Participants,
    IReadOnlyList<BracketRound> Rounds,
    CompetitionStatus Status = CompetitionStatus.Draft,
    int DrawRevision = 0,
    DateTimeOffset? DrawPublishedAt = null,
    IReadOnlyList<PayoutDisplay>? Payouts = null);

public sealed record PayoutDisplay(int Place, string Label, decimal Amount);

public sealed record TournamentEvent(
    Guid Id,
    string Name,
    string Venue,
    string Locality,
    DateTimeOffset StartsAt,
    TournamentStatus Status,
    string Description,
    IReadOnlyList<Competition> Competitions,
    IReadOnlyList<PayoutDisplay> Payouts,
    TournamentVisibility Visibility = TournamentVisibility.Public);

public sealed record TournamentDirectoryViewModel(
    IReadOnlyList<TournamentEvent> Active,
    IReadOnlyList<TournamentEvent> Upcoming,
    IReadOnlyList<TournamentEvent> History);

public sealed record TournamentDetailViewModel(TournamentEvent Event, Competition Competition);

// Gives players a deterministic round-robin table derived from retained results rather than a second mutable record.
public sealed record CompetitionStanding(
    int Position,
    Participant Entrant,
    int Played,
    int Wins,
    int Losses,
    int ScoreFor,
    int ScoreAgainst)
{
    public int ScoreDifference => ScoreFor - ScoreAgainst;
}

public static class TournamentStandings
{
    // Ranks completed round-robin results by wins, score difference, score-for, then display name.
    public static IReadOnlyList<CompetitionStanding> Calculate(Competition competition)
    {
        if (competition.Format != CompetitionFormat.RoundRobin)
        {
            return Array.Empty<CompetitionStanding>();
        }

        var rows = competition.Participants
            .Where(participant => participant.RegistrationStatus is RegistrationStatus.Registered or RegistrationStatus.CheckedIn)
            .ToDictionary(participant => participant.Id, participant => new MutableStanding(participant));
        foreach (var match in competition.Rounds.SelectMany(round => round.Matches)
                     .Where(match => match.Status == MatchStatus.Complete
                         && match.EntrantOne is not null && match.EntrantTwo is not null
                         && match.EntrantOneScore.HasValue && match.EntrantTwoScore.HasValue))
        {
            if (!rows.TryGetValue(match.EntrantOne!.Id, out var first) || !rows.TryGetValue(match.EntrantTwo!.Id, out var second))
            {
                continue;
            }

            first.Played++; second.Played++;
            first.ScoreFor += match.EntrantOneScore!.Value; first.ScoreAgainst += match.EntrantTwoScore!.Value;
            second.ScoreFor += match.EntrantTwoScore.Value; second.ScoreAgainst += match.EntrantOneScore.Value;
            var winnerId = match.WinnerId ?? (match.EntrantOneScore > match.EntrantTwoScore ? match.EntrantOne.Id : match.EntrantTwo.Id);
            if (winnerId == match.EntrantOne.Id) { first.Wins++; second.Losses++; }
            else { second.Wins++; first.Losses++; }
        }

        return rows.Values
            .OrderByDescending(row => row.Wins)
            .ThenByDescending(row => row.ScoreFor - row.ScoreAgainst)
            .ThenByDescending(row => row.ScoreFor)
            .ThenBy(row => row.Entrant.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select((row, index) => new CompetitionStanding(index + 1, row.Entrant, row.Played, row.Wins, row.Losses, row.ScoreFor, row.ScoreAgainst))
            .ToArray();
    }

    private sealed class MutableStanding(Participant entrant)
    {
        public Participant Entrant { get; } = entrant;
        public int Played { get; set; }
        public int Wins { get; set; }
        public int Losses { get; set; }
        public int ScoreFor { get; set; }
        public int ScoreAgainst { get; set; }
    }
}

public sealed record CompetitionPlacement(int Place, Participant Entrant, string Label);

public static class TournamentPlacements
{
    // Derives visible finishers from authoritative completed results without creating a second editable ranking source.
    public static IReadOnlyList<CompetitionPlacement> Calculate(Competition competition)
    {
        if (competition.Status != CompetitionStatus.Complete) return Array.Empty<CompetitionPlacement>();
        if (competition.Format == CompetitionFormat.RoundRobin)
            return TournamentStandings.Calculate(competition).Select(row => new CompetitionPlacement(row.Position, row.Entrant, $"Place {row.Position}")).ToArray();

        var matches = competition.Rounds.SelectMany(round => round.Matches).ToArray();
        var final = competition.Format == CompetitionFormat.DoubleElimination
            ? matches.FirstOrDefault(match => match.Id == "GF2" && match.Status == MatchStatus.Complete)
                ?? matches.FirstOrDefault(match => match.Id == "GF1" && match.Status == MatchStatus.Complete)
            : matches.Where(match => match.Bracket == "Winners" && match.Status == MatchStatus.Complete).OrderByDescending(match => match.Round).FirstOrDefault();
        if (final?.WinnerId is null || final.EntrantOne is null || final.EntrantTwo is null) return Array.Empty<CompetitionPlacement>();
        var champion = final.WinnerId == final.EntrantOne.Id ? final.EntrantOne : final.EntrantTwo;
        var runnerUp = champion.Id == final.EntrantOne.Id ? final.EntrantTwo : final.EntrantOne;
        var result = new List<CompetitionPlacement>
        {
            new(1, champion, "Champion"),
            new(2, runnerUp, "Runner-up")
        };
        if (competition.Format == CompetitionFormat.DoubleElimination)
        {
            var eliminationFinal = matches.Where(match => match.Bracket == "Elimination" && match.Status == MatchStatus.Complete).OrderByDescending(match => match.Round).FirstOrDefault();
            if (eliminationFinal?.WinnerId is not null && eliminationFinal.EntrantOne is not null && eliminationFinal.EntrantTwo is not null)
            {
                var third = eliminationFinal.WinnerId == eliminationFinal.EntrantOne.Id ? eliminationFinal.EntrantTwo : eliminationFinal.EntrantOne;
                if (result.All(item => item.Entrant.Id != third.Id)) result.Add(new CompetitionPlacement(3, third, "Third place"));
            }
        }
        return result;
    }
}

public static class TournamentLabels
{
    // Keeps public wording stable while storage uses enum values suitable for contracts and validation.
    public static string Format(CompetitionFormat format) => format switch
    {
        CompetitionFormat.SingleElimination => "Single elimination",
        CompetitionFormat.DoubleElimination => "Double elimination",
        CompetitionFormat.RoundRobin => "Round robin",
        CompetitionFormat.Swiss => "Swiss",
        CompetitionFormat.GroupToFinals => "Group play to finals",
        _ => format.ToString()
    };

    public static string Status(TournamentStatus status) => status switch
    {
        TournamentStatus.RegistrationOpen => "Registration open",
        TournamentStatus.CheckIn => "Check-in",
        TournamentStatus.InProgress => "In progress",
        _ => status.ToString()
    };

    public static string CompetitionStatus(CompetitionStatus status) => status switch
    {
        Models.CompetitionStatus.RegistrationOpen => "Registration open",
        Models.CompetitionStatus.InProgress => "In progress",
        _ => status.ToString()
    };

    // Keeps match-state wording consistent across public brackets and organizer floor controls.
    public static string MatchStatus(MatchStatus status) => status switch
    {
        Models.MatchStatus.InProgress => "In progress",
        _ => status.ToString()
    };
}
