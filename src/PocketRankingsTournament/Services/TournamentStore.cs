using System.Security.Claims;
using Npgsql;
using PocketRankingsTournament.Models;

namespace PocketRankingsTournament.Services;

public interface ITournamentStore
{
    // Keeps public discovery filtered independently from organizer history.
    Task<TournamentDirectoryViewModel> GetDirectoryAsync(CancellationToken cancellationToken = default);
    // Supplies the complete installation-local inventory only to authorized organizer workflows.
    Task<IReadOnlyList<TournamentEvent>> GetAllAsync(CancellationToken cancellationToken = default);
    // Resolves one event for either a guarded public projection or an organizer workflow.
    Task<TournamentEvent?> FindAsync(Guid id, CancellationToken cancellationToken = default);
    // Creates a private draft and its audit evidence as one logical operation.
    Task<TournamentEvent> CreateAsync(CreateTournamentInput input, ClaimsPrincipal actor, CancellationToken cancellationToken = default);
    // Moves lifecycle state only along approved one-way edges with actor/reason evidence.
    Task<bool> TransitionAsync(TransitionTournamentInput input, ClaimsPrincipal actor, CancellationToken cancellationToken = default);
    // Preserves the full readable state-transition narrative for retained events.
    Task<IReadOnlyList<TournamentStatusChange>> GetStatusHistoryAsync(Guid id, CancellationToken cancellationToken = default);
    // Returns redacted mutation evidence without exposing provider or session identifiers.
    Task<IReadOnlyList<TournamentAuditEntry>> GetAuditAsync(Guid id, CancellationToken cancellationToken = default);
}

public static class TournamentLifecycle
{
    private static readonly IReadOnlyDictionary<TournamentStatus, TournamentStatus[]> Transitions =
        new Dictionary<TournamentStatus, TournamentStatus[]>
        {
            [TournamentStatus.Draft] = new[] { TournamentStatus.RegistrationOpen, TournamentStatus.Archived },
            [TournamentStatus.RegistrationOpen] = new[] { TournamentStatus.CheckIn, TournamentStatus.Archived },
            [TournamentStatus.CheckIn] = new[] { TournamentStatus.InProgress, TournamentStatus.Archived },
            [TournamentStatus.InProgress] = new[] { TournamentStatus.Complete },
            [TournamentStatus.Complete] = new[] { TournamentStatus.Archived },
            [TournamentStatus.Archived] = Array.Empty<TournamentStatus>()
        };

    // Makes state changes one-way so published competition history cannot be silently rewritten as an earlier phase.
    // Provides the UI from the same transition map the mutation boundary enforces.
    public static IReadOnlyList<TournamentStatus> AvailableFrom(TournamentStatus status) => Transitions[status];

    // Rejects backwards or invented state changes even when a request bypasses the rendered controls.
    public static bool CanTransition(TournamentStatus from, TournamentStatus to) =>
        Transitions[from].Contains(to);
}

public static class TournamentScheduling
{
    // Rejects daylight-saving gaps and overlaps instead of silently moving a tournament to an unintended instant.
    public static bool TryResolveLocalStart(CreateTournamentInput input, out DateTimeOffset startsAt)
    {
        startsAt = default;
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(input.TimeZoneId);
            var local = DateTime.SpecifyKind(input.StartsAtLocal, DateTimeKind.Unspecified);
            if (zone.IsInvalidTime(local) || zone.IsAmbiguousTime(local))
            {
                return false;
            }

            startsAt = new DateTimeOffset(local, zone.GetUtcOffset(local));
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }

    // Gives repositories one exact instant after the controller has rejected ambiguous local input.
    public static DateTimeOffset ResolveLocalStart(CreateTournamentInput input) =>
        TryResolveLocalStart(input, out var startsAt)
            ? startsAt
            : throw new ArgumentException("The selected local time or time zone is not valid.", nameof(input));
}

public sealed class DevelopmentTournamentStore : ITournamentStore
{
    private readonly object _sync = new();
    private readonly List<TournamentEvent> _events;
    private readonly Dictionary<Guid, List<TournamentStatusChange>> _history = new();
    private readonly Dictionary<Guid, List<TournamentAuditEntry>> _audit = new();

    // Provides fictional local state only when no PostgreSQL connection exists; Production never registers this store.
    public DevelopmentTournamentStore(BracketBuilder bracketBuilder)
    {
        _events = TournamentFixtures.Create(bracketBuilder).ToList();
        foreach (var tournament in _events)
        {
            _history[tournament.Id] = new List<TournamentStatusChange>();
            _audit[tournament.Id] = new List<TournamentAuditEntry>();
        }
    }

    // Mirrors Production visibility rules while retaining deterministic fictional local fixtures.
    public Task<TournamentDirectoryViewModel> GetDirectoryAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            return Task.FromResult(new TournamentDirectoryViewModel(
                _events.Where(item => item.Status is TournamentStatus.InProgress or TournamentStatus.CheckIn).ToArray(),
                _events.Where(item => item.Status == TournamentStatus.RegistrationOpen).ToArray(),
                _events.Where(item => item.Status == TournamentStatus.Complete).OrderByDescending(item => item.StartsAt).ToArray()));
        }
    }

    // Returns snapshots under the same lock used by mutations so tests never observe a partial transition.
    public Task<IReadOnlyList<TournamentEvent>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            return Task.FromResult<IReadOnlyList<TournamentEvent>>(_events.OrderByDescending(item => item.StartsAt).ToArray());
        }
    }

    // Uses UUID equality to match the external route boundary without exposing local database identities.
    public Task<TournamentEvent?> FindAsync(Guid id, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            return Task.FromResult(_events.SingleOrDefault(item => item.Id == id));
        }
    }

    // Models the same private-draft plus audit transaction used by PostgreSQL for faithful local validation.
    public Task<TournamentEvent> CreateAsync(CreateTournamentInput input, ClaimsPrincipal actor, CancellationToken cancellationToken = default)
    {
        var created = new TournamentEvent(
            Guid.NewGuid(),
            input.Name.Trim(),
            input.Venue.Trim(),
            input.Locality.Trim(),
            TournamentScheduling.ResolveLocalStart(input),
            TournamentStatus.Draft,
            input.Description.Trim(),
            Array.Empty<Competition>(),
            Array.Empty<PayoutDisplay>());

        lock (_sync)
        {
            _events.Add(created);
            _history[created.Id] = new List<TournamentStatusChange>();
            _audit[created.Id] = new List<TournamentAuditEntry>
            {
                NewAudit(actor, "tournament_created", created.Id, "Tournament created as a private draft")
            };
        }

        return Task.FromResult(created);
    }

    // Updates state and both evidence projections under one lock so local history cannot lag the event.
    public Task<bool> TransitionAsync(TransitionTournamentInput input, ClaimsPrincipal actor, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            var index = _events.FindIndex(item => item.Id == input.TournamentId);
            if (index < 0 || !TournamentLifecycle.CanTransition(_events[index].Status, input.ToStatus))
            {
                return Task.FromResult(false);
            }

            var before = _events[index];
            _events[index] = before with { Status = input.ToStatus };
            _history[input.TournamentId].Add(new TournamentStatusChange(
                before.Status,
                input.ToStatus,
                DateTimeOffset.UtcNow,
                ActorName(actor),
                ActorRole(actor),
                input.Reason.Trim()));
            _audit[input.TournamentId].Add(NewAudit(actor, "tournament_status_changed", input.TournamentId, input.Reason.Trim()));
            return Task.FromResult(true);
        }
    }

    // Returns newest-first copies so callers cannot mutate the retained in-memory evidence.
    public Task<IReadOnlyList<TournamentStatusChange>> GetStatusHistoryAsync(Guid id, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            return Task.FromResult<IReadOnlyList<TournamentStatusChange>>(
                _history.TryGetValue(id, out var entries) ? entries.OrderByDescending(item => item.OccurredAt).ToArray() : Array.Empty<TournamentStatusChange>());
        }
    }

    // Returns newest-first redacted copies, matching the durable audit projection.
    public Task<IReadOnlyList<TournamentAuditEntry>> GetAuditAsync(Guid id, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            return Task.FromResult<IReadOnlyList<TournamentAuditEntry>>(
                _audit.TryGetValue(id, out var entries) ? entries.OrderByDescending(item => item.OccurredAt).ToArray() : Array.Empty<TournamentAuditEntry>());
        }
    }

    private static TournamentAuditEntry NewAudit(ClaimsPrincipal actor, string action, Guid targetId, string? reason) =>
        new(DateTimeOffset.UtcNow, ActorName(actor), ActorRole(actor), action, "tournament", targetId, reason);

    private static string ActorName(ClaimsPrincipal actor) => actor.Identity?.Name ?? "Unknown organizer";
    private static string ActorRole(ClaimsPrincipal actor) => actor.FindFirstValue(ClaimTypes.Role) ?? "unknown";
}

public sealed class PostgresTournamentStore : ITournamentStore
{
    private readonly NpgsqlDataSource _dataSource;

    // Keeps all durable Tournament reads and mutations inside this product's isolated PostgreSQL database.
    public PostgresTournamentStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    // Filters public status from the full durable inventory rather than maintaining a separate public database.
    public async Task<TournamentDirectoryViewModel> GetDirectoryAsync(CancellationToken cancellationToken = default)
    {
        var all = await GetAllAsync(cancellationToken);
        return new TournamentDirectoryViewModel(
            all.Where(item => item.Status is TournamentStatus.InProgress or TournamentStatus.CheckIn).ToArray(),
            all.Where(item => item.Status == TournamentStatus.RegistrationOpen).ToArray(),
            all.Where(item => item.Status == TournamentStatus.Complete).OrderByDescending(item => item.StartsAt).ToArray());
    }

    // Projects only public UUIDs and domain values, keeping local bigint identities inside PostgreSQL.
    public async Task<IReadOnlyList<TournamentEvent>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<TournamentEvent>();
        await using var command = _dataSource.CreateCommand("""
            SELECT e.public_uuid, e.name, COALESCE(v.name, ''), COALESCE(v.locality, ''),
                   e.starts_at, e.status, e.description
            FROM tourn.events e
            LEFT JOIN tourn.venues v ON v.venue_id = e.venue_id
            ORDER BY e.starts_at DESC, e.event_id DESC
            """);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new TournamentEvent(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetFieldValue<DateTimeOffset>(4), ParseStatus(reader.GetString(5)), reader.GetString(6),
                Array.Empty<Competition>(), Array.Empty<PayoutDisplay>()));
        }

        return result;
    }

    // Keeps UUID lookup semantics identical between Development and PostgreSQL stores.
    public async Task<TournamentEvent?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        (await GetAllAsync(cancellationToken)).SingleOrDefault(item => item.Id == id);

    // Commits venue, private event, and audit evidence atomically so no unaudited draft can survive.
    public async Task<TournamentEvent> CreateAsync(CreateTournamentInput input, ClaimsPrincipal actor, CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid();
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        long venueId;
        await using (var venue = new NpgsqlCommand("""
            INSERT INTO tourn.venues (public_uuid, name, locality, timezone_id)
            VALUES (@public_uuid, @name, @locality, @timezone_id)
            RETURNING venue_id
            """, connection, transaction))
        {
            venue.Parameters.AddWithValue("public_uuid", Guid.NewGuid());
            venue.Parameters.AddWithValue("name", input.Venue.Trim());
            venue.Parameters.AddWithValue("locality", input.Locality.Trim());
            venue.Parameters.AddWithValue("timezone_id", input.TimeZoneId);
            venueId = (long)(await venue.ExecuteScalarAsync(cancellationToken) ?? throw new InvalidOperationException("Venue creation returned no identity."));
        }

        await using (var create = new NpgsqlCommand("""
            INSERT INTO tourn.events (public_uuid, venue_id, name, description, starts_at, status, visibility)
            VALUES (@public_uuid, @venue_id, @name, @description, @starts_at, 'draft', 'private')
            """, connection, transaction))
        {
            create.Parameters.AddWithValue("public_uuid", id);
            create.Parameters.AddWithValue("venue_id", venueId);
            create.Parameters.AddWithValue("name", input.Name.Trim());
            create.Parameters.AddWithValue("description", input.Description.Trim());
            create.Parameters.AddWithValue("starts_at", TournamentScheduling.ResolveLocalStart(input));
            await create.ExecuteNonQueryAsync(cancellationToken);
        }

        await InsertAuditAsync(connection, transaction, actor, "tournament_created", id, "Tournament created as a private draft", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (await FindAsync(id, cancellationToken))!;
    }

    // Locks the event while checking the one-way edge to prevent concurrent operators from skipping history.
    public async Task<bool> TransitionAsync(TransitionTournamentInput input, ClaimsPrincipal actor, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        string? status;
        await using (var read = new NpgsqlCommand("SELECT status FROM tourn.events WHERE public_uuid = @id FOR UPDATE", connection, transaction))
        {
            read.Parameters.AddWithValue("id", input.TournamentId);
            status = (string?)await read.ExecuteScalarAsync(cancellationToken);
        }

        if (status is null || !TournamentLifecycle.CanTransition(ParseStatus(status), input.ToStatus))
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        var from = ParseStatus(status);
        await using (var update = new NpgsqlCommand("UPDATE tourn.events SET status = @status, visibility = @visibility, updated_at = CURRENT_TIMESTAMP WHERE public_uuid = @id", connection, transaction))
        {
            update.Parameters.AddWithValue("status", DbStatus(input.ToStatus));
            update.Parameters.AddWithValue("visibility", input.ToStatus is TournamentStatus.Draft or TournamentStatus.Archived ? "private" : "public");
            update.Parameters.AddWithValue("id", input.TournamentId);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var history = new NpgsqlCommand("""
            INSERT INTO tourn.event_status_history
                (event_id, from_status, to_status, actor_display_name, actor_role, reason)
            SELECT event_id, @from_status, @to_status, @actor_name, @actor_role, @reason
            FROM tourn.events WHERE public_uuid = @id
            """, connection, transaction))
        {
            history.Parameters.AddWithValue("from_status", DbStatus(from));
            history.Parameters.AddWithValue("to_status", DbStatus(input.ToStatus));
            history.Parameters.AddWithValue("actor_name", ActorName(actor));
            history.Parameters.AddWithValue("actor_role", ActorRole(actor));
            history.Parameters.AddWithValue("reason", input.Reason.Trim());
            history.Parameters.AddWithValue("id", input.TournamentId);
            await history.ExecuteNonQueryAsync(cancellationToken);
        }

        await InsertAuditAsync(connection, transaction, actor, "tournament_status_changed", input.TournamentId, input.Reason.Trim(), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    // Reads immutable lifecycle evidence separately from the mutable current-event projection.
    public async Task<IReadOnlyList<TournamentStatusChange>> GetStatusHistoryAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var result = new List<TournamentStatusChange>();
        await using var command = _dataSource.CreateCommand("""
            SELECT h.from_status, h.to_status, h.occurred_at, h.actor_display_name, h.actor_role, h.reason
            FROM tourn.event_status_history h
            JOIN tourn.events e ON e.event_id = h.event_id
            WHERE e.public_uuid = @id ORDER BY h.occurred_at DESC, h.event_status_history_id DESC
            """);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new TournamentStatusChange(ParseStatus(reader.GetString(0)), ParseStatus(reader.GetString(1)),
                reader.GetFieldValue<DateTimeOffset>(2), reader.GetString(3), reader.GetString(4), reader.GetString(5)));
        }
        return result;
    }

    // Exposes only the redacted fields organizers need to understand a mutation.
    public async Task<IReadOnlyList<TournamentAuditEntry>> GetAuditAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var result = new List<TournamentAuditEntry>();
        await using var command = _dataSource.CreateCommand("""
            SELECT occurred_at, COALESCE(actor_display_name, 'Unknown organizer'), actor_role,
                   action, target_type, target_uuid, reason
            FROM audit.entries WHERE target_uuid = @id ORDER BY occurred_at DESC, audit_id DESC
            """);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new TournamentAuditEntry(reader.GetFieldValue<DateTimeOffset>(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4), reader.GetGuid(5), reader.IsDBNull(6) ? null : reader.GetString(6)));
        }
        return result;
    }

    // Shares the caller's transaction so a mutation cannot commit without its audit evidence.
    private static async Task InsertAuditAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, ClaimsPrincipal actor,
        string action, Guid targetId, string? reason, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO audit.entries
                (actor_role, actor_display_name, action, target_type, target_uuid, reason, request_id, source)
            VALUES (@actor_role, @actor_name, @action, 'tournament', @target_uuid, @reason, @request_id, 'web')
            """, connection, transaction);
        command.Parameters.AddWithValue("actor_role", ActorRole(actor));
        command.Parameters.AddWithValue("actor_name", ActorName(actor));
        command.Parameters.AddWithValue("action", action);
        command.Parameters.AddWithValue("target_uuid", targetId);
        command.Parameters.AddWithValue("reason", (object?)reason ?? DBNull.Value);
        command.Parameters.AddWithValue("request_id", Guid.NewGuid().ToString("N"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // Fails on schema/application vocabulary drift rather than quietly assigning the wrong lifecycle state.
    private static TournamentStatus ParseStatus(string value) => value switch
    {
        "draft" => TournamentStatus.Draft,
        "registration_open" => TournamentStatus.RegistrationOpen,
        "check_in" => TournamentStatus.CheckIn,
        "in_progress" => TournamentStatus.InProgress,
        "complete" => TournamentStatus.Complete,
        "archived" => TournamentStatus.Archived,
        _ => throw new InvalidOperationException($"Unknown tournament status '{value}'.")
    };

    // Maintains the explicit snake-case contract documented in the PostgreSQL schema.
    private static string DbStatus(TournamentStatus status) => status switch
    {
        TournamentStatus.RegistrationOpen => "registration_open",
        TournamentStatus.CheckIn => "check_in",
        TournamentStatus.InProgress => "in_progress",
        _ => status.ToString().ToLowerInvariant()
    };

    private static string ActorName(ClaimsPrincipal actor) => actor.Identity?.Name ?? "Unknown organizer";
    private static string ActorRole(ClaimsPrincipal actor) => actor.FindFirstValue(ClaimTypes.Role) ?? "unknown";
}

internal static class TournamentFixtures
{
    // Keeps all deterministic public examples in one source shared by the development store and its tests.
    public static IReadOnlyList<TournamentEvent> Create(BracketBuilder bracketBuilder)
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

        var competition = new Competition(Guid.Parse("20000000-0000-0000-0000-000000000001"), "Open 9-Ball",
            PoolDiscipline.NineBall, "9-ball", CompetitionFormat.DoubleElimination, EntrantType.Singles, 5, 4, true,
            "Local room rules · alternating break", participants, rounds);

        return new[]
        {
            new TournamentEvent(Guid.Parse("30000000-0000-0000-0000-000000000001"), "Saturday Night Community Open",
                "Corner Pocket Billiards", "Riverton", new DateTimeOffset(2026, 9, 13, 18, 30, 0, TimeSpan.FromHours(-4)),
                TournamentStatus.InProgress, "An approachable weekly tournament for local players of every skill level.",
                new[] { competition }, new[] { new PayoutDisplay(1, "Champion", 240m), new PayoutDisplay(2, "Runner-up", 120m), new PayoutDisplay(3, "Third place", 40m) }),
            new TournamentEvent(Guid.Parse("30000000-0000-0000-0000-000000000002"), "Fall 8-Ball Fundraiser",
                "The Break Room", "Lakeview", new DateTimeOffset(2026, 9, 20, 13, 0, 0, TimeSpan.FromHours(-4)),
                TournamentStatus.RegistrationOpen, "A friendly benefit event with a short race and open registration.",
                Array.Empty<Competition>(), Array.Empty<PayoutDisplay>())
        };
    }
}
