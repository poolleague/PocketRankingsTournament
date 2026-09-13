using System.ComponentModel.DataAnnotations;

namespace PocketRankingsTournament.Models;

// Bounds organizer input before any private draft reaches an in-memory or PostgreSQL store.
public sealed class CreateTournamentInput
{
    [Required, StringLength(180, MinimumLength = 1)]
    public string Name { get; set; } = "";

    [Required, StringLength(160, MinimumLength = 1)]
    public string Venue { get; set; } = "";

    [StringLength(160)]
    public string Locality { get; set; } = "";

    [Required]
    public DateTime StartsAtLocal { get; set; } = DateTime.Today.AddDays(7).AddHours(18);

    [StringLength(2000)]
    public string Description { get; set; } = "";

    public string TimeZoneId { get; set; } = "America/New_York";
}

// Preserves the actor and reason that explain each irreversible lifecycle move.
public sealed record TournamentStatusChange(
    TournamentStatus FromStatus,
    TournamentStatus ToStatus,
    DateTimeOffset OccurredAt,
    string ActorName,
    string ActorRole,
    string Reason);

// Gives organizers a redacted readable projection of the append-only audit evidence.
public sealed record TournamentAuditEntry(
    DateTimeOffset OccurredAt,
    string ActorName,
    string ActorRole,
    string Action,
    string TargetType,
    Guid TargetId,
    string? Reason);

// Separates current work from retained history so volunteer directors can find the next task quickly.
public sealed record OrganizerDashboardViewModel(
    IReadOnlyList<TournamentEvent> Active,
    IReadOnlyList<TournamentEvent> History);

// Keeps lifecycle choices and their supporting evidence together on the event operations page.
public sealed record OrganizerTournamentViewModel(
    TournamentEvent Event,
    IReadOnlyList<TournamentStatusChange> StatusHistory,
    IReadOnlyList<TournamentAuditEntry> AuditEntries,
    IReadOnlyList<TournamentStatus> AvailableTransitions);

// Requires an explicit reason whenever a tournament crosses a public or historical boundary.
public sealed class TransitionTournamentInput
{
    public Guid TournamentId { get; set; }
    public TournamentStatus ToStatus { get; set; }

    [Required, StringLength(500, MinimumLength = 3)]
    public string Reason { get; set; } = "";
}
