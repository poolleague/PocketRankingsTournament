using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PocketRankingsTournament.Security;

namespace PocketRankingsTournament.Controllers;

[AllowAnonymous]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class DevelopmentAccessController : Controller
{
    private readonly IWebHostEnvironment _environment;

    // Makes fictional role testing possible locally while leaving Production without a bypass around Account identity.
    public DevelopmentAccessController(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    [HttpGet("/development/access")]
    // Returning 404 outside Development prevents the fictional selector becoming an alternate Production login.
    public IActionResult Index() => _environment.IsDevelopment() ? View() : NotFound();

    [HttpPost("/development/access")]
    [ValidateAntiForgeryToken]
    // Issues only a fictional local cookie so each role boundary can be exercised without real identity data.
    public async Task<IActionResult> SignInAs(string role)
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        var allowed = new[] { TournamentRoles.Owner, TournamentRoles.TournamentDirector, TournamentRoles.Scorekeeper };
        if (!allowed.Contains(role, StringComparer.Ordinal))
        {
            return BadRequest();
        }

        var displayName = role switch
        {
            TournamentRoles.Owner => "Fictional Installation Owner",
            TournamentRoles.TournamentDirector => "Fictional Tournament Director",
            _ => "Fictional Scorekeeper"
        };
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, $"development-{role}"),
            new Claim(ClaimTypes.Name, displayName),
            new Claim(ClaimTypes.Role, role),
            new Claim(TournamentClaimTypes.Entitlement, "active"),
            new Claim(TournamentClaimTypes.EntitlementExpiresAt, new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero).ToString("O")),
            new Claim("identity_source", "fictional-development-only")
        };
        if (role is TournamentRoles.TournamentDirector or TournamentRoles.Scorekeeper)
        {
            // The wildcard exists only in the fictional Development selector; Production handoffs must enumerate event UUIDs.
            claims.Add(new Claim(TournamentClaimTypes.EventAssignment, "*"));
        }
        if (role == TournamentRoles.Scorekeeper)
        {
            // Fictional scorekeepers can exercise every local match card; Production assignments must name a stable match key.
            claims.Add(new Claim(TournamentClaimTypes.MatchAssignment, "*"));
        }
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
        return RedirectToAction("Index", "Organizer");
    }

    [HttpPost("/sign-out")]
    [ValidateAntiForgeryToken]
    // Uses the normal cookie sign-out path so local validation also covers session cleanup.
    public async Task<IActionResult> SignOutLocal()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Index", "Tournaments");
    }

    [HttpGet("/access-denied")]
    // Gives authenticated users a neutral permission failure without revealing protected event details.
    public IActionResult AccessDenied() => View();

    [HttpGet("/account-required")]
    // Keeps Production public views useful while clearly failing closed for unavailable Account integration.
    public IActionResult AccountRequired() => View();
}
