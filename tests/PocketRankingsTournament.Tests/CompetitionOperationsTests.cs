using System.Security.Claims;
using PocketRankingsTournament.Models;
using PocketRankingsTournament.Security;
using PocketRankingsTournament.Services;

namespace PocketRankingsTournament.Tests;

// Exercises the highest-risk draw, advancement, history, concurrency, and assignment boundaries.
public sealed class CompetitionOperationsTests
{
    [Fact]
    // Covers the volunteer workflow from an empty event through an auditable bracket publication.
    public async Task OrganizerCanCreateRegisterAndPublishBracket()
    {
        var (store, eventId, competitionId) = await CreateCompetitionWithEntrantsAsync(4);

        var result = await store.PublishDrawAsync(new PublishDrawInput
        {
            TournamentId = eventId,
            CompetitionId = competitionId,
            Strategy = DrawStrategy.Seeded,
            Reason = "Field and seeds verified"
        }, Principal(TournamentRoles.Owner));

        Assert.True(result.Succeeded);
        var competition = (await store.FindAsync(eventId))!.Competitions.Single();
        Assert.Equal(CompetitionStatus.Drawn, competition.Status);
        Assert.Equal(1, competition.DrawRevision);
        Assert.Equal(new[] { 2, 1 }, competition.Rounds.Select(round => round.Matches.Count));
        Assert.Contains((await store.GetAuditAsync(eventId)), entry => entry.Action == "draw_published");
    }

    [Fact]
    // Prevents a partially seeded manual field from being presented as an intentional ordering.
    public async Task ManualDrawRequiresEveryEntrantToHaveUniqueSeed()
    {
        var (store, eventId, competitionId) = await CreateCompetitionWithEntrantsAsync(3, omitLastSeed: true);

        var result = await store.PublishDrawAsync(new PublishDrawInput
        {
            TournamentId = eventId,
            CompetitionId = competitionId,
            Strategy = DrawStrategy.Manual,
            Reason = "Manual order reviewed"
        }, Assigned(TournamentRoles.TournamentDirector, eventId));

        Assert.False(result.Succeeded);
        Assert.Contains("unique seed", result.Message);
    }

    [Fact]
    // Proves score entry advances the bracket once and stale concurrent submissions do not overwrite it.
    public async Task ResultAdvancesWinnerAndRejectsStaleVersion()
    {
        var (store, eventId, competitionId) = await CreateCompetitionWithEntrantsAsync(4);
        await PublishAsync(store, eventId, competitionId);

        var first = (await store.FindAsync(eventId))!.Competitions.Single().Rounds[0].Matches[0];
        var input = new RecordMatchResultInput
        {
            TournamentId = eventId,
            CompetitionId = competitionId,
            MatchId = first.Id,
            EntrantOneScore = 5,
            EntrantTwoScore = 2,
            ExpectedVersion = 0,
            Reason = "Score confirmed at table"
        };
        var recorded = await store.RecordMatchResultAsync(input, AssignedScorekeeper(eventId, first.Id));
        var stale = await store.RecordMatchResultAsync(input, AssignedScorekeeper(eventId, first.Id));

        Assert.True(recorded.Succeeded);
        Assert.False(stale.Succeeded);
        var competition = (await store.FindAsync(eventId))!.Competitions.Single();
        Assert.Equal(1, competition.Rounds[0].Matches[0].ResultVersion);
        Assert.Equal(first.EntrantOne!.Id, competition.Rounds[^1].Matches[0].EntrantOne?.Id);
    }

    [Fact]
    // Allows score correction with a reason while refusing winner changes that could invalidate later rounds.
    public async Task CorrectionAppendsVersionButCannotChangeWinner()
    {
        var (store, eventId, competitionId) = await CreateCompetitionWithEntrantsAsync(2, format: CompetitionFormat.DoubleElimination);
        await PublishAsync(store, eventId, competitionId);
        var match = (await store.FindAsync(eventId))!.Competitions.Single().Rounds[0].Matches[0];
        await store.RecordMatchResultAsync(Result(eventId, competitionId, match.Id, 5, 2, 0), AssignedScorekeeper(eventId, match.Id));

        var corrected = await store.RecordMatchResultAsync(Result(eventId, competitionId, match.Id, 5, 3, 1), AssignedScorekeeper(eventId, match.Id));
        var changedWinner = await store.RecordMatchResultAsync(Result(eventId, competitionId, match.Id, 3, 5, 2), AssignedScorekeeper(eventId, match.Id));

        Assert.True(corrected.Succeeded);
        Assert.False(changedWinner.Succeeded);
        Assert.Equal(2, (await store.FindAsync(eventId))!.Competitions.Single().Rounds[0].Matches[0].ResultVersion);
    }

    [Fact]
    // Locks the field after publication so a public draw cannot silently gain another entrant.
    public async Task PublishedDrawLocksRegistration()
    {
        var (store, eventId, competitionId) = await CreateCompetitionWithEntrantsAsync(2);
        await store.PublishDrawAsync(new PublishDrawInput
        {
            TournamentId = eventId,
            CompetitionId = competitionId,
            Reason = "Field verified"
        }, Principal(TournamentRoles.Owner));

        var result = await store.RegisterEntrantAsync(new RegisterEntrantInput
        {
            TournamentId = eventId,
            CompetitionId = competitionId,
            DisplayName = "Late Entrant"
        }, Principal(TournamentRoles.Owner));

        Assert.False(result.Succeeded);
        Assert.Contains("locked", result.Message);
    }

    [Fact]
    // Ensures a wildcard assignment cannot be forged by a non-development identity.
    public void EventAssignmentsAreExactOutsideFictionalDevelopment()
    {
        var eventId = Guid.NewGuid();
        var forged = Principal(TournamentRoles.Scorekeeper, new Claim(TournamentClaimTypes.EventAssignment, "*"));
        var assigned = Principal(TournamentRoles.Scorekeeper, new Claim(TournamentClaimTypes.EventAssignment, eventId.ToString()));
        var development = Principal(TournamentRoles.Scorekeeper,
            new Claim(TournamentClaimTypes.EventAssignment, "*"), new Claim("identity_source", "fictional-development-only"));

        Assert.False(TournamentEventAccess.CanAccess(forged, eventId));
        Assert.True(TournamentEventAccess.CanAccess(assigned, eventId));
        Assert.True(TournamentEventAccess.CanAccess(development, eventId));
    }

    [Fact]
    // Protects the store boundary when a caller bypasses controller authorization attributes.
    public async Task UnassignedScorekeeperCannotWriteThroughStoreDirectly()
    {
        var (store, eventId, competitionId) = await CreateCompetitionWithEntrantsAsync(2);
        await PublishAsync(store, eventId, competitionId);
        var match = (await store.FindAsync(eventId))!.Competitions.Single().Rounds[0].Matches[0];

        var result = await store.RecordMatchResultAsync(
            Result(eventId, competitionId, match.Id, 5, 2, 0),
            Principal(TournamentRoles.Scorekeeper));

        Assert.False(result.Succeeded);
        Assert.Equal(0, (await store.FindAsync(eventId))!.Competitions.Single().Rounds[0].Matches[0].ResultVersion);
    }

    [Fact]
    // Confirms waitlist/check-in state is retained and only checked-in entrants form the draw once check-in is used.
    public async Task CheckedInFieldExcludesWaitlistedEntrantFromDraw()
    {
        var (store, eventId, competitionId) = await CreateCompetitionWithEntrantsAsync(3);
        var competition = (await store.FindAsync(eventId))!.Competitions.Single();
        foreach (var entrant in competition.Participants.Take(2))
        {
            Assert.True((await store.UpdateEntrantStatusAsync(new UpdateEntrantStatusInput
            {
                TournamentId = eventId,
                CompetitionId = competitionId,
                EntrantId = entrant.Id,
                ToStatus = RegistrationStatus.CheckedIn,
                Reason = "Player checked in at desk"
            }, Principal(TournamentRoles.Owner))).Succeeded);
        }
        Assert.True((await store.UpdateEntrantStatusAsync(new UpdateEntrantStatusInput
        {
            TournamentId = eventId,
            CompetitionId = competitionId,
            EntrantId = competition.Participants[2].Id,
            ToStatus = RegistrationStatus.Waitlisted,
            Reason = "Field capacity reached"
        }, Principal(TournamentRoles.Owner))).Succeeded);

        await store.PublishDrawAsync(new PublishDrawInput { TournamentId = eventId, CompetitionId = competitionId, Reason = "Checked-in field verified" }, Principal(TournamentRoles.Owner));
        var assigned = (await store.FindAsync(eventId))!.Competitions.Single().Rounds.SelectMany(round => round.Matches)
            .SelectMany(match => new[] { match.EntrantOne, match.EntrantTwo }).Where(item => item is not null).Select(item => item!.Id).Distinct().ToArray();

        Assert.Equal(2, assigned.Length);
        Assert.DoesNotContain(competition.Participants[2].Id, assigned);
    }

    [Fact]
    // Keeps floor assignments tied to stable table and match identities rather than display position.
    public async Task DirectorCanCreateTableAndAssignCurrentMatch()
    {
        var (store, eventId, competitionId) = await CreateCompetitionWithEntrantsAsync(2);
        await store.PublishDrawAsync(new PublishDrawInput { TournamentId = eventId, CompetitionId = competitionId, Reason = "Field verified" }, Principal(TournamentRoles.Owner));
        var director = Assigned(TournamentRoles.TournamentDirector, eventId);
        Assert.True((await store.CreateTableAsync(new CreateTournamentTableInput { TournamentId = eventId, Name = "Feature Table" }, director)).Succeeded);
        var table = Assert.Single(await store.GetTablesAsync(eventId));
        var match = (await store.FindAsync(eventId))!.Competitions.Single().Rounds[0].Matches[0];

        Assert.True((await store.AssignMatchTableAsync(new AssignMatchTableInput
        {
            TournamentId = eventId,
            CompetitionId = competitionId,
            MatchId = match.Id,
            TableId = table.Id
        }, director)).Succeeded);
        Assert.Equal("Feature Table", (await store.FindAsync(eventId))!.Competitions.Single().Rounds[0].Matches[0].TableName);
    }

    [Fact]
    // Prevents an event from entering live scoring before every configured competition has a published draw.
    public async Task TournamentCannotStartWithoutPublishedDraw()
    {
        var (store, eventId, _) = await CreateCompetitionWithEntrantsAsync(2);
        var actor = Principal(TournamentRoles.Owner);
        Assert.True(await store.TransitionAsync(new TransitionTournamentInput { TournamentId = eventId, ToStatus = TournamentStatus.RegistrationOpen, Reason = "Registration reviewed" }, actor));
        Assert.True(await store.TransitionAsync(new TransitionTournamentInput { TournamentId = eventId, ToStatus = TournamentStatus.CheckIn, Reason = "Check-in opened" }, actor));

        Assert.False(await store.TransitionAsync(new TransitionTournamentInput { TournamentId = eventId, ToStatus = TournamentStatus.InProgress, Reason = "Attempted early start" }, actor));
        Assert.Equal(TournamentStatus.CheckIn, (await store.FindAsync(eventId))!.Status);
    }

    [Fact]
    // Requires bracket completion before either the competition or its parent event enters history.
    public async Task CompletedBracketUnlocksCompetitionAndEventCompletion()
    {
        var (store, eventId, competitionId) = await CreateCompetitionWithEntrantsAsync(2, format: CompetitionFormat.DoubleElimination);
        await PublishAsync(store, eventId, competitionId);
        var actor = Principal(TournamentRoles.Owner);
        var winnersMatch = (await store.FindAsync(eventId))!.Competitions.Single().Rounds.SelectMany(round => round.Matches).Single(match => match.Id == "W1M1");
        Assert.True((await store.RecordMatchResultAsync(Result(eventId, competitionId, winnersMatch.Id, 5, 2, 0), actor)).Succeeded);
        var final = (await store.FindAsync(eventId))!.Competitions.Single().Rounds.SelectMany(round => round.Matches).Single(match => match.Id == "GF1");
        Assert.True((await store.RecordMatchResultAsync(Result(eventId, competitionId, final.Id, 5, 3, 0), actor)).Succeeded);

        Assert.True((await store.CompleteCompetitionAsync(new CompleteCompetitionInput
        {
            TournamentId = eventId,
            CompetitionId = competitionId,
            Reason = "Final result reviewed"
        }, actor)).Succeeded);
        Assert.True(await store.TransitionAsync(new TransitionTournamentInput
        {
            TournamentId = eventId,
            ToStatus = TournamentStatus.Complete,
            Reason = "All competitions finalized"
        }, actor));
    }

    // Builds a fresh private event so each workflow test owns all of its mutable state.
    private static async Task<(DevelopmentTournamentStore Store, Guid EventId, Guid CompetitionId)> CreateCompetitionWithEntrantsAsync(
        int count,
        bool omitLastSeed = false,
        CompetitionFormat format = CompetitionFormat.SingleElimination)
    {
        var store = new DevelopmentTournamentStore(new BracketBuilder());
        var created = await store.CreateAsync(new CreateTournamentInput
        {
            Name = "Community Open",
            Venue = "Corner Pocket",
            StartsAtLocal = DateTime.Today.AddDays(10)
        }, Principal(TournamentRoles.Owner));
        var setup = new CreateCompetitionInput
        {
            TournamentId = created.Id,
            Name = "Open 8-Ball",
            Discipline = PoolDiscipline.EightBall,
            Format = format,
            EntrantType = EntrantType.Singles,
            WinnersRaceTo = 5,
            RulesLabel = "Local rules"
        };
        Assert.True((await store.CreateCompetitionAsync(setup, Principal(TournamentRoles.Owner))).Succeeded);
        var competitionId = (await store.FindAsync(created.Id))!.Competitions.Single().Id;
        for (var index = 1; index <= count; index++)
        {
            Assert.True((await store.RegisterEntrantAsync(new RegisterEntrantInput
            {
                TournamentId = created.Id,
                CompetitionId = competitionId,
                DisplayName = $"Player {index}",
                Seed = omitLastSeed && index == count ? null : index
            }, Assigned(TournamentRoles.TournamentDirector, created.Id))).Succeeded);
        }
        return (store, created.Id, competitionId);
    }

    // Keeps the common publication reason valid while individual tests focus on result behavior.
    private static async Task<OperationResult> PublishAsync(DevelopmentTournamentStore store, Guid eventId, Guid competitionId)
    {
        var actor = Principal(TournamentRoles.Owner);
        var result = await store.PublishDrawAsync(new PublishDrawInput { TournamentId = eventId, CompetitionId = competitionId, Reason = "Field verified" }, actor);
        foreach (var status in new[] { TournamentStatus.RegistrationOpen, TournamentStatus.CheckIn, TournamentStatus.InProgress })
        {
            Assert.True(await store.TransitionAsync(new TransitionTournamentInput
            {
                TournamentId = eventId,
                ToStatus = status,
                Reason = "Operational prerequisite verified"
            }, actor));
        }
        return result;
    }

    // Creates versioned score input without hiding which scores each correction supplies.
    private static RecordMatchResultInput Result(Guid eventId, Guid competitionId, string matchId, int one, int two, int version) => new()
    {
        TournamentId = eventId,
        CompetitionId = competitionId,
        MatchId = matchId,
        EntrantOneScore = one,
        EntrantTwoScore = two,
        ExpectedVersion = version,
        Reason = "Score reviewed"
    };

    // Uses fictional claims only; no test identity can become a runtime credential.
    private static ClaimsPrincipal Principal(string role, params Claim[] additional) => new(new ClaimsIdentity(new[]
    {
        new Claim(ClaimTypes.NameIdentifier, $"test-{role}"), new Claim(ClaimTypes.Name, "Fictional Organizer"),
        new Claim(ClaimTypes.Role, role)
    }.Concat(additional), "test"));

    // Models the exact event claim a future verified Account handoff must supply to delegated roles.
    private static ClaimsPrincipal Assigned(string role, Guid eventId) =>
        Principal(role, new Claim(TournamentClaimTypes.EventAssignment, eventId.ToString()));

    // Includes both scopes required by the score-writing storage boundary.
    private static ClaimsPrincipal AssignedScorekeeper(Guid eventId, string matchId) => Principal(
        TournamentRoles.Scorekeeper,
        new Claim(TournamentClaimTypes.EventAssignment, eventId.ToString()),
        new Claim(TournamentClaimTypes.MatchAssignment, matchId));
}
