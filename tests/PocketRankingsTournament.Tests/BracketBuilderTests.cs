using PocketRankingsTournament.Models;
using PocketRankingsTournament.Services;

namespace PocketRankingsTournament.Tests;

public sealed class BracketBuilderTests
{
    private readonly BracketBuilder _builder = new();

    [Fact]
    public void SingleEliminationBuildsBalancedRoundsAndFinal()
    {
        var rounds = _builder.Build(CompetitionFormat.SingleElimination, Players(8));

        Assert.Equal(new[] { 4, 2, 1 }, rounds.Select(round => round.Matches.Count));
        Assert.Equal("W2M1", rounds[0].Matches[0].WinnerTo);
        Assert.Equal("Winners final", rounds[^1].Name);
        Assert.Equal("Player 1", rounds[0].Matches[0].EntrantOne?.DisplayName);
        Assert.Equal("Player 8", rounds[0].Matches[0].EntrantTwo?.DisplayName);
    }

    [Fact]
    public void NonPowerOfTwoFieldUsesByesWithoutDuplicateEntrants()
    {
        var rounds = _builder.Build(CompetitionFormat.SingleElimination, Players(5));
        var opening = rounds[0].Matches;

        Assert.Equal(4, opening.Count);
        Assert.Equal(3, opening.Count(match => match.Status == MatchStatus.Bye));
        var assigned = opening.SelectMany(match => new[] { match.EntrantOne, match.EntrantTwo }).Where(player => player is not null).ToArray();
        Assert.Equal(5, assigned.Length);
        Assert.Equal(5, assigned.Select(player => player!.Id).Distinct().Count());
    }

    [Fact]
    public void DoubleEliminationNamesWinnerLoserAndConditionalFinalPaths()
    {
        var rounds = _builder.Build(CompetitionFormat.DoubleElimination, Players(8));
        var matches = rounds.SelectMany(round => round.Matches).ToArray();

        Assert.Equal(15, matches.Length);
        Assert.All(matches.Where(match => match.Bracket == "Winners"), match => Assert.NotNull(match.LoserTo));
        Assert.Contains(matches, match => match.Id == "GF1");
        Assert.Contains(matches, match => match.Id == "GF2" && match.IsConditional && match.Status == MatchStatus.Conditional);
    }

    [Fact]
    // Keeps each winner-side drop in its matching elimination slot instead of colliding in larger fields.
    public void EightPlayerDoubleEliminationRoutesSecondRoundLosersSeparately()
    {
        var matches = _builder.Build(CompetitionFormat.DoubleElimination, Players(8)).SelectMany(round => round.Matches).ToArray();

        Assert.Equal("L2M1", matches.Single(match => match.Id == "W2M1").LoserTo);
        Assert.Equal("L2M2", matches.Single(match => match.Id == "W2M2").LoserTo);
        Assert.Equal(matches.Where(match => match.Id.StartsWith("W2M", StringComparison.Ordinal)).Select(match => match.LoserTo).Count(),
            matches.Where(match => match.Id.StartsWith("W2M", StringComparison.Ordinal)).Select(match => match.LoserTo).Distinct().Count());
    }

    [Fact]
    public void TwoPlayerDoubleEliminationRoutesBothSidesToChampionship()
    {
        var matches = _builder.Build(CompetitionFormat.DoubleElimination, Players(2))
            .SelectMany(round => round.Matches)
            .ToArray();

        Assert.Equal(3, matches.Length);
        Assert.Equal("GF1", matches.Single(match => match.Id == "W1M1").WinnerTo);
        Assert.Equal("GF1", matches.Single(match => match.Id == "W1M1").LoserTo);
    }

    [Fact]
    public void DuplicateParticipantIsRejectedBeforeDraw()
    {
        var player = new Participant(Guid.NewGuid(), "Same player", 1);
        var error = Assert.Throws<ArgumentException>(() => _builder.Build(CompetitionFormat.SingleElimination, new[] { player, player }));
        Assert.Contains("only once", error.Message);
    }

    [Theory]
    [InlineData(4, 3, 6)]
    [InlineData(5, 5, 10)]
    // Proves the circle scheduler creates every pairing once for even and odd fields.
    public void RoundRobinSchedulesEveryPairExactlyOnce(int entrantCount, int expectedRounds, int expectedMatches)
    {
        var players = Players(entrantCount);
        var rounds = _builder.Build(CompetitionFormat.RoundRobin, players);
        var matches = rounds.SelectMany(round => round.Matches).ToArray();

        Assert.Equal(expectedRounds, rounds.Count);
        Assert.Equal(expectedMatches, matches.Length);
        Assert.All(matches, match => Assert.Equal(MatchStatus.Ready, match.Status));
        var pairings = matches.Select(match => string.Join(':', new[] { match.EntrantOne!.Id, match.EntrantTwo!.Id }.Order())).ToArray();
        Assert.Equal(expectedMatches, pairings.Distinct().Count());
        Assert.All(players, player => Assert.Equal(entrantCount - 1, matches.Count(match => match.EntrantOne?.Id == player.Id || match.EntrantTwo?.Id == player.Id)));
    }

    [Theory]
    [InlineData(CompetitionFormat.Swiss)]
    [InlineData(CompetitionFormat.GroupToFinals)]
    public void ModeledLaterFormatsFailClearlyInsteadOfCreatingWrongBrackets(CompetitionFormat format)
    {
        var error = Assert.Throws<NotSupportedException>(() => _builder.Build(format, Players(4)));
        Assert.Contains("later phase", error.Message);
    }

    private static IReadOnlyList<Participant> Players(int count) => Enumerable.Range(1, count)
        .Select(index => new Participant(Guid.NewGuid(), $"Player {index}", index))
        .ToArray();
}
