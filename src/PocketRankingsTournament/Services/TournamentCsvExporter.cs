using System.Text;
using PocketRankingsTournament.Models;

namespace PocketRankingsTournament.Services;

// Produces privacy-bounded operational exports while neutralizing spreadsheet formula injection in user-entered labels.
public static class TournamentCsvExporter
{
    // Exports the retained registration state for reconciliation at the tournament desk.
    public static string Entrants(TournamentEvent tournament)
    {
        var rows = new List<IReadOnlyList<object?>> { new object?[] { "Competition", "Entrant", "Seed", "Handicap", "Status" } };
        rows.AddRange(tournament.Competitions.SelectMany(competition => competition.Participants.Select(entrant =>
            (IReadOnlyList<object?>)new object?[] { competition.Name, entrant.DisplayName, entrant.Seed, entrant.Handicap, entrant.RegistrationStatus })));
        return Build(rows);
    }

    // Exports current results without leaking local database identifiers or superseded audit evidence.
    public static string Results(TournamentEvent tournament)
    {
        var rows = new List<IReadOnlyList<object?>> { new object?[] { "Competition", "Match", "Round", "Table", "Status", "Entrant 1", "Score 1", "Entrant 2", "Score 2", "Version" } };
        rows.AddRange(tournament.Competitions.SelectMany(competition => competition.Rounds.SelectMany(round => round.Matches.Select(match =>
            (IReadOnlyList<object?>)new object?[] { competition.Name, match.Id, round.Name, match.TableName, TournamentLabels.MatchStatus(match.Status), match.EntrantOne?.DisplayName, match.EntrantOneScore, match.EntrantTwo?.DisplayName, match.EntrantTwoScore, match.ResultVersion }))));
        return Build(rows);
    }

    // Exports the deterministic ranking projection used by the public round-robin screen.
    public static string Standings(Competition competition)
    {
        var rows = new List<IReadOnlyList<object?>> { new object?[] { "Position", "Entrant", "Played", "Won", "Lost", "For", "Against", "Difference" } };
        rows.AddRange(TournamentStandings.Calculate(competition).Select(row =>
            (IReadOnlyList<object?>)new object?[] { row.Position, row.Entrant.DisplayName, row.Played, row.Wins, row.Losses, row.ScoreFor, row.ScoreAgainst, row.ScoreDifference }));
        return Build(rows);
    }

    // Exports redacted append-only evidence for local record retention and support review.
    public static string Audit(IReadOnlyList<TournamentAuditEntry> entries)
    {
        var rows = new List<IReadOnlyList<object?>> { new object?[] { "Occurred at UTC", "Actor", "Role", "Action", "Target type", "Target UUID", "Reason" } };
        rows.AddRange(entries.Select(entry => (IReadOnlyList<object?>)new object?[]
            { entry.OccurredAt.UtcDateTime.ToString("O"), entry.ActorName, entry.ActorRole, entry.Action, entry.TargetType, entry.TargetId, entry.Reason }));
        return Build(rows);
    }

    // Applies RFC-style quoting after making formula-like cells inert in common spreadsheet programs.
    private static string Build(IEnumerable<IReadOnlyList<object?>> rows)
    {
        var output = new StringBuilder();
        foreach (var row in rows)
        {
            output.AppendLine(string.Join(',', row.Select(Cell)));
        }
        return output.ToString();
    }

    private static string Cell(object? value)
    {
        var text = value?.ToString() ?? "";
        if (text.Length > 0 && (text[0] is '=' or '+' or '-' or '@' || char.IsControl(text[0])))
        {
            text = "'" + text;
        }
        return '"' + text.Replace("\"", "\"\"") + '"';
    }
}
