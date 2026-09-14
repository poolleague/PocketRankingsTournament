namespace PocketRankingsTournament.Tests;

public sealed class SchemaContractTests
{
    [Fact]
    public void InitialSchemaIsTransactionalIdempotentAndProductLocal()
    {
        var sql = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Database", "001_initial_schema.sql"));

        Assert.StartsWith("BEGIN;", sql.TrimStart(), StringComparison.Ordinal);
        Assert.EndsWith("COMMIT;", sql.TrimEnd(), StringComparison.Ordinal);
        Assert.Contains("CREATE SCHEMA IF NOT EXISTS tourn", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS integ.person_links", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("league.", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("email", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParticipantIdentityLinkRemainsOptionalAndSeparate()
    {
        var sql = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Database", "001_initial_schema.sql"));

        var participantOffset = sql.IndexOf("CREATE TABLE IF NOT EXISTS tourn.participants", StringComparison.Ordinal);
        var linkOffset = sql.IndexOf("CREATE TABLE IF NOT EXISTS integ.person_links", StringComparison.Ordinal);
        Assert.True(participantOffset >= 0 && linkOffset > participantOffset);
        var participantBlock = sql[participantOffset..linkOffset];
        Assert.DoesNotContain("person_uuid", participantBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void AdministrationMigrationRetainsRolesDrawsResultsAndLifecycleHistory()
    {
        var sql = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Database", "002_administration_history.sql"));

        Assert.StartsWith("BEGIN;", sql.TrimStart(), StringComparison.Ordinal);
        Assert.EndsWith("COMMIT;", sql.TrimEnd(), StringComparison.Ordinal);
        Assert.Contains("core.local_accounts", sql, StringComparison.Ordinal);
        Assert.Contains("core.account_roles", sql, StringComparison.Ordinal);
        Assert.Contains("tourn.event_role_assignments", sql, StringComparison.Ordinal);
        Assert.Contains("tourn.event_status_history", sql, StringComparison.Ordinal);
        Assert.Contains("tourn.draw_revisions", sql, StringComparison.Ordinal);
        Assert.Contains("tourn.match_result_revisions", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("league.", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AuditMutationIsRejectedAtTheDatabaseBoundary()
    {
        var sql = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Database", "002_administration_history.sql"));

        Assert.Contains("BEFORE UPDATE OR DELETE ON audit.entries", sql, StringComparison.Ordinal);
        Assert.Contains("Tournament audit entries are append-only", sql, StringComparison.Ordinal);
    }

    [Fact]
    // Guards the additive operational contract for current draws, bracket keys, strategies, and result outcomes.
    public void CompetitionOperationsMigrationIsAdditiveIdempotentAndProductLocal()
    {
        var sql = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Database", "003_competition_operations.sql"));

        Assert.StartsWith("BEGIN;", sql.TrimStart(), StringComparison.Ordinal);
        Assert.EndsWith("COMMIT;", sql.TrimEnd(), StringComparison.Ordinal);
        Assert.Contains("current_draw_revision", sql, StringComparison.Ordinal);
        Assert.Contains("bracket_key", sql, StringComparison.Ordinal);
        Assert.Contains("draw_strategy", sql, StringComparison.Ordinal);
        Assert.Contains("outcome", sql, StringComparison.Ordinal);
        Assert.Contains("tourn.match_scorekeeper_assignments", sql, StringComparison.Ordinal);
        Assert.Contains("tourn.venue_tables ADD COLUMN IF NOT EXISTS public_uuid", sql, StringComparison.Ordinal);
        Assert.Contains("ON CONFLICT (migration_name) DO NOTHING", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("league.", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("email", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    // Keeps invalidated downstream results distinguishable from confirmed scores in retained history.
    public void LaunchOperationsMigrationLabelsDownstreamResetRevisions()
    {
        var sql = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Database", "004_launch_operations.sql"));

        Assert.StartsWith("BEGIN;", sql.TrimStart(), StringComparison.Ordinal);
        Assert.EndsWith("COMMIT;", sql.TrimEnd(), StringComparison.Ordinal);
        Assert.Contains("revision_kind", sql, StringComparison.Ordinal);
        Assert.Contains("downstream_reset", sql, StringComparison.Ordinal);
        Assert.Contains("004_launch_operations", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("league.", sql, StringComparison.OrdinalIgnoreCase);
    }
}
