using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using PocketRankingsTournament.Models;

namespace PocketRankingsTournament.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;

    public HomeController(ILogger<HomeController> logger)
    {
        _logger = logger;
    }

    // Keeps the legal placeholder available without competing with the Tournament directory's root route.
    public IActionResult Privacy()
    {
        return View();
    }

    // Avoids caching exception details and exposes only the request correlation id.
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
