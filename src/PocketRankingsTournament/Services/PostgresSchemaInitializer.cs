using Npgsql;

namespace PocketRankingsTournament.Services;

public sealed class PostgresSchemaInitializer : IHostedService
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<PostgresSchemaInitializer> _logger;

    // Applies the idempotent checked-in contract at startup so a new isolated client stack is self-initializing.
    public PostgresSchemaInitializer(
        NpgsqlDataSource dataSource,
        IWebHostEnvironment environment,
        ILogger<PostgresSchemaInitializer> logger)
    {
        _dataSource = dataSource;
        _environment = environment;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(_environment.ContentRootPath, "Database", "001_initial_schema.sql");
        var sql = await File.ReadAllTextAsync(path, cancellationToken);
        await using var command = _dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Tournament PostgreSQL schema contract is ready");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
