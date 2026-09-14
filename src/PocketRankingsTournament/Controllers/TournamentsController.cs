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

    [HttpGet("/live/{code}")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    // Uses the same read-only public projection after the store verifies code, expiry, revocation, and lifecycle.
    public async Task<IActionResult> Live(string code, Guid? competitionId = null, CancellationToken cancellationToken = default)
    {
        var tournament = await _store.FindByLiveCodeAsync(code, cancellationToken);
        if (tournament is null) return NotFound();
        var competition = competitionId.HasValue
            ? tournament.Competitions.SingleOrDefault(item => item.Id == competitionId.Value)
            : tournament.Competitions.FirstOrDefault();
        return competition is null
            ? View("ComingSoon", tournament)
            : View("Details", new TournamentDetailViewModel(tournament, competition));
    }
}
