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
    IReadOnlyList<TournamentTable> Tables,
    IReadOnlyList<TournamentStatusChange> StatusHistory,
    IReadOnlyList<TournamentAuditEntry> AuditEntries,
    IReadOnlyList<TournamentStatus> AvailableTransitions);

// Bounds the operational choices that define a bracket before registrations begin.
public sealed class CreateCompetitionInput
{
    public Guid TournamentId { get; set; }

    [Required, StringLength(160, MinimumLength = 1)]
    public string Name { get; set; } = "";

    public PoolDiscipline Discipline { get; set; } = PoolDiscipline.EightBall;
    public EntrantType EntrantType { get; set; } = EntrantType.Singles;
    public CompetitionFormat Format { get; set; } = CompetitionFormat.DoubleElimination;

    [Range(1, 100)]
    public int WinnersRaceTo { get; set; } = 5;

    [Range(1, 100)]
    public int? LosersRaceTo { get; set; } = 4;

    public bool UsesHandicap { get; set; }
    public bool BracketResetEnabled { get; set; } = true;

    [Required, StringLength(300, MinimumLength = 1)]
    public string RulesLabel { get; set; } = "Local room rules · alternating break";
}

// Registers a local competitive identity without requiring an Account or contact record.
public sealed class RegisterEntrantInput
{
    public Guid TournamentId { get; set; }
    public Guid CompetitionId { get; set; }

    [Required, StringLength(160, MinimumLength = 1)]
    public string DisplayName { get; set; } = "";

    [Range(1, 4096)]
    public int? Seed { get; set; }

    [StringLength(80)]
    public string? Handicap { get; set; }

    [EnumDataType(typeof(RegistrationStatus))]
    public RegistrationStatus Status { get; set; } = RegistrationStatus.Registered;
}

// Allows check-in, waitlist, withdrawal, and disqualification without deleting the entrant record.
public sealed class UpdateEntrantStatusInput
{
    public Guid TournamentId { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid EntrantId { get; set; }

    [EnumDataType(typeof(RegistrationStatus))]
    public RegistrationStatus ToStatus { get; set; }

    [Required, StringLength(500, MinimumLength = 3)]
    public string Reason { get; set; } = "";
}

// Adds a named physical table to the event venue without coupling it to another product.
public sealed class CreateTournamentTableInput
{
    public Guid TournamentId { get; set; }

    [Required, StringLength(80, MinimumLength = 1)]
    public string Name { get; set; } = "";
}

// Assigns the current draw's stable match key to one active venue table.
public sealed class AssignMatchTableInput
{
    public Guid TournamentId { get; set; }
    public Guid CompetitionId { get; set; }

    [Required, StringLength(40, MinimumLength = 1)]
    public string MatchId { get; set; } = "";

    public Guid TableId { get; set; }
}

// Requires an organizer explanation before a competition becomes retained result history.
public sealed class CompleteCompetitionInput
{
    public Guid TournamentId { get; set; }
    public Guid CompetitionId { get; set; }

    [Required, StringLength(500, MinimumLength = 3)]
    public string Reason { get; set; } = "";
}

// Captures the publication decision that freezes an auditable draw revision.
public sealed class PublishDrawInput
{
    public Guid TournamentId { get; set; }
    public Guid CompetitionId { get; set; }
    public DrawStrategy Strategy { get; set; } = DrawStrategy.Seeded;

    [Required, StringLength(500, MinimumLength = 3)]
    public string Reason { get; set; } = "";
}

// Uses an expected version to prevent two scorekeepers from silently overwriting one another.
public sealed class RecordMatchResultInput
{
    public Guid TournamentId { get; set; }
    public Guid CompetitionId { get; set; }

    [Required, StringLength(40, MinimumLength = 1)]
    public string MatchId { get; set; } = "";

    [Range(0, 1000)]
    public int EntrantOneScore { get; set; }

    [Range(0, 1000)]
    public int EntrantTwoScore { get; set; }

    public MatchOutcome Outcome { get; set; }
    [Range(0, int.MaxValue)]
    public int ExpectedVersion { get; set; }

    [Required, StringLength(500, MinimumLength = 3)]
    public string Reason { get; set; } = "";
}

public sealed record OperationResult(bool Succeeded, string Message)
{
    // Standardizes success/failure messaging across in-memory and PostgreSQL implementations.
    public static OperationResult Success(string message) => new(true, message);
    public static OperationResult Failure(string message) => new(false, message);
}

// Requires an explicit reason whenever a tournament crosses a public or historical boundary.
public sealed class TransitionTournamentInput
{
    public Guid TournamentId { get; set; }
    public TournamentStatus ToStatus { get; set; }

    [Required, StringLength(500, MinimumLength = 3)]
    public string Reason { get; set; } = "";
}
