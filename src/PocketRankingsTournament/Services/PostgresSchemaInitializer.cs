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

    // Applies every checked-in contract in stable order before any request can use a partially upgraded database.
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var databasePath = Path.Combine(_environment.ContentRootPath, "Database");
        var migrationPaths = Directory.GetFiles(databasePath, "*.sql").OrderBy(path => path, StringComparer.Ordinal).ToArray();
        foreach (var path in migrationPaths)
        {
            // Checked-in migrations are idempotent and ordered by filename so a new client can safely initialize from zero.
            var sql = await File.ReadAllTextAsync(path, cancellationToken);
            await using var command = _dataSource.CreateCommand(sql);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        _logger.LogInformation("Tournament PostgreSQL schema contract is ready at {MigrationCount} migrations", migrationPaths.Length);
    }

    // The initializer owns no background work after startup, so shutdown intentionally has nothing to drain.
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
