using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PocketRankingsTournament.Models;
using PocketRankingsTournament.Security;
using PocketRankingsTournament.Services;

namespace PocketRankingsTournament.Controllers;

[Authorize(Policy = TournamentPolicies.ViewOperations)]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class OrganizerController : Controller
{
    private readonly ITournamentStore _store;
    private readonly LiveLinkRevealStore _liveLinkReveals;

    // Keeps authenticated operations separate from the public read-only tournament surface.
    public OrganizerController(ITournamentStore store, LiveLinkRevealStore liveLinkReveals)
    {
        _store = store;
        _liveLinkReveals = liveLinkReveals;
    }

    [HttpGet("/organizer")]
    // Splits current work from immutable history rather than mixing archived events into the operations queue.
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var all = await _store.GetAllAsync(cancellationToken);
        all = all.Where(item => TournamentEventAccess.CanAccess(User, item.Id)).ToArray();
        return View(new OrganizerDashboardViewModel(
            all.Where(item => item.Status is not TournamentStatus.Complete and not TournamentStatus.Archived).ToArray(),
            all.Where(item => item.Status is TournamentStatus.Complete or TournamentStatus.Archived).ToArray()));
    }

    [Authorize(Policy = TournamentPolicies.ManageTournaments)]
    [HttpGet("/organizer/tournaments/new")]
    // Starts with a bounded draft form so nothing becomes public merely by opening the setup page.
    public IActionResult Create() => View(new CreateTournamentInput());

    [Authorize(Policy = TournamentPolicies.ManageTournaments)]
    [HttpPost("/organizer/tournaments/new")]
    [ValidateAntiForgeryToken]
    // Persists only validated private drafts and keeps local-time ambiguity out of the historical record.
    public async Task<IActionResult> Create(CreateTournamentInput input, CancellationToken cancellationToken)
    {
        if (!TournamentScheduling.TryResolveLocalStart(input, out _))
        {
            ModelState.AddModelError(nameof(input.StartsAtLocal), "Choose an unambiguous local date and time.");
        }
        if (!ModelState.IsValid)
        {
            return View(input);
        }

        var created = await _store.CreateAsync(input, User, cancellationToken);
        TempData["Notice"] = "Tournament created as a private draft.";
        return RedirectToAction(nameof(Details), new { id = created.Id });
    }

    [HttpGet("/organizer/tournaments/{id:guid}")]
    // Co-locates current state, allowed next states, and evidence so operators need no separate resolution screen.
    public async Task<IActionResult> Details(Guid id, CancellationToken cancellationToken)
    {
        var tournament = await _store.FindAsync(id, cancellationToken);
        if (tournament is null)
        {
            return NotFound();
        }
        if (!TournamentEventAccess.CanAccess(User, id))
        {
            return NotFound();
        }

        var revealedCode = TempData["LiveLinkRevealId"] is string revealText && Guid.TryParseExact(revealText, "N", out var revealId)
            ? _liveLinkReveals.Take(revealId)
            : null;
        var revealedUrl = revealedCode is null
            ? null
            : Url.Action("Live", "Tournaments", new { code = revealedCode }, Request.Scheme);
        return View(new OrganizerTournamentViewModel(
            tournament,
            await _store.GetTablesAsync(id, cancellationToken),
            await _store.GetStatusHistoryAsync(id, cancellationToken),
            await _store.GetAuditAsync(id, cancellationToken),
            TournamentLifecycle.AvailableFrom(tournament.Status)
                .Where(next => next != TournamentStatus.CheckIn || tournament.Competitions.Count > 0)
                .Where(next => next != TournamentStatus.InProgress || (tournament.Competitions.Count > 0 && tournament.Competitions.All(item => item.DrawRevision > 0)))
                .Where(next => next != TournamentStatus.Complete || (tournament.Competitions.Count > 0 && tournament.Competitions.All(item => item.Status == CompetitionStatus.Complete)))
                .ToArray(),
            await _store.GetLiveLinkAsync(id, cancellationToken),
            revealedUrl,
            revealedUrl is null ? null : LiveTournamentLinks.CreateQrSvgDataUri(revealedUrl)));
    }

    [Authorize(Policy = TournamentPolicies.ManageTournaments)]
    [HttpPost("/organizer/tournaments/{id:guid}/status")]
    [ValidateAntiForgeryToken]
    // Routes every lifecycle mutation through the one-way store rule and its matching audit transaction.
    public async Task<IActionResult> Transition(Guid id, TransitionTournamentInput input, CancellationToken cancellationToken)
    {
        input.TournamentId = id;
        if (!TournamentEventAccess.CanAccess(User, id) || !ModelState.IsValid || !await _store.TransitionAsync(input, User, cancellationToken))
        {
            TempData["Error"] = "That status change is not allowed.";
        }
        else
        {
            TempData["Notice"] = $"Tournament moved to {TournamentLabels.Status(input.ToStatus)}.";
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Policy = TournamentPolicies.ManageTournaments)]
    [HttpPost("/organizer/tournaments/{id:guid}/visibility")]
    [ValidateAntiForgeryToken]
    // Keeps public sharing explicit and audited instead of coupling it to a lifecycle button.
    public async Task<IActionResult> UpdateVisibility(Guid id, UpdateTournamentVisibilityInput input, CancellationToken cancellationToken)
    {
        input.TournamentId = id;
        var result = TournamentEventAccess.CanAccess(User, id) && ModelState.IsValid
            ? await _store.UpdateVisibilityAsync(input, User, cancellationToken)
            : OperationResult.Failure("Visibility details are incomplete or access is unavailable.");
        TempData[result.Succeeded ? "Notice" : "Error"] = result.Message;
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Policy = TournamentPolicies.ManageTournaments)]
    [HttpPost("/organizer/tournaments/{id:guid}/live-link")]
    [ValidateAntiForgeryToken]
    // Reveals a newly rotated public locator once while retaining only its hash after this response.
    public async Task<IActionResult> ActivateLiveLink(Guid id, ActivateLiveTournamentLinkInput input, CancellationToken cancellationToken)
    {
        input.TournamentId = id;
        var result = TournamentEventAccess.CanAccess(User, id) && ModelState.IsValid
            ? await _store.ActivateLiveLinkAsync(input, User, cancellationToken)
            : LiveTournamentLinkActivationResult.Failure("Live-link details are incomplete or access is unavailable.");
        TempData[result.Succeeded ? "Notice" : "Error"] = result.Message;
        if (result.Succeeded && result.Code is not null) TempData["LiveLinkRevealId"] = _liveLinkReveals.Hold(result.Code).ToString("N");
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Policy = TournamentPolicies.ManageTournaments)]
    [HttpPost("/organizer/tournaments/{id:guid}/live-link/deactivate")]
    [ValidateAntiForgeryToken]
    // Ends anonymous display access independently from permanent tournament history and visibility.
    public async Task<IActionResult> DeactivateLiveLink(Guid id, DeactivateLiveTournamentLinkInput input, CancellationToken cancellationToken)
    {
        input.TournamentId = id;
        var result = TournamentEventAccess.CanAccess(User, id) && ModelState.IsValid
            ? await _store.DeactivateLiveLinkAsync(input, User, cancellationToken)
            : OperationResult.Failure("Live-link details are incomplete or access is unavailable.");
        TempData[result.Succeeded ? "Notice" : "Error"] = result.Message;
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Policy = TournamentPolicies.ManageTournaments)]
    [HttpPost("/organizer/tournaments/{id:guid}/competitions")]
    [ValidateAntiForgeryToken]
    // Keeps setup mutations on the event operations page while enforcing its event assignment at the boundary.
    public async Task<IActionResult> CreateCompetition(Guid id, CreateCompetitionInput input, CancellationToken cancellationToken)
    {
        input.TournamentId = id;
        var result = TournamentEventAccess.CanAccess(User, id) && ModelState.IsValid
            ? await _store.CreateCompetitionAsync(input, User, cancellationToken)
            : OperationResult.Failure("Competition details are incomplete or access is unavailable.");
        TempData[result.Succeeded ? "Notice" : "Error"] = result.Message;
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Policy = TournamentPolicies.ManageTournaments)]
    [HttpPost("/organizer/tournaments/{id:guid}/competitions/{competitionId:guid}/entrants")]
    [ValidateAntiForgeryToken]
    // Registers a local entrant without collecting contact information or requiring a cross-product identity.
    public async Task<IActionResult> RegisterEntrant(Guid id, Guid competitionId, RegisterEntrantInput input, CancellationToken cancellationToken)
    {
        input.TournamentId = id;
        input.CompetitionId = competitionId;
        var result = TournamentEventAccess.CanAccess(User, id) && ModelState.IsValid
            ? await _store.RegisterEntrantAsync(input, User, cancellationToken)
            : OperationResult.Failure("Entrant details are incomplete or access is unavailable.");
        TempData[result.Succeeded ? "Notice" : "Error"] = result.Message;
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Policy = TournamentPolicies.ManageTournaments)]
    [HttpPost("/organizer/tournaments/{id:guid}/competitions/{competitionId:guid}/entrants/{entrantId:guid}/status")]
    [ValidateAntiForgeryToken]
    // Changes registration state without deleting the entrant or its historical identity.
    public async Task<IActionResult> UpdateEntrantStatus(Guid id, Guid competitionId, Guid entrantId, UpdateEntrantStatusInput input, CancellationToken cancellationToken)
    {
        input.TournamentId = id; input.CompetitionId = competitionId; input.EntrantId = entrantId;
        var result = TournamentEventAccess.CanAccess(User, id) && ModelState.IsValid
            ? await _store.UpdateEntrantStatusAsync(input, User, cancellationToken)
            : OperationResult.Failure("Registration update is incomplete or access is unavailable.");
        TempData[result.Succeeded ? "Notice" : "Error"] = result.Message;
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Policy = TournamentPolicies.ManageTournaments)]
    [HttpPost("/organizer/tournaments/{id:guid}/competitions/{competitionId:guid}/draw")]
    [ValidateAntiForgeryToken]
    // Publishes only a validated immutable draw revision and records the organizer's reason.
    public async Task<IActionResult> PublishDraw(Guid id, Guid competitionId, PublishDrawInput input, CancellationToken cancellationToken)
    {
        input.TournamentId = id;
        input.CompetitionId = competitionId;
        var result = TournamentEventAccess.CanAccess(User, id) && ModelState.IsValid
            ? await _store.PublishDrawAsync(input, User, cancellationToken)
            : OperationResult.Failure("Draw publication details are incomplete or access is unavailable.");
        TempData[result.Succeeded ? "Notice" : "Error"] = result.Message;
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Policy = TournamentPolicies.RecordScores)]
    [HttpPost("/organizer/tournaments/{id:guid}/competitions/{competitionId:guid}/matches/{matchId}/result")]
    [ValidateAntiForgeryToken]
    // Uses optimistic result versions so concurrent scorekeepers refresh instead of overwriting history.
    public async Task<IActionResult> RecordResult(Guid id, Guid competitionId, string matchId, RecordMatchResultInput input, CancellationToken cancellationToken)
    {
        input.TournamentId = id;
        input.CompetitionId = competitionId;
        input.MatchId = matchId;
        var result = TournamentEventAccess.CanAccess(User, id) && ModelState.IsValid
            ? await _store.RecordMatchResultAsync(input, User, cancellationToken)
            : OperationResult.Failure("Result details are incomplete or access is unavailable.");
        TempData[result.Succeeded ? "Notice" : "Error"] = result.Message;
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Policy = TournamentPolicies.ManageTournaments)]
    [HttpPost("/organizer/tournaments/{id:guid}/tables")]
    [ValidateAntiForgeryToken]
    // Adds a table to this event's venue inventory without exposing database-local identities.
    public async Task<IActionResult> CreateTable(Guid id, CreateTournamentTableInput input, CancellationToken cancellationToken)
    {
        input.TournamentId = id;
        var result = TournamentEventAccess.CanAccess(User, id) && ModelState.IsValid
            ? await _store.CreateTableAsync(input, User, cancellationToken)
            : OperationResult.Failure("Table details are incomplete or access is unavailable.");
        TempData[result.Succeeded ? "Notice" : "Error"] = result.Message;
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Policy = TournamentPolicies.ManageTournaments)]
    [HttpPost("/organizer/tournaments/{id:guid}/competitions/{competitionId:guid}/matches/{matchId}/table")]
    [ValidateAntiForgeryToken]
    // Assigns only a current-draw match to a table owned by the same event venue.
    public async Task<IActionResult> AssignTable(Guid id, Guid competitionId, string matchId, AssignMatchTableInput input, CancellationToken cancellationToken)
    {
        input.TournamentId = id; input.CompetitionId = competitionId; input.MatchId = matchId;
        var result = TournamentEventAccess.CanAccess(User, id) && ModelState.IsValid
            ? await _store.AssignMatchTableAsync(input, User, cancellationToken)
            : OperationResult.Failure("Table assignment is incomplete or access is unavailable.");
        TempData[result.Succeeded ? "Notice" : "Error"] = result.Message;
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Policy = TournamentPolicies.RecordScores)]
    [HttpPost("/organizer/tournaments/{id:guid}/competitions/{competitionId:guid}/matches/{matchId}/status")]
    [ValidateAntiForgeryToken]
    // Keeps table calls and match starts inside the same event and match-assignment boundary as score entry.
    public async Task<IActionResult> UpdateMatchStatus(Guid id, Guid competitionId, string matchId, UpdateMatchStatusInput input, CancellationToken cancellationToken)
    {
        input.TournamentId = id; input.CompetitionId = competitionId; input.MatchId = matchId;
        var result = TournamentEventAccess.CanAccess(User, id) && ModelState.IsValid
            ? await _store.UpdateMatchStatusAsync(input, User, cancellationToken)
            : OperationResult.Failure("Match status details are incomplete or access is unavailable.");
        TempData[result.Succeeded ? "Notice" : "Error"] = result.Message;
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Policy = TournamentPolicies.ManageTournaments)]
    [HttpPost("/organizer/tournaments/{id:guid}/competitions/{competitionId:guid}/complete")]
    [ValidateAntiForgeryToken]
    // Finalizes one competition before the parent event may enter retained completed history.
    public async Task<IActionResult> CompleteCompetition(Guid id, Guid competitionId, CompleteCompetitionInput input, CancellationToken cancellationToken)
    {
        input.TournamentId = id; input.CompetitionId = competitionId;
        var result = TournamentEventAccess.CanAccess(User, id) && ModelState.IsValid
            ? await _store.CompleteCompetitionAsync(input, User, cancellationToken)
            : OperationResult.Failure("Completion details are incomplete or access is unavailable.");
        TempData[result.Succeeded ? "Notice" : "Error"] = result.Message;
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Policy = TournamentPolicies.ManageTournaments)]
    [HttpPost("/organizer/tournaments/{id:guid}/competitions/{competitionId:guid}/payouts")]
    [ValidateAntiForgeryToken]
    // Saves informational prizes through the same event-scoped authorization and audit boundary as setup.
    public async Task<IActionResult> UpsertPayout(Guid id, Guid competitionId, UpsertPayoutDisplayInput input, CancellationToken cancellationToken)
    {
        input.TournamentId = id; input.CompetitionId = competitionId;
        var result = TournamentEventAccess.CanAccess(User, id) && ModelState.IsValid
            ? await _store.UpsertPayoutDisplayAsync(input, User, cancellationToken)
            : OperationResult.Failure("Payout details are incomplete or access is unavailable.");
        TempData[result.Succeeded ? "Notice" : "Error"] = result.Message;
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Policy = TournamentPolicies.ManageTournaments)]
    [HttpGet("/organizer/tournaments/{id:guid}/exports/entrants.csv")]
    // Provides a product-local registration backup without contacts, account claims, or database identifiers.
    public async Task<IActionResult> ExportEntrants(Guid id, CancellationToken cancellationToken) =>
        await ExportEventAsync(id, "entrants", TournamentCsvExporter.Entrants, cancellationToken);

    [Authorize(Policy = TournamentPolicies.ManageTournaments)]
    [HttpGet("/organizer/tournaments/{id:guid}/exports/results.csv")]
    // Gives directors a current-score snapshot suitable for room records and post-event reconciliation.
    public async Task<IActionResult> ExportResults(Guid id, CancellationToken cancellationToken) =>
        await ExportEventAsync(id, "results", TournamentCsvExporter.Results, cancellationToken);

    [Authorize(Policy = TournamentPolicies.ManageTournaments)]
    [HttpGet("/organizer/tournaments/{id:guid}/competitions/{competitionId:guid}/exports/standings.csv")]
    // Exports exactly the standings calculation shown publicly for one round-robin competition.
    public async Task<IActionResult> ExportStandings(Guid id, Guid competitionId, CancellationToken cancellationToken)
    {
        var tournament = await _store.FindAsync(id, cancellationToken);
        var competition = tournament?.Competitions.SingleOrDefault(item => item.Id == competitionId);
        if (tournament is null || competition is null || !TournamentEventAccess.CanAccess(User, id)) return NotFound();
        return File(System.Text.Encoding.UTF8.GetBytes(TournamentCsvExporter.Standings(competition)), "text/csv; charset=utf-8", $"tournament-{id:N}-standings.csv");
    }

    [Authorize(Policy = TournamentPolicies.ManageTournaments)]
    [HttpGet("/organizer/tournaments/{id:guid}/exports/audit.csv")]
    // Makes redacted append-only history portable without exposing authentication or provider evidence.
    public async Task<IActionResult> ExportAudit(Guid id, CancellationToken cancellationToken)
    {
        if (!TournamentEventAccess.CanAccess(User, id) || await _store.FindAsync(id, cancellationToken) is null) return NotFound();
        var csv = TournamentCsvExporter.Audit(await _store.GetAuditAsync(id, cancellationToken));
        return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv; charset=utf-8", $"tournament-{id:N}-audit.csv");
    }

    // Centralizes event-scope checks and safe download naming for current-state exports.
    private async Task<IActionResult> ExportEventAsync(Guid id, string label, Func<TournamentEvent, string> exporter, CancellationToken cancellationToken)
    {
        var tournament = await _store.FindAsync(id, cancellationToken);
        if (tournament is null || !TournamentEventAccess.CanAccess(User, id)) return NotFound();
        return File(System.Text.Encoding.UTF8.GetBytes(exporter(tournament)), "text/csv; charset=utf-8", $"tournament-{id:N}-{label}.csv");
    }

    [Authorize(Policy = TournamentPolicies.ManageAccess)]
    [HttpGet("/organizer/access")]
    // Exposes the owner-only boundary without pretending real Account invitations are available in this repo.
    public IActionResult Access() => View();
}
