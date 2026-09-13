using Microsoft.AspNetCore.Mvc;
using PocketRankingsTournament.Models;
using PocketRankingsTournament.Services;

namespace PocketRankingsTournament.Controllers;

public sealed class TournamentsController : Controller
{
    private readonly ITournamentCatalog _catalog;

    // Keeps the public read surface independent from future authenticated organizer workflows.
    public TournamentsController(ITournamentCatalog catalog)
    {
        _catalog = catalog;
    }

    [HttpGet("/")]
    [HttpGet("/tournaments")]
    public IActionResult Index() => View(_catalog.GetDirectory());

    [HttpGet("/tournaments/{id:guid}")]
    public IActionResult Details(Guid id, Guid? competitionId = null)
    {
        var tournament = _catalog.Find(id);
        if (tournament is null)
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
