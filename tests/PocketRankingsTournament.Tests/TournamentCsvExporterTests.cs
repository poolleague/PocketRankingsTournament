using PocketRankingsTournament.Models;
using PocketRankingsTournament.Services;

namespace PocketRankingsTournament.Tests;

// Protects portable tournament records from malformed quoting and spreadsheet formula execution.
public sealed class TournamentCsvExporterTests
{
    [Fact]
    public void EntrantExportQuotesFieldsAndNeutralizesFormulaPrefixes()
    {
        var entrant = new Participant(Guid.NewGuid(), "=HYPERLINK(\"bad\")", 1);
        var competition = new Competition(Guid.NewGuid(), "Open, Division", PoolDiscipline.EightBall, "8-ball",
            CompetitionFormat.SingleElimination, EntrantType.Singles, 5, null, false, "Local rules",
            new[] { entrant }, Array.Empty<BracketRound>());
        var tournament = new TournamentEvent(Guid.NewGuid(), "Community Open", "Room", "Town", DateTimeOffset.UtcNow,
            TournamentStatus.Draft, "", new[] { competition }, Array.Empty<PayoutDisplay>());

        var csv = TournamentCsvExporter.Entrants(tournament);

        Assert.Contains("\"Open, Division\"", csv);
        Assert.Contains("\"'=HYPERLINK(\"\"bad\"\")\"", csv);
        Assert.DoesNotContain(",\"=HYPERLINK", csv);
    }

    [Fact]
    public void RoundRobinStandingExportMatchesCalculatedRanking()
    {
        var one = new Participant(Guid.NewGuid(), "One", 1);
        var two = new Participant(Guid.NewGuid(), "Two", 2);
        var match = new BracketMatch("RR1M1", "Round robin", 1, 1, "Round 1", one, two, 5, 3, MatchStatus.Complete, ResultVersion: 1);
        var competition = new Competition(Guid.NewGuid(), "Round robin", PoolDiscipline.NineBall, "9-ball",
            CompetitionFormat.RoundRobin, EntrantType.Singles, 5, null, false, "Local rules",
            new[] { one, two }, new[] { new BracketRound("RR1", "Round 1", "Round robin", 1, new[] { match }) });

        var csv = TournamentCsvExporter.Standings(competition);

        Assert.Contains("\"1\",\"One\",\"1\",\"1\",\"0\",\"5\",\"3\",\"2\"", csv);
    }
}
