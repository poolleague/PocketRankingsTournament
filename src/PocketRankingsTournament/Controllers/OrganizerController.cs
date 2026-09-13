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

    // Keeps authenticated operations separate from the public read-only tournament surface.
    public OrganizerController(ITournamentStore store)
    {
        _store = store;
    }

    [HttpGet("/organizer")]
    // Splits current work from immutable history rather than mixing archived events into the operations queue.
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var all = await _store.GetAllAsync(cancellationToken);
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

        return View(new OrganizerTournamentViewModel(
            tournament,
            await _store.GetStatusHistoryAsync(id, cancellationToken),
            await _store.GetAuditAsync(id, cancellationToken),
            TournamentLifecycle.AvailableFrom(tournament.Status)));
    }

    [Authorize(Policy = TournamentPolicies.ManageTournaments)]
    [HttpPost("/organizer/tournaments/{id:guid}/status")]
    [ValidateAntiForgeryToken]
    // Routes every lifecycle mutation through the one-way store rule and its matching audit transaction.
    public async Task<IActionResult> Transition(Guid id, TransitionTournamentInput input, CancellationToken cancellationToken)
    {
        input.TournamentId = id;
        if (!ModelState.IsValid || !await _store.TransitionAsync(input, User, cancellationToken))
        {
            TempData["Error"] = "That status change is not allowed.";
        }
        else
        {
            TempData["Notice"] = $"Tournament moved to {TournamentLabels.Status(input.ToStatus)}.";
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Policy = TournamentPolicies.ManageAccess)]
    [HttpGet("/organizer/access")]
    // Exposes the owner-only boundary without pretending real Account invitations are available in this repo.
    public IActionResult Access() => View();
}
