using Microsoft.AspNetCore.Mvc;

namespace PocketRankingsTournament.Controllers;

public sealed class HelpController : Controller
{
    [HttpGet("/help")]
    // Keeps the tournament-day operating sequence available even when another Pocket Rankings product is offline.
    public IActionResult Index() => View();
}
