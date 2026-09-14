using System.Security.Claims;
using System.Globalization;
using Microsoft.AspNetCore.Authorization;

namespace PocketRankingsTournament.Security;

public static class TournamentRoles
{
    public const string Owner = "owner";
    public const string TournamentDirector = "tournament_director";
    public const string Scorekeeper = "scorekeeper";
}

public static class TournamentPolicies
{
    public const string ViewOperations = "tournament.operations.view";
    public const string ManageTournaments = "tournament.manage";
    public const string RecordScores = "tournament.scores.record";
    public const string ManageAccess = "tournament.access.manage";
}

public static class TournamentClaimTypes
{
    public const string Entitlement = "pocketrankings:tournament:entitlement";
    public const string EntitlementExpiresAt = "pocketrankings:tournament:entitlement_expires_at";
    public const string EventAssignment = "pocketrankings:tournament:event";
    public const string MatchAssignment = "pocketrankings:tournament:match";
}

public static class TournamentEventAccess
{
    // Owners administer their isolated installation; delegated roles must carry an explicit event assignment.
    public static bool CanAccess(ClaimsPrincipal user, Guid eventId) =>
        user.IsInRole(TournamentRoles.Owner)
        || user.FindAll(TournamentClaimTypes.EventAssignment).Any(claim =>
            string.Equals(claim.Value, eventId.ToString(), StringComparison.OrdinalIgnoreCase)
            || (claim.Value == "*" && user.HasClaim("identity_source", "fictional-development-only")));
}

public static class TournamentMatchAccess
{
    // Owners/directors may manage the floor; scorekeepers require a stable match-key assignment.
    public static bool CanRecord(ClaimsPrincipal user, string matchId) =>
        user.IsInRole(TournamentRoles.Owner)
        || user.IsInRole(TournamentRoles.TournamentDirector)
        || (user.IsInRole(TournamentRoles.Scorekeeper)
            && user.FindAll(TournamentClaimTypes.MatchAssignment).Any(claim =>
                string.Equals(claim.Value, matchId, StringComparison.Ordinal)
                || (claim.Value == "*" && user.HasClaim("identity_source", "fictional-development-only"))));
}

public static class TournamentEntitlement
{
    // Uses signed-session claims as the future Account boundary; missing, malformed, or expired access always fails closed.
    public static bool IsCurrent(ClaimsPrincipal user, DateTimeOffset now)
    {
        if (!string.Equals(user.FindFirstValue(TournamentClaimTypes.Entitlement), "active", StringComparison.Ordinal))
        {
            return false;
        }

        return DateTimeOffset.TryParse(
                user.FindFirstValue(TournamentClaimTypes.EntitlementExpiresAt),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var expiresAt)
            && expiresAt > now;
    }
}

public static class TournamentAuthorization
{
    // Centralizes the role matrix so controller attributes cannot drift into competing permission rules.
    public static void Configure(AuthorizationOptions options)
    {
        options.AddPolicy(TournamentPolicies.ViewOperations, policy =>
        {
            RequireCurrentEntitlement(policy);
            policy.RequireRole(TournamentRoles.Owner, TournamentRoles.TournamentDirector, TournamentRoles.Scorekeeper);
        });
        options.AddPolicy(TournamentPolicies.ManageTournaments, policy =>
        {
            RequireCurrentEntitlement(policy);
            policy.RequireRole(TournamentRoles.Owner, TournamentRoles.TournamentDirector);
        });
        options.AddPolicy(TournamentPolicies.RecordScores, policy =>
        {
            RequireCurrentEntitlement(policy);
            policy.RequireRole(TournamentRoles.Owner, TournamentRoles.TournamentDirector, TournamentRoles.Scorekeeper);
        });
        options.AddPolicy(TournamentPolicies.ManageAccess, policy =>
        {
            RequireCurrentEntitlement(policy);
            policy.RequireRole(TournamentRoles.Owner);
        });
    }

    private static void RequireCurrentEntitlement(AuthorizationPolicyBuilder policy) =>
        policy.RequireAssertion(context => TournamentEntitlement.IsCurrent(context.User, DateTimeOffset.UtcNow));
}
