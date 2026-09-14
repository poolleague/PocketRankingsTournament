using Microsoft.AspNetCore.Mvc;
using PocketRankingsTournament.Models;
using PocketRankingsTournament.Security;
using PocketRankingsTournament.Services;

namespace PocketRankingsTournament.Controllers;

public sealed class TournamentsController : Controller
{
    private readonly ITournamentStore _store;

    // Keeps the public read surface independent from future authenticated organizer workflows.
    public TournamentsController(ITournamentStore store)
    {
        _store = store;
    }

    [HttpGet("/")]
    [HttpGet("/tournaments")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(await _store.GetDirectoryAsync(cancellationToken));

    [HttpGet("/tournaments/{id:guid}")]
    public async Task<IActionResult> Details(Guid id, Guid? competitionId = null, CancellationToken cancellationToken = default)
    {
        var tournament = await _store.FindAsync(id, cancellationToken);
        // Private drafts and archived operational records must never become public through a guessed UUID.
        if (tournament is null || tournament.Status is TournamentStatus.Draft or TournamentStatus.Archived
            || tournament.Visibility == TournamentVisibility.Private && !TournamentEventAccess.CanAccess(User, id))
        {
            return NotFound();
        }

        var competition = competitionId.HasValue
            ? tournament.Competitions.SingleOrDefault(item => item.Id == competitionId.Value)
            : tournament.Competitions.FirstOrDefault();
        if (competition is null)
        {
            return View("ComingSoon", tournament);
        }

        return View(new TournamentDetailViewModel(tournament, competition));
    }
}
