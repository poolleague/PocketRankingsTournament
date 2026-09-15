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
    // Changes public discoverability independently from the one-way operational lifecycle.
    Task<OperationResult> UpdateVisibilityAsync(UpdateTournamentVisibilityInput input, ClaimsPrincipal actor, CancellationToken cancellationToken = default);
    // Returns only safe live-link metadata; the usable code is never recoverable from storage.
    Task<LiveTournamentLink?> GetLiveLinkAsync(Guid eventId, CancellationToken cancellationToken = default);
    // Rotates any earlier venue link and reveals the newly generated code exactly once.
    Task<LiveTournamentLinkActivationResult> ActivateLiveLinkAsync(ActivateLiveTournamentLinkInput input, ClaimsPrincipal actor, CancellationToken cancellationToken = default);
    // Revokes the current live link without changing permanent tournament history.
    Task<OperationResult> DeactivateLiveLinkAsync(DeactivateLiveTournamentLinkInput input, ClaimsPrincipal actor, CancellationToken cancellationToken = default);
    // Resolves a valid anonymous venue code without widening normal tournament visibility.
    Task<TournamentEvent?> FindByLiveCodeAsync(string code, CancellationToken cancellationToken = default);
    // Preserves the full readable state-transition narrative for retained events.
    Task<IReadOnlyList<TournamentStatusChange>> GetStatusHistoryAsync(Guid id, CancellationToken cancellationToken = default);
    // Returns redacted mutation evidence without exposing provider or session identifiers.
    Task<IReadOnlyList<TournamentAuditEntry>> GetAuditAsync(Guid id, CancellationToken cancellationToken = default);
    // Creates one pool competition without widening access to other events or products.
    Task<OperationResult> CreateCompetitionAsync(CreateCompetitionInput input, ClaimsPrincipal actor, CancellationToken cancellationToken = default);
    // Adds a product-local entrant while registration remains editable.
    Task<OperationResult> RegisterEntrantAsync(RegisterEntrantInput input, ClaimsPrincipal actor, CancellationToken cancellationToken = default);
    // Freezes a versioned draw from the validated entrant inventory.
    Task<OperationResult> PublishDrawAsync(PublishDrawInput input, ClaimsPrincipal actor, CancellationToken cancellationToken = default);
    // Appends a versioned result and advances its bracket routes atomically.
    Task<OperationResult> RecordMatchResultAsync(RecordMatchResultInput input, ClaimsPrincipal actor, CancellationToken cancellationToken = default);
    // Changes registration/check-in state without erasing the entrant.
    Task<OperationResult> UpdateEntrantStatusAsync(UpdateEntrantStatusInput input, ClaimsPrincipal actor, CancellationToken cancellationToken = default);
    // Returns the event venue's active operational table inventory.
    Task<IReadOnlyList<TournamentTable>> GetTablesAsync(Guid eventId, CancellationToken cancellationToken = default);
    // Adds one venue table for match assignment.
    Task<OperationResult> CreateTableAsync(CreateTournamentTableInput input, ClaimsPrincipal actor, CancellationToken cancellationToken = default);
    // Assigns a current-draw match to one venue table.
    Task<OperationResult> AssignMatchTableAsync(AssignMatchTableInput input, ClaimsPrincipal actor, CancellationToken cancellationToken = default);
    // Moves an assigned current-draw match through ready, called, and in-progress floor states.
    Task<OperationResult> UpdateMatchStatusAsync(UpdateMatchStatusInput input, ClaimsPrincipal actor, CancellationToken cancellationToken = default);
    // Finalizes a competition only after all required bracket matches have results.
    Task<OperationResult> CompleteCompetitionAsync(CompleteCompetitionInput input, ClaimsPrincipal actor, CancellationToken cancellationToken = default);
    // Creates or corrects an informational payout row before the draw is published.
    Task<OperationResult> UpsertPayoutDisplayAsync(UpsertPayoutDisplayInput input, ClaimsPrincipal actor, CancellationToken cancellationToken = default);
    // Replaces a linked player with an installation-local anonymous identity without breaking brackets or results.
    Task<PlayerDataAnonymizationResult> AnonymizePlayerDataAsync(PlayerDataAnonymizationDirective directive, CancellationToken cancellationToken = default);
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

public sealed partial class DevelopmentTournamentStore : ITournamentStore
{
    private readonly object _sync = new();
    private readonly List<TournamentEvent> _events;
    private readonly Dictionary<Guid, List<TournamentStatusChange>> _history = new();
    private readonly Dictionary<Guid, List<TournamentAuditEntry>> _audit = new();
    private readonly Dictionary<Guid, List<TournamentTable>> _tables = new();
    private readonly Dictionary<Guid, (LiveTournamentLink Link, byte[] Hash)> _liveLinks = new();
    private readonly HashSet<string> _issuedLiveLinkHashes = new(StringComparer.Ordinal);
    private readonly HashSet<Guid> _privacyRequests = [];
    private readonly BracketBuilder _bracketBuilder;

    // Provides fictional local state only when no PostgreSQL connection exists; Production never registers this store.
    public DevelopmentTournamentStore(BracketBuilder bracketBuilder)
    {
        _bracketBuilder = bracketBuilder;
        _events = TournamentFixtures.Create(bracketBuilder).ToList();
        foreach (var tournament in _events)
        {
            _history[tournament.Id] = new List<TournamentStatusChange>();
            _audit[tournament.Id] = new List<TournamentAuditEntry>();
            _tables[tournament.Id] = tournament.Competitions.Count > 0
                ? Enumerable.Range(1, 4).Select(index => new TournamentTable(Guid.NewGuid(), $"Table {index}", index)).ToList()
                : new List<TournamentTable>();
        }
    }

    // Mirrors Production visibility rules while retaining deterministic fictional local fixtures.
    public Task<TournamentDirectoryViewModel> GetDirectoryAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            return Task.FromResult(new TournamentDirectoryViewModel(
                _events.Where(item => item.Visibility == TournamentVisibility.Public && item.Status is TournamentStatus.InProgress or TournamentStatus.CheckIn).ToArray(),
                _events.Where(item => item.Visibility == TournamentVisibility.Public && item.Status == TournamentStatus.RegistrationOpen).ToArray(),
                _events.Where(item => item.Visibility == TournamentVisibility.Public && item.Status == TournamentStatus.Complete).OrderByDescending(item => item.StartsAt).ToArray()));
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
            Array.Empty<PayoutDisplay>(),
            TournamentVisibility.Private);

        lock (_sync)
        {
            _events.Add(created);
            _history[created.Id] = new List<TournamentStatusChange>();
            _audit[created.Id] = new List<TournamentAuditEntry>
            {
                NewAudit(actor, "tournament_created", created.Id, "Tournament created as a private draft")
            };
            _tables[created.Id] = new List<TournamentTable>();
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
            if (input.ToStatus == TournamentStatus.CheckIn && _events[index].Competitions.Count == 0)
            {
                return Task.FromResult(false);
            }
            if (input.ToStatus == TournamentStatus.InProgress
                && (_events[index].Competitions.Count == 0 || _events[index].Competitions.Any(item => item.DrawRevision == 0)))
            {
                return Task.FromResult(false);
            }
            if (input.ToStatus == TournamentStatus.Complete
                && (_events[index].Competitions.Count == 0 || _events[index].Competitions.Any(item => item.Status != CompetitionStatus.Complete)))
            {
                return Task.FromResult(false);
            }

            var before = _events[index];
            _events[index] = before with { Status = input.ToStatus };
            if (input.ToStatus == TournamentStatus.Archived && _liveLinks.TryGetValue(input.TournamentId, out var live))
            {
                _liveLinks[input.TournamentId] = (live.Link with { RevokedAt = DateTimeOffset.UtcNow }, live.Hash);
            }
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

    // Updates discoverability with audit evidence while keeping archived records out of public circulation.
    public Task<OperationResult> UpdateVisibilityAsync(UpdateTournamentVisibilityInput input, ClaimsPrincipal actor, CancellationToken cancellationToken = default)
    {
        if (!DevelopmentTournamentStore.CanManage(actor, input.TournamentId)) return Task.FromResult(OperationResult.Failure("Visibility access is unavailable."));
        lock (_sync)
        {
            var index = _events.FindIndex(item => item.Id == input.TournamentId);
            if (index < 0 || _events[index].Status == TournamentStatus.Archived)
                return Task.FromResult(OperationResult.Failure("Archived tournaments cannot be republished."));
            _events[index] = _events[index] with { Visibility = input.ToVisibility };
            _audit[input.TournamentId].Add(NewAudit(actor, "tournament_visibility_changed", input.TournamentId, input.Reason.Trim()));
            return Task.FromResult(OperationResult.Success($"Tournament visibility changed to {input.ToVisibility.ToString().ToLowerInvariant()}."));
        }
    }

    // Keeps the raw venue code out of organizer history after its one-time reveal.
    public Task<LiveTournamentLink?> GetLiveLinkAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            return Task.FromResult(_liveLinks.TryGetValue(eventId, out var stored) ? stored.Link : null);
        }
    }

    // Mirrors the durable rotation transaction while preventing even theoretical duplicate development codes.
    public Task<LiveTournamentLinkActivationResult> ActivateLiveLinkAsync(ActivateLiveTournamentLinkInput input, ClaimsPrincipal actor, CancellationToken cancellationToken = default)
    {
        if (!CanManage(actor, input.TournamentId)) return Task.FromResult(LiveTournamentLinkActivationResult.Failure("Live-link access is unavailable."));
        if (input.LifetimeHours is < 1 or > 168) return Task.FromResult(LiveTournamentLinkActivationResult.Failure("Live links must last between 1 and 168 hours."));
        lock (_sync)
        {
            var tournament = _events.SingleOrDefault(item => item.Id == input.TournamentId);
            if (tournament is null || tournament.Status is TournamentStatus.Draft or TournamentStatus.Archived)
                return Task.FromResult(LiveTournamentLinkActivationResult.Failure("Open registration before activating a live link; archived tournaments cannot be reactivated."));

            string code;
            byte[] hash;
            do
            {
                code = LiveTournamentLinks.GenerateCode();
                LiveTournamentLinks.TryHash(code, out hash);
            } while (_issuedLiveLinkHashes.Contains(Convert.ToHexString(hash)));

            var now = DateTimeOffset.UtcNow;
            var link = new LiveTournamentLink(Guid.NewGuid(), input.TournamentId, code[^4..], now, now.AddHours(input.LifetimeHours));
            _liveLinks[input.TournamentId] = (link, hash);
            _issuedLiveLinkHashes.Add(Convert.ToHexString(hash));
            _audit[input.TournamentId].Add(NewAudit(actor, "live_link_activated", input.TournamentId, input.Reason.Trim()));
            return Task.FromResult(LiveTournamentLinkActivationResult.Success("Live tournament link activated. Copy or print it now; rotating it invalidates the earlier address.", code));
        }
    }

    // Ends anonymous venue access immediately while preserving the link metadata and audit record.
    public Task<OperationResult> DeactivateLiveLinkAsync(DeactivateLiveTournamentLinkInput input, ClaimsPrincipal actor, CancellationToken cancellationToken = default)
    {
        if (!CanManage(actor, input.TournamentId)) return Task.FromResult(OperationResult.Failure("Live-link access is unavailable."));
        lock (_sync)
        {
            if (!_liveLinks.TryGetValue(input.TournamentId, out var stored) || stored.Link.RevokedAt is not null)
                return Task.FromResult(OperationResult.Failure("No active live tournament link was found."));
            _liveLinks[input.TournamentId] = (stored.Link with { RevokedAt = DateTimeOffset.UtcNow }, stored.Hash);
            _audit[input.TournamentId].Add(NewAudit(actor, "live_link_deactivated", input.TournamentId, input.Reason.Trim()));
            return Task.FromResult(OperationResult.Success("Live tournament link deactivated."));
        }
    }

    // Applies code, time, and lifecycle checks together so expired or archived links reveal no event details.
    public Task<TournamentEvent?> FindByLiveCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        if (!LiveTournamentLinks.TryHash(code, out var hash)) return Task.FromResult<TournamentEvent?>(null);
        lock (_sync)
        {
            var stored = _liveLinks.Values.SingleOrDefault(item => item.Hash.SequenceEqual(hash));
            if (stored.Link is null || !stored.Link.IsActiveAt(DateTimeOffset.UtcNow)) return Task.FromResult<TournamentEvent?>(null);
            var tournament = _events.SingleOrDefault(item => item.Id == stored.Link.TournamentId);
            return Task.FromResult(tournament is { Status: not TournamentStatus.Draft and not TournamentStatus.Archived } ? tournament : null);
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

    // Development has no Account links; it still models idempotent delivery without inventing cross-product identity.
    public Task<PlayerDataAnonymizationResult> AnonymizePlayerDataAsync(PlayerDataAnonymizationDirective directive, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            var duplicate = !_privacyRequests.Add(directive.RequestId);
            return Task.FromResult(new PlayerDataAnonymizationResult(directive.RequestId, true, duplicate, 0));
        }
    }
}

public sealed partial class PostgresTournamentStore : ITournamentStore
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly TournamentPrivacy _privacy;

    // Keeps all durable Tournament reads and mutations inside this product's isolated PostgreSQL database.
    public PostgresTournamentStore(NpgsqlDataSource dataSource, TournamentPrivacy privacy)
    {
        _dataSource = dataSource;
        _privacy = privacy;
    }

    // Anonymizes every linked local participant in one transaction while preserving all competitive foreign keys.
    public async Task<PlayerDataAnonymizationResult> AnonymizePlayerDataAsync(PlayerDataAnonymizationDirective directive, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var prior = new NpgsqlCommand("SELECT participants_anonymized FROM privacy.anonymization_receipts WHERE request_id=@request", connection, transaction))
        {
            prior.Parameters.AddWithValue("request", directive.RequestId);
            if (await prior.ExecuteScalarAsync(cancellationToken) is int priorCount)
            {
                await transaction.CommitAsync(cancellationToken);
                return new(directive.RequestId, true, true, priorCount);
            }
        }

        var participants = new List<(long Id, string Name)>();
        await using (var find = new NpgsqlCommand("SELECT p.participant_id,p.display_name FROM tourn.participants p JOIN integ.person_links l ON l.participant_id=p.participant_id WHERE l.person_uuid=@person FOR UPDATE", connection, transaction))
        {
            find.Parameters.AddWithValue("person", directive.PersonId);
            await using var reader = await find.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) participants.Add((reader.GetInt64(0), reader.GetString(1)));
        }

        await using (var allowAuditRedaction = new NpgsqlCommand("SELECT set_config('pocketrankings.privacy_anonymization','on',true)", connection, transaction))
            await allowAuditRedaction.ExecuteNonQueryAsync(cancellationToken);
        foreach (var participant in participants)
        {
            var label = _privacy.CreateAnonymousLabel();
            await using (var participantUpdate = new NpgsqlCommand("UPDATE tourn.participants SET public_uuid=@surrogate,display_name=@label,is_active=false WHERE participant_id=@participant", connection, transaction))
            {
                participantUpdate.Parameters.AddWithValue("surrogate", Guid.NewGuid());
                participantUpdate.Parameters.AddWithValue("label", label);
                participantUpdate.Parameters.AddWithValue("participant", participant.Id);
                await participantUpdate.ExecuteNonQueryAsync(cancellationToken);
            }
            await using (var entrantUpdate = new NpgsqlCommand("UPDATE tourn.entrants e SET display_name=CASE WHEN (SELECT count(*) FROM tourn.entrant_members members WHERE members.entrant_id=e.entrant_id)=1 THEN @label ELSE 'Team with ' || @label END WHERE EXISTS (SELECT 1 FROM tourn.entrant_members member WHERE member.entrant_id=e.entrant_id AND member.participant_id=@participant)", connection, transaction))
            {
                entrantUpdate.Parameters.AddWithValue("label", label);
                entrantUpdate.Parameters.AddWithValue("participant", participant.Id);
                await entrantUpdate.ExecuteNonQueryAsync(cancellationToken);
            }
            await using (var redactAudit = new NpgsqlCommand("UPDATE audit.entries SET actor_person_uuid=CASE WHEN actor_person_uuid=@person THEN NULL ELSE actor_person_uuid END, actor_display_name=replace(COALESCE(actor_display_name,''),@old,@label), reason=replace(COALESCE(reason,''),@old,@label), before_json=CASE WHEN before_json IS NULL THEN NULL ELSE replace(replace(before_json::text,@old,@label),@person_text,@label)::jsonb END, after_json=CASE WHEN after_json IS NULL THEN NULL ELSE replace(replace(after_json::text,@old,@label),@person_text,@label)::jsonb END WHERE actor_person_uuid=@person OR actor_display_name LIKE '%' || @old || '%' OR reason LIKE '%' || @old || '%' OR before_json::text LIKE '%' || @old || '%' OR after_json::text LIKE '%' || @old || '%'", connection, transaction))
            {
                redactAudit.Parameters.AddWithValue("person", directive.PersonId);
                redactAudit.Parameters.AddWithValue("person_text", directive.PersonId.ToString());
                redactAudit.Parameters.AddWithValue("old", participant.Name);
                redactAudit.Parameters.AddWithValue("label", label);
                await redactAudit.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        await using (var unlink = new NpgsqlCommand("DELETE FROM integ.person_links WHERE person_uuid=@person", connection, transaction))
        {
            unlink.Parameters.AddWithValue("person", directive.PersonId);
            await unlink.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var suppression = new NpgsqlCommand("INSERT INTO privacy.identity_suppressions(suppression_hash,first_request_id) VALUES (@hash,@request) ON CONFLICT (suppression_hash) DO NOTHING", connection, transaction))
        {
            suppression.Parameters.AddWithValue("hash", _privacy.Hash(directive.PersonId));
            suppression.Parameters.AddWithValue("request", directive.RequestId);
            await suppression.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var receipt = new NpgsqlCommand("INSERT INTO privacy.anonymization_receipts(request_id,token_id,participants_anonymized) VALUES (@request,@token,@count)", connection, transaction))
        {
            receipt.Parameters.AddWithValue("request", directive.RequestId);
            receipt.Parameters.AddWithValue("token", directive.TokenId);
            receipt.Parameters.AddWithValue("count", participants.Count);
            await receipt.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return new(directive.RequestId, true, false, participants.Count);
    }

    // Filters public status from the full durable inventory rather than maintaining a separate public database.
    public async Task<TournamentDirectoryViewModel> GetDirectoryAsync(CancellationToken cancellationToken = default)
    {
        var all = await GetAllAsync(cancellationToken);
        return new TournamentDirectoryViewModel(
            all.Where(item => item.Visibility == TournamentVisibility.Public && item.Status is TournamentStatus.InProgress or TournamentStatus.CheckIn).ToArray(),
            all.Where(item => item.Visibility == TournamentVisibility.Public && item.Status == TournamentStatus.RegistrationOpen).ToArray(),
            all.Where(item => item.Visibility == TournamentVisibility.Public && item.Status == TournamentStatus.Complete).OrderByDescending(item => item.StartsAt).ToArray());
    }

    // Projects only public UUIDs and domain values, keeping local bigint identities inside PostgreSQL.
    public async Task<IReadOnlyList<TournamentEvent>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<TournamentEvent>();
        await using var command = _dataSource.CreateCommand("""
            SELECT e.public_uuid, e.name, COALESCE(v.name, ''), COALESCE(v.locality, ''),
                   e.starts_at, e.status, e.description, e.visibility
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
                Array.Empty<Competition>(), Array.Empty<PayoutDisplay>(), ParseVisibility(reader.GetString(7))));
        }

        return result;
    }

    // Keeps UUID lookup semantics identical between Development and PostgreSQL stores.
    public async Task<TournamentEvent?> FindAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var found = (await GetAllAsync(cancellationToken)).SingleOrDefault(item => item.Id == id);
        return found is null ? null : found with { Competitions = await LoadCompetitionsAsync(id, cancellationToken) };
    }

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

        if (input.ToStatus is TournamentStatus.CheckIn or TournamentStatus.InProgress or TournamentStatus.Complete)
        {
            await using var readiness = new NpgsqlCommand("""
                SELECT count(*) > 0
                   AND (@requires_draw = false OR bool_and(current_draw_revision > 0))
                   AND (@requires_complete = false OR bool_and(status = 'complete'))
                FROM tourn.competitions
                WHERE event_id = (SELECT event_id FROM tourn.events WHERE public_uuid = @id)
                """, connection, transaction);
            readiness.Parameters.AddWithValue("id", input.TournamentId);
            readiness.Parameters.AddWithValue("requires_draw", input.ToStatus == TournamentStatus.InProgress);
            readiness.Parameters.AddWithValue("requires_complete", input.ToStatus == TournamentStatus.Complete);
            if (await readiness.ExecuteScalarAsync(cancellationToken) is not true)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }
        }

        var from = ParseStatus(status);
        await using (var update = new NpgsqlCommand("UPDATE tourn.events SET status = @status, visibility = @visibility, updated_at = CURRENT_TIMESTAMP WHERE public_uuid = @id", connection, transaction))
        {
            update.Parameters.AddWithValue("status", DbStatus(input.ToStatus));
            update.Parameters.AddWithValue("visibility", input.ToStatus is TournamentStatus.Draft or TournamentStatus.Archived ? "private" : "public");
            update.Parameters.AddWithValue("id", input.TournamentId);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        if (input.ToStatus == TournamentStatus.Archived)
        {
            // Archival ends every venue-display address in the same transaction as the irreversible lifecycle move.
            await using var revokeLinks = new NpgsqlCommand("""
                UPDATE tourn.event_live_links SET revoked_at=CURRENT_TIMESTAMP
                WHERE event_id=(SELECT event_id FROM tourn.events WHERE public_uuid=@id) AND revoked_at IS NULL
                """, connection, transaction);
            revokeLinks.Parameters.AddWithValue("id", input.TournamentId);
            await revokeLinks.ExecuteNonQueryAsync(cancellationToken);
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

    // Locks the event while changing discoverability so audit evidence cannot lag the visible state.
    public async Task<OperationResult> UpdateVisibilityAsync(UpdateTournamentVisibilityInput input, ClaimsPrincipal actor, CancellationToken cancellationToken = default)
    {
        if (!DevelopmentTournamentStore.CanManage(actor, input.TournamentId)) return OperationResult.Failure("Visibility access is unavailable.");
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("UPDATE tourn.events SET visibility=@visibility,updated_at=CURRENT_TIMESTAMP WHERE public_uuid=@id AND status<>'archived'", connection, transaction);
        command.Parameters.AddWithValue("visibility", DbVisibility(input.ToVisibility)); command.Parameters.AddWithValue("id", input.TournamentId);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1) { await transaction.RollbackAsync(cancellationToken); return OperationResult.Failure("Tournament not found or already archived."); }
        await InsertAuditAsync(connection, transaction, actor, "tournament_visibility_changed", input.TournamentId, input.Reason.Trim(), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return OperationResult.Success($"Tournament visibility changed to {input.ToVisibility.ToString().ToLowerInvariant()}.");
    }

    // Projects only the non-secret suffix and lifecycle timestamps required by the organizer page.
    public async Task<LiveTournamentLink?> GetLiveLinkAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        await using var command = _dataSource.CreateCommand("""
            SELECT l.public_uuid, l.code_hint, l.activated_at, l.expires_at, l.revoked_at
            FROM tourn.event_live_links l
            JOIN tourn.events e ON e.event_id = l.event_id
            WHERE e.public_uuid = @id
            ORDER BY l.activated_at DESC, l.event_live_link_id DESC
            LIMIT 1
            """);
        command.Parameters.AddWithValue("id", eventId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new LiveTournamentLink(reader.GetGuid(0), eventId, reader.GetString(1).Trim(), reader.GetFieldValue<DateTimeOffset>(2),
                reader.GetFieldValue<DateTimeOffset>(3), reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4))
            : null;
    }

    // Revokes the previous address and inserts one collision-checked hash in the same audited transaction.
    public async Task<LiveTournamentLinkActivationResult> ActivateLiveLinkAsync(ActivateLiveTournamentLinkInput input, ClaimsPrincipal actor, CancellationToken cancellationToken = default)
    {
        if (!DevelopmentTournamentStore.CanManage(actor, input.TournamentId)) return LiveTournamentLinkActivationResult.Failure("Live-link access is unavailable.");
        if (input.LifetimeHours is < 1 or > 168) return LiveTournamentLinkActivationResult.Failure("Live links must last between 1 and 168 hours.");
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        long eventId;
        string status;
        await using (var read = new NpgsqlCommand("SELECT event_id,status FROM tourn.events WHERE public_uuid=@id FOR UPDATE", connection, transaction))
        {
            read.Parameters.AddWithValue("id", input.TournamentId);
            await using var reader = await read.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return LiveTournamentLinkActivationResult.Failure("Tournament not found.");
            eventId = reader.GetInt64(0); status = reader.GetString(1);
        }
        if (ParseStatus(status) is TournamentStatus.Draft or TournamentStatus.Archived)
            return LiveTournamentLinkActivationResult.Failure("Open registration before activating a live link; archived tournaments cannot be reactivated.");

        await using (var revoke = new NpgsqlCommand("UPDATE tourn.event_live_links SET revoked_at=CURRENT_TIMESTAMP WHERE event_id=@event_id AND revoked_at IS NULL", connection, transaction))
        {
            revoke.Parameters.AddWithValue("event_id", eventId);
            await revoke.ExecuteNonQueryAsync(cancellationToken);
        }

        string? code = null;
        for (var attempt = 0; attempt < 5 && code is null; attempt++)
        {
            var candidate = LiveTournamentLinks.GenerateCode();
            LiveTournamentLinks.TryHash(candidate, out var hash);
            await using var insert = new NpgsqlCommand("""
                INSERT INTO tourn.event_live_links (public_uuid,event_id,token_hash,code_hint,expires_at)
                VALUES (@uuid,@event_id,@hash,@hint,CURRENT_TIMESTAMP + (@hours * INTERVAL '1 hour'))
                ON CONFLICT (token_hash) DO NOTHING
                RETURNING event_live_link_id
                """, connection, transaction);
            insert.Parameters.AddWithValue("uuid", Guid.NewGuid()); insert.Parameters.AddWithValue("event_id", eventId);
            insert.Parameters.AddWithValue("hash", hash); insert.Parameters.AddWithValue("hint", candidate[^4..]); insert.Parameters.AddWithValue("hours", input.LifetimeHours);
            if (await insert.ExecuteScalarAsync(cancellationToken) is not null) code = candidate;
        }
        if (code is null) { await transaction.RollbackAsync(cancellationToken); return LiveTournamentLinkActivationResult.Failure("A unique live link could not be generated. Try again."); }
        await InsertAuditAsync(connection, transaction, actor, "live_link_activated", input.TournamentId, input.Reason.Trim(), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return LiveTournamentLinkActivationResult.Success("Live tournament link activated. Copy or print it now; rotating it invalidates the earlier address.", code);
    }

    // Revokes only this event's current address and commits matching evidence atomically.
    public async Task<OperationResult> DeactivateLiveLinkAsync(DeactivateLiveTournamentLinkInput input, ClaimsPrincipal actor, CancellationToken cancellationToken = default)
    {
        if (!DevelopmentTournamentStore.CanManage(actor, input.TournamentId)) return OperationResult.Failure("Live-link access is unavailable.");
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var update = new NpgsqlCommand("""
            UPDATE tourn.event_live_links SET revoked_at=CURRENT_TIMESTAMP
            WHERE event_id=(SELECT event_id FROM tourn.events WHERE public_uuid=@id) AND revoked_at IS NULL
            """, connection, transaction);
        update.Parameters.AddWithValue("id", input.TournamentId);
        if (await update.ExecuteNonQueryAsync(cancellationToken) == 0) return OperationResult.Failure("No active live tournament link was found.");
        await InsertAuditAsync(connection, transaction, actor, "live_link_deactivated", input.TournamentId, input.Reason.Trim(), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return OperationResult.Success("Live tournament link deactivated.");
    }

    // Matches a one-way hash and enforces expiry and event lifecycle inside PostgreSQL before loading public details.
    public async Task<TournamentEvent?> FindByLiveCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        if (!LiveTournamentLinks.TryHash(code, out var hash)) return null;
        await using var command = _dataSource.CreateCommand("""
            SELECT e.public_uuid
            FROM tourn.event_live_links l
            JOIN tourn.events e ON e.event_id=l.event_id
            WHERE l.token_hash=@hash AND l.revoked_at IS NULL AND l.expires_at>CURRENT_TIMESTAMP
              AND e.status NOT IN ('draft','archived')
            """);
        command.Parameters.AddWithValue("hash", hash);
        var id = await command.ExecuteScalarAsync(cancellationToken);
        return id is Guid eventId ? await FindAsync(eventId, cancellationToken) : null;
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

    private static TournamentVisibility ParseVisibility(string value) => value switch
    {
        "private" => TournamentVisibility.Private,
        "unlisted" => TournamentVisibility.Unlisted,
        "public" => TournamentVisibility.Public,
        _ => throw new InvalidOperationException($"Unknown tournament visibility '{value}'.")
    };

    private static string DbVisibility(TournamentVisibility visibility) => visibility.ToString().ToLowerInvariant();

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
                0 => match with { EntrantOneScore = 5, EntrantTwoScore = 2, Status = MatchStatus.Complete, TableName = "Table 1", ResultVersion = 1, WinnerId = match.EntrantOne!.Id },
                1 => match with { EntrantOneScore = 5, EntrantTwoScore = 4, Status = MatchStatus.Complete, TableName = "Table 3", ResultVersion = 1, WinnerId = match.EntrantOne!.Id },
                2 => match with { Status = MatchStatus.InProgress, TableName = "Table 2" },
                _ => match with { Status = MatchStatus.Called, TableName = "Table 4" }
            }).ToArray()
        };

        var competition = new Competition(Guid.Parse("20000000-0000-0000-0000-000000000001"), "Open 9-Ball",
            PoolDiscipline.NineBall, "9-ball", CompetitionFormat.DoubleElimination, EntrantType.Singles, 5, 4, true,
            "Local room rules · alternating break", participants, rounds,
            CompetitionStatus.InProgress, 1, new DateTimeOffset(2026, 9, 13, 18, 0, 0, TimeSpan.FromHours(-4)));

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
