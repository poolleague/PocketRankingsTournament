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
}
