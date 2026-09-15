using Microsoft.AspNetCore.Mvc;
using PocketRankingsTournament.Services;

namespace PocketRankingsTournament.Controllers;

// Accepts only a signed machine directive and returns a non-identifying idempotent acknowledgement.
[ApiController]
[Route("internal/privacy")]
public sealed class InternalPrivacyController(PrivacyDirectiveVerifier verifier, ITournamentStore store) : ControllerBase
{
    [HttpPost("player-data-erasure")]
    [IgnoreAntiforgeryToken]
    // Bypasses browser CSRF only because this machine route requires a purpose-bound Account signature.
    public async Task<IActionResult> Erase([FromHeader(Name="Authorization")] string authorization,CancellationToken token)
    {
        Response.Headers.CacheControl="no-store";
        if(!authorization.StartsWith("Bearer ",StringComparison.Ordinal)||!verifier.TryVerify(authorization[7..],DateTimeOffset.UtcNow,out var directive))return Unauthorized();
        var result=await store.AnonymizePlayerDataAsync(directive!,token);
        return Ok(new{requestId=result.RequestId,completed=result.Completed,duplicate=result.Duplicate});
    }
}
