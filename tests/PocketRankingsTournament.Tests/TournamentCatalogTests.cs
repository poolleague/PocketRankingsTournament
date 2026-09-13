using PocketRankingsTournament.Models;
using PocketRankingsTournament.Services;

namespace PocketRankingsTournament.Tests;

public sealed class TournamentStoreTests
{
    [Fact]
    public async Task DirectorySeparatesLiveAndRegistrationEvents()
    {
        var directory = await new DevelopmentTournamentStore(new BracketBuilder()).GetDirectoryAsync();

        Assert.Single(directory.Active);
        Assert.Single(directory.Upcoming);
        Assert.Equal(TournamentStatus.InProgress, directory.Active[0].Status);
        Assert.Equal(TournamentStatus.RegistrationOpen, directory.Upcoming[0].Status);
    }

    [Fact]
    public void OwnerFacingLabelsStayReadable()
    {
        Assert.Equal("Double elimination", TournamentLabels.Format(CompetitionFormat.DoubleElimination));
        Assert.Equal("Registration open", TournamentLabels.Status(TournamentStatus.RegistrationOpen));
    }
}
