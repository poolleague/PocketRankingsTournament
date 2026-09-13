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

public sealed record Participant(Guid Id, string DisplayName, int? Seed = null, string? Handicap = null);

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
    bool IsConditional = false);

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
    IReadOnlyList<BracketRound> Rounds);

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
    IReadOnlyList<PayoutDisplay> Payouts);

public sealed record TournamentDirectoryViewModel(IReadOnlyList<TournamentEvent> Active, IReadOnlyList<TournamentEvent> Upcoming);

public sealed record TournamentDetailViewModel(TournamentEvent Event, Competition Competition);

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
}
