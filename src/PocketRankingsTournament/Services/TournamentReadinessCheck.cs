using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace PocketRankingsTournament.Services;

// Distinguishes a live web process from a Tournament installation that can actually reach its durable database.
public sealed class TournamentReadinessCheck(NpgsqlDataSource dataSource) : IHealthCheck
{
    // Executes the smallest possible database round trip without returning connection details or schema data.
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var command = dataSource.CreateCommand("SELECT 1");
            await command.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy("Tournament database is reachable.");
        }
        catch (Exception exception) when (exception is NpgsqlException or TimeoutException)
        {
            return HealthCheckResult.Unhealthy("Tournament database is unavailable.");
        }
    }
}
