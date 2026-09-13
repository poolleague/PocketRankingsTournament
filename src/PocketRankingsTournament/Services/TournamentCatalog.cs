using PocketRankingsTournament.Models;

namespace PocketRankingsTournament.Services;

public interface ITournamentCatalog
{
    TournamentDirectoryViewModel GetDirectory();
    TournamentEvent? Find(Guid id);
}

public sealed class TournamentCatalog : ITournamentCatalog
{
    private readonly IReadOnlyList<TournamentEvent> _events;

    // Supplies a deterministic, fictional public fixture until the approved organizer-write phase connects PostgreSQL reads.
    public TournamentCatalog(BracketBuilder bracketBuilder)
    {
        var participants = new[]
        {
            new Participant(Guid.Parse("10000000-0000-0000-0000-000000000001"), "Avery Brooks", 1, "525"),
            new Participant(Guid.Parse("10000000-0000-0000-0000-000000000002"), "Jordan Lee", 2, "510"),
            new Participant(Guid.Parse("10000000-0000-0000-0000-000000000003"), "Morgan Diaz", 3, "485"),
            new Participant(Guid.Parse("10000000-0000-0000-0000-000000000004"), "Casey Patel", 4, "470"),
            new Participant(Guid.Parse("10000000-0000-0000-0000-000000000005"), "Riley Chen", 5, "455"),
            new Participant(Guid.Parse("10000000-0000-0000-0000-000000000006"), "Taylor Reed", 6, "440"),
            new Participant(Guid.Parse("10000000-0000-0000-0000-000000000007"), "Sam Rivera", 7, "425"),
            new Participant(Guid.Parse("10000000-0000-0000-0000-000000000008"), "Jamie Quinn", 8, "410")
        };

        var rounds = bracketBuilder.Build(CompetitionFormat.DoubleElimination, participants).ToArray();
        rounds[0] = rounds[0] with
        {
            Matches = rounds[0].Matches.Select((match, index) => index switch
            {
                0 => match with { EntrantOneScore = 5, EntrantTwoScore = 2, Status = MatchStatus.Complete, TableName = "Table 1" },
                1 => match with { EntrantOneScore = 5, EntrantTwoScore = 4, Status = MatchStatus.Complete, TableName = "Table 3" },
                2 => match with { Status = MatchStatus.InProgress, TableName = "Table 2" },
                _ => match with { Status = MatchStatus.Called, TableName = "Table 4" }
            }).ToArray()
        };

        var activeCompetition = new Competition(
            Guid.Parse("20000000-0000-0000-0000-000000000001"),
            "Open 9-Ball",
            PoolDiscipline.NineBall,
            "9-ball",
            CompetitionFormat.DoubleElimination,
            EntrantType.Singles,
            5,
            4,
            true,
            "Local room rules · alternating break",
            participants,
            rounds);

        var active = new TournamentEvent(
            Guid.Parse("30000000-0000-0000-0000-000000000001"),
            "Saturday Night Community Open",
            "Corner Pocket Billiards",
            "Riverton",
            new DateTimeOffset(2026, 9, 13, 18, 30, 0, TimeSpan.FromHours(-4)),
            TournamentStatus.InProgress,
            "An approachable weekly tournament for local players of every skill level.",
            new[] { activeCompetition },
            new[]
            {
                new PayoutDisplay(1, "Champion", 240m),
                new PayoutDisplay(2, "Runner-up", 120m),
                new PayoutDisplay(3, "Third place", 40m)
            });

        var upcoming = new TournamentEvent(
            Guid.Parse("30000000-0000-0000-0000-000000000002"),
            "Fall 8-Ball Fundraiser",
            "The Break Room",
            "Lakeview",
            new DateTimeOffset(2026, 9, 20, 13, 0, 0, TimeSpan.FromHours(-4)),
            TournamentStatus.RegistrationOpen,
            "A friendly benefit event with a short race and open registration.",
            Array.Empty<Competition>(),
            Array.Empty<PayoutDisplay>());

        _events = new[] { active, upcoming };
    }

    public TournamentDirectoryViewModel GetDirectory() => new(
        _events.Where(item => item.Status is TournamentStatus.InProgress or TournamentStatus.CheckIn).ToArray(),
        _events.Where(item => item.Status == TournamentStatus.RegistrationOpen).ToArray());

    public TournamentEvent? Find(Guid id) => _events.SingleOrDefault(item => item.Id == id);
}
