using PocketRankingsTournament.Services;

namespace PocketRankingsTournament.Tests;

public sealed class PlayerDataAnonymizationTests
{
    [Fact]
    public void AnonymousLabelsAreReadableAndNotDeterministic()
    {
        var privacy = new TournamentPrivacy("test-only-tournament-privacy-key-12345");
        var first = privacy.CreateAnonymousLabel();
        var second = privacy.CreateAnonymousLabel();

        Assert.StartsWith("Deleted player ", first, StringComparison.Ordinal);
        Assert.NotEqual(first, second);
        Assert.Equal(19, first.Length);
    }

    [Fact]
    public async Task DevelopmentDeliveryIsIdempotentWithoutInventingIdentityLinks()
    {
        var store = new DevelopmentTournamentStore(new BracketBuilder());
        var directive = new PocketRankingsTournament.Models.PlayerDataAnonymizationDirective(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow);
        Assert.False((await store.AnonymizePlayerDataAsync(directive)).Duplicate);
        Assert.True((await store.AnonymizePlayerDataAsync(directive)).Duplicate);
    }
}
