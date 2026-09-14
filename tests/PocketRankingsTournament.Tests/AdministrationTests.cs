using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using PocketRankingsTournament.Models;
using PocketRankingsTournament.Controllers;
using PocketRankingsTournament.Security;
using PocketRankingsTournament.Services;

namespace PocketRankingsTournament.Tests;

public sealed class AdministrationTests
{
    [Fact]
    // Separates direct-link sharing from directory discovery while retaining private organizer previews.
    public async Task VisibilityChangesAreAuditedAndOnlyPublicEventsAreListed()
    {
        var store = new DevelopmentTournamentStore(new BracketBuilder());
        var actor = Principal(TournamentRoles.Owner);
        var created = await store.CreateAsync(new CreateTournamentInput
        {
            Name = "Private Community Open",
            Venue = "Corner Pocket",
            StartsAtLocal = DateTime.Today.AddDays(7)
        }, actor);
        Assert.Equal(TournamentVisibility.Private, created.Visibility);
        Assert.DoesNotContain((await store.GetDirectoryAsync()).Upcoming, item => item.Id == created.Id);

        Assert.True(await store.TransitionAsync(new TransitionTournamentInput { TournamentId = created.Id, ToStatus = TournamentStatus.RegistrationOpen, Reason = "Registration ready" }, actor));
        Assert.True((await store.UpdateVisibilityAsync(new UpdateTournamentVisibilityInput { TournamentId = created.Id, ToVisibility = TournamentVisibility.Unlisted, Reason = "Direct link only" }, actor)).Succeeded);
        Assert.DoesNotContain((await store.GetDirectoryAsync()).Upcoming, item => item.Id == created.Id);

        Assert.True((await store.UpdateVisibilityAsync(new UpdateTournamentVisibilityInput { TournamentId = created.Id, ToVisibility = TournamentVisibility.Public, Reason = "Directory publication approved" }, actor)).Succeeded);
        Assert.Contains((await store.GetDirectoryAsync()).Upcoming, item => item.Id == created.Id);
        Assert.Contains((await store.GetAuditAsync(created.Id)), entry => entry.Action == "tournament_visibility_changed");
    }

    [Fact]
    // Guards the historical invariant that public events never move back into an editable phase.
    public void LifecycleOnlyMovesForwardThroughPublishedHistory()
    {
        Assert.True(TournamentLifecycle.CanTransition(TournamentStatus.Draft, TournamentStatus.RegistrationOpen));
        Assert.True(TournamentLifecycle.CanTransition(TournamentStatus.InProgress, TournamentStatus.Complete));
        Assert.True(TournamentLifecycle.CanTransition(TournamentStatus.Complete, TournamentStatus.Archived));
        Assert.False(TournamentLifecycle.CanTransition(TournamentStatus.Complete, TournamentStatus.InProgress));
        Assert.Empty(TournamentLifecycle.AvailableFrom(TournamentStatus.Archived));
    }

    [Fact]
    // Proves a create operation starts private and cannot exist without matching evidence.
    public async Task CreateStartsPrivateWorkflowAsDraftAndWritesAuditEvidence()
    {
        var store = new DevelopmentTournamentStore(new BracketBuilder());
        var created = await store.CreateAsync(new CreateTournamentInput
        {
            Name = " Neighborhood Nine Ball ",
            Venue = " Community Room ",
            Locality = " Riverton ",
            StartsAtLocal = new DateTime(2026, 10, 3, 18, 0, 0),
            Description = " Open amateur event "
        }, Principal(TournamentRoles.Owner));

        Assert.Equal(TournamentStatus.Draft, created.Status);
        Assert.Equal("Neighborhood Nine Ball", created.Name);
        var audit = await store.GetAuditAsync(created.Id);
        Assert.Single(audit);
        Assert.Equal("tournament_created", audit[0].Action);
        Assert.Equal(TournamentRoles.Owner, audit[0].ActorRole);
    }

    [Fact]
    // Protects the actor/reason context that makes retained history understandable later.
    public async Task StatusChangesRetainActorReasonAndPreviousState()
    {
        var store = new DevelopmentTournamentStore(new BracketBuilder());
        var created = await store.CreateAsync(new CreateTournamentInput
        {
            Name = "Charity Eight Ball",
            Venue = "The Break Room",
            StartsAtLocal = DateTime.Today.AddDays(5)
        }, Principal(TournamentRoles.TournamentDirector));

        var changed = await store.TransitionAsync(new TransitionTournamentInput
        {
            TournamentId = created.Id,
            ToStatus = TournamentStatus.RegistrationOpen,
            Reason = "Registration page reviewed"
        }, Principal(TournamentRoles.TournamentDirector));

        Assert.True(changed);
        var history = await store.GetStatusHistoryAsync(created.Id);
        Assert.Single(history);
        Assert.Equal(TournamentStatus.Draft, history[0].FromStatus);
        Assert.Equal(TournamentStatus.RegistrationOpen, history[0].ToStatus);
        Assert.Equal("Registration page reviewed", history[0].Reason);
    }

    [Fact]
    // Proves direct service calls cannot bypass the same one-way rule shown in the UI.
    public async Task InvalidBackwardStatusChangeDoesNotAlterHistory()
    {
        var store = new DevelopmentTournamentStore(new BracketBuilder());
        var active = (await store.GetAllAsync()).Single(item => item.Status == TournamentStatus.InProgress);

        var changed = await store.TransitionAsync(new TransitionTournamentInput
        {
            TournamentId = active.Id,
            ToStatus = TournamentStatus.RegistrationOpen,
            Reason = "Attempt to reopen"
        }, Principal(TournamentRoles.Owner));

        Assert.False(changed);
        Assert.Empty(await store.GetStatusHistoryAsync(active.Id));
    }

    [Fact]
    // Keeps score entry permission from accidentally expanding into event or access administration.
    public void ScorekeeperCannotManageTournamentsOrOrganizerAccess()
    {
        var options = new AuthorizationOptions();
        TournamentAuthorization.Configure(options);

        Assert.DoesNotContain(TournamentRoles.Scorekeeper, AllowedRoles(options, TournamentPolicies.ManageTournaments));
        Assert.DoesNotContain(TournamentRoles.Scorekeeper, AllowedRoles(options, TournamentPolicies.ManageAccess));
        Assert.Contains(TournamentRoles.Scorekeeper, AllowedRoles(options, TournamentPolicies.RecordScores));
    }

    [Fact]
    // Protects the owner-only identity administration boundary before real Account integration exists.
    public void OnlyOwnerCanManageOrganizerAccess()
    {
        var options = new AuthorizationOptions();
        TournamentAuthorization.Configure(options);

        Assert.Equal(new[] { TournamentRoles.Owner }, AllowedRoles(options, TournamentPolicies.ManageAccess));
    }

    [Fact]
    // Exercises all malformed entitlement paths because elevated access must fail closed.
    public void EntitlementFailsClosedWhenMissingMalformedOrExpired()
    {
        Assert.False(TournamentEntitlement.IsCurrent(new ClaimsPrincipal(), DateTimeOffset.UtcNow));
        Assert.False(TournamentEntitlement.IsCurrent(Principal(TournamentRoles.Owner, "not-a-date"), DateTimeOffset.UtcNow));
        Assert.False(TournamentEntitlement.IsCurrent(Principal(TournamentRoles.Owner, "2020-01-01T00:00:00Z"), DateTimeOffset.UtcNow));
        Assert.True(TournamentEntitlement.IsCurrent(Principal(TournamentRoles.Owner, "2099-01-01T00:00:00Z"), DateTimeOffset.UtcNow));
    }

    [Fact]
    // Prevents UUID guessing from exposing setup details before an event is deliberately published.
    public async Task PrivateDraftCannotBeOpenedThroughThePublicUuidRoute()
    {
        var store = new DevelopmentTournamentStore(new BracketBuilder());
        var created = await store.CreateAsync(new CreateTournamentInput
        {
            Name = "Private Draft",
            Venue = "Community Room",
            StartsAtLocal = new DateTime(2026, 10, 3, 18, 0, 0)
        }, Principal(TournamentRoles.Owner));
        var controller = new TournamentsController(store);

        var response = await controller.Details(created.Id);

        Assert.IsType<NotFoundResult>(response);
    }

    [Fact]
    // Proves a private event can expose only its read-only live projection without entering the public directory.
    public async Task LiveLinkAllowsAnonymousViewWithoutChangingDirectoryVisibility()
    {
        var store = new DevelopmentTournamentStore(new BracketBuilder());
        var actor = Principal(TournamentRoles.Owner);
        var created = await store.CreateAsync(new CreateTournamentInput
        {
            Name = "Link-only Community Open", Venue = "Corner Pocket", StartsAtLocal = DateTime.Today.AddDays(1)
        }, actor);
        Assert.True(await store.TransitionAsync(new TransitionTournamentInput { TournamentId = created.Id, ToStatus = TournamentStatus.RegistrationOpen, Reason = "Registration ready" }, actor));
        Assert.False((await store.ActivateLiveLinkAsync(new ActivateLiveTournamentLinkInput { TournamentId = created.Id }, Principal(TournamentRoles.Scorekeeper))).Succeeded);
        Assert.False((await store.ActivateLiveLinkAsync(new ActivateLiveTournamentLinkInput { TournamentId = created.Id, LifetimeHours = 169 }, actor)).Succeeded);

        var activation = await store.ActivateLiveLinkAsync(new ActivateLiveTournamentLinkInput { TournamentId = created.Id, LifetimeHours = 24 }, actor);

        Assert.True(activation.Succeeded);
        Assert.NotNull(activation.Code);
        Assert.DoesNotContain((await store.GetDirectoryAsync()).Upcoming, item => item.Id == created.Id);
        Assert.Equal(created.Id, (await store.FindByLiveCodeAsync(activation.Code!))?.Id);
        var metadata = await store.GetLiveLinkAsync(created.Id);
        Assert.NotNull(metadata);
        Assert.Equal(activation.Code![^4..], metadata.CodeHint);
        Assert.DoesNotContain(activation.Code, metadata.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    // Locks rotation, explicit revocation, and archival to the same fail-closed public lookup behavior.
    public async Task RotatedDeactivatedAndArchivedLiveLinksStopResolving()
    {
        var store = new DevelopmentTournamentStore(new BracketBuilder());
        var actor = Principal(TournamentRoles.Owner);
        var created = await store.CreateAsync(new CreateTournamentInput { Name = "Venue Display", Venue = "Pool Room", StartsAtLocal = DateTime.Today.AddDays(1) }, actor);
        Assert.True(await store.TransitionAsync(new TransitionTournamentInput { TournamentId = created.Id, ToStatus = TournamentStatus.RegistrationOpen, Reason = "Registration ready" }, actor));
        var first = await store.ActivateLiveLinkAsync(new ActivateLiveTournamentLinkInput { TournamentId = created.Id }, actor);
        var second = await store.ActivateLiveLinkAsync(new ActivateLiveTournamentLinkInput { TournamentId = created.Id }, actor);
        Assert.Null(await store.FindByLiveCodeAsync(first.Code!));
        Assert.NotNull(await store.FindByLiveCodeAsync(second.Code!));

        Assert.True((await store.DeactivateLiveLinkAsync(new DeactivateLiveTournamentLinkInput { TournamentId = created.Id }, actor)).Succeeded);
        Assert.Null(await store.FindByLiveCodeAsync(second.Code!));
        var third = await store.ActivateLiveLinkAsync(new ActivateLiveTournamentLinkInput { TournamentId = created.Id }, actor);
        Assert.True(await store.TransitionAsync(new TransitionTournamentInput { TournamentId = created.Id, ToStatus = TournamentStatus.Archived, Reason = "Event retained" }, actor));
        Assert.Null(await store.FindByLiveCodeAsync(third.Code!));
        Assert.Contains((await store.GetAuditAsync(created.Id)), entry => entry.Action == "live_link_activated");
        Assert.Contains((await store.GetAuditAsync(created.Id)), entry => entry.Action == "live_link_deactivated");
    }

    [Fact]
    // Rejects malformed locators before lookup and keeps QR rendering local and self-contained.
    public void LiveCodeAndQrContractsAreBounded()
    {
        var codes = Enumerable.Range(0, 256).Select(_ => LiveTournamentLinks.GenerateCode()).ToArray();
        Assert.Equal(codes.Length, codes.Distinct(StringComparer.Ordinal).Count());
        Assert.All(codes, code => Assert.True(LiveTournamentLinks.TryHash(code, out var hash) && hash.Length == 32));
        Assert.False(LiveTournamentLinks.TryHash("guess-me", out _));
        Assert.False(new LiveTournamentLink(Guid.NewGuid(), Guid.NewGuid(), "89AB", DateTimeOffset.UtcNow.AddHours(-2), DateTimeOffset.UtcNow.AddHours(-1)).IsActiveAt(DateTimeOffset.UtcNow));
        Assert.StartsWith("data:image/svg+xml;base64,", LiveTournamentLinks.CreateQrSvgDataUri("https://tournaments.example.test/live/23456789AB"), StringComparison.Ordinal);
        var reveals = new LiveLinkRevealStore();
        var revealId = reveals.Hold(codes[0]);
        Assert.Equal(codes[0], reveals.Take(revealId));
        Assert.Null(reveals.Take(revealId));
        var cachePolicy = Assert.Single(typeof(TournamentsController).GetMethod(nameof(TournamentsController.Live))!
            .GetCustomAttributes(typeof(ResponseCacheAttribute), false).Cast<ResponseCacheAttribute>());
        Assert.True(cachePolicy.NoStore);
    }

    [Fact]
    // Avoids recording an unintended instant when local wall clocks skip or repeat an hour.
    public void DaylightSavingGapAndOverlapRequireAnExplicitDifferentTime()
    {
        Assert.False(TournamentScheduling.TryResolveLocalStart(new CreateTournamentInput
        {
            StartsAtLocal = new DateTime(2026, 3, 8, 2, 30, 0),
            TimeZoneId = "America/New_York"
        }, out _));
        Assert.False(TournamentScheduling.TryResolveLocalStart(new CreateTournamentInput
        {
            StartsAtLocal = new DateTime(2026, 11, 1, 1, 30, 0),
            TimeZoneId = "America/New_York"
        }, out _));
    }

    [Fact]
    // Ensures the fictional role selector cannot become a Production authentication bypass.
    public async Task DevelopmentRoleSelectionReturnsNotFoundInProduction()
    {
        var controller = new DevelopmentAccessController(new TestHostEnvironment(Environments.Production));

        Assert.IsType<NotFoundResult>(controller.Index());
        Assert.IsType<NotFoundResult>(await controller.SignInAs(TournamentRoles.Owner));
    }

    private static ClaimsPrincipal Principal(string role, string entitlementExpiresAt = "2099-01-01T00:00:00Z") => new(new ClaimsIdentity(new[]
    {
        new Claim(ClaimTypes.NameIdentifier, $"test-{role}"),
        new Claim(ClaimTypes.Name, "Fictional Test Organizer"),
        new Claim(ClaimTypes.Role, role),
        new Claim(TournamentClaimTypes.Entitlement, "active"),
        new Claim(TournamentClaimTypes.EntitlementExpiresAt, entitlementExpiresAt)
    }, "test"));

    private static IReadOnlyList<string> AllowedRoles(AuthorizationOptions options, string policyName) =>
        options.GetPolicy(policyName)!.Requirements.OfType<RolesAuthorizationRequirement>().Single().AllowedRoles.ToArray();

    private sealed class TestHostEnvironment(string environmentName) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "PocketRankingsTournament.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = "";
        public string EnvironmentName { get; set; } = environmentName;
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
