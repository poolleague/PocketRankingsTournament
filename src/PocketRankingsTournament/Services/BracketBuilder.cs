using PocketRankingsTournament.Models;

namespace PocketRankingsTournament.Services;

public sealed class BracketBuilder
{
    // Builds a deterministic bracket plan so the draw can later be audited independently of mutable match results.
    public IReadOnlyList<BracketRound> Build(
        CompetitionFormat format,
        IReadOnlyList<Participant> participants,
        bool includeGrandFinalReset = true)
    {
        if (participants.Count < 2)
        {
            throw new ArgumentException("A bracket requires at least two participants.", nameof(participants));
        }

        if (participants.Select(participant => participant.Id).Distinct().Count() != participants.Count)
        {
            throw new ArgumentException("A participant can appear only once in a draw.", nameof(participants));
        }

        return format switch
        {
            CompetitionFormat.SingleElimination => BuildSingleElimination(participants),
            CompetitionFormat.DoubleElimination => BuildDoubleElimination(participants, includeGrandFinalReset),
            CompetitionFormat.RoundRobin => BuildRoundRobin(participants),
            _ => throw new NotSupportedException($"{format} is modeled but its scheduling engine is planned for a later phase.")
        };
    }

    // Uses the circle method so every entrant plays every other entrant exactly once with balanced rounds.
    private static IReadOnlyList<BracketRound> BuildRoundRobin(IReadOnlyList<Participant> participants)
    {
        var rotating = participants
            .OrderBy(participant => participant.Seed ?? int.MaxValue)
            .ThenBy(participant => participant.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Cast<Participant?>()
            .ToList();
        if (rotating.Count % 2 != 0)
        {
            rotating.Add(null);
        }

        var rounds = new List<BracketRound>();
        for (var roundNumber = 1; roundNumber < rotating.Count; roundNumber++)
        {
            var matches = new List<BracketMatch>();
            for (var pair = 0; pair < rotating.Count / 2; pair++)
            {
                var first = rotating[pair];
                var second = rotating[rotating.Count - 1 - pair];
                if (first is null || second is null)
                {
                    continue;
                }

                var position = matches.Count + 1;
                matches.Add(new BracketMatch(
                    $"RR{roundNumber}M{position}", "Round robin", roundNumber, position,
                    $"Round {roundNumber}", first, second, null, null, MatchStatus.Ready));
            }

            rounds.Add(new BracketRound($"RR{roundNumber}", $"Round {roundNumber}", "Round robin", roundNumber, matches));

            // The first entrant stays fixed while the remaining entrants rotate one place clockwise.
            var last = rotating[^1];
            rotating.RemoveAt(rotating.Count - 1);
            rotating.Insert(1, last);
        }

        return rounds;
    }

    // Produces winner-bracket rounds with explicit advancement targets; byes advance without inventing results.
    private static IReadOnlyList<BracketRound> BuildSingleElimination(IReadOnlyList<Participant> participants)
    {
        var size = NextPowerOfTwo(participants.Count);
        var roundCount = (int)Math.Log2(size);
        var seededSlots = SeedParticipants(participants, size);
        var rounds = new List<BracketRound>();

        for (var roundNumber = 1; roundNumber <= roundCount; roundNumber++)
        {
            var matchCount = size / (int)Math.Pow(2, roundNumber);
            var matches = new List<BracketMatch>(matchCount);
            for (var position = 1; position <= matchCount; position++)
            {
                var id = $"W{roundNumber}M{position}";
                var next = roundNumber == roundCount ? null : $"W{roundNumber + 1}M{(position + 1) / 2}";
                var first = roundNumber == 1 ? seededSlots[(position - 1) * 2] : null;
                var second = roundNumber == 1 ? seededSlots[((position - 1) * 2) + 1] : null;
                var status = roundNumber == 1 && (first is null || second is null) ? MatchStatus.Bye : MatchStatus.Waiting;
                matches.Add(new BracketMatch(id, "Winners", roundNumber, position, RoundLabel(roundNumber, roundCount), first, second, null, null, status, WinnerTo: next));
            }

            rounds.Add(new BracketRound($"W{roundNumber}", RoundLabel(roundNumber, roundCount), "Winners", roundNumber, matches));
        }

        return rounds;
    }

    // Extends the winner bracket with the standard two-round-per-drop losers structure and optional reset final.
    private static IReadOnlyList<BracketRound> BuildDoubleElimination(IReadOnlyList<Participant> participants, bool includeGrandFinalReset)
    {
        var winnerRounds = BuildSingleElimination(participants).ToList();
        var size = NextPowerOfTwo(participants.Count);
        var winnerRoundCount = (int)Math.Log2(size);
        var loserRounds = new List<BracketRound>();

        for (var stage = 1; stage < winnerRoundCount; stage++)
        {
            var matchesInStage = size / (int)Math.Pow(2, stage + 1);
            var minorRound = (stage * 2) - 1;
            var majorRound = stage * 2;

            loserRounds.Add(new BracketRound(
                $"L{minorRound}",
                $"Elimination round {minorRound}",
                "Elimination",
                minorRound,
                Enumerable.Range(1, matchesInStage)
                    .Select(position => new BracketMatch(
                        $"L{minorRound}M{position}", "Elimination", minorRound, position,
                        $"Elimination round {minorRound}", null, null, null, null, MatchStatus.Waiting,
                        WinnerTo: $"L{majorRound}M{position}"))
                    .ToArray()));

            var nextLoserRound = stage == winnerRoundCount - 1 ? "GF1" : $"L{majorRound + 1}M{1}";
            loserRounds.Add(new BracketRound(
                $"L{majorRound}",
                $"Elimination round {majorRound}",
                "Elimination",
                majorRound,
                Enumerable.Range(1, matchesInStage)
                    .Select(position => new BracketMatch(
                        $"L{majorRound}M{position}", "Elimination", majorRound, position,
                        $"Elimination round {majorRound}", null, null, null, null, MatchStatus.Waiting,
                        WinnerTo: stage == winnerRoundCount - 1 ? "GF1" : $"L{majorRound + 1}M{(position + 1) / 2}"))
                    .ToArray()));
        }

        // Loser destinations are named now; the result service can later fill their exact slots transactionally.
        for (var roundIndex = 0; roundIndex < winnerRounds.Count; roundIndex++)
        {
            var loserRoundNumber = roundIndex == 0 ? 1 : roundIndex * 2;
            winnerRounds[roundIndex] = winnerRounds[roundIndex] with
            {
                Matches = winnerRounds[roundIndex].Matches.Select(match => match with
                {
                    WinnerTo = roundIndex == winnerRounds.Count - 1 ? "GF1" : match.WinnerTo,
                    LoserTo = winnerRoundCount == 1
                        ? "GF1"
                        : roundIndex == 0
                            ? $"L{loserRoundNumber}M{(match.Position + 1) / 2}"
                            : $"L{loserRoundNumber}M{match.Position}"
                }).ToArray()
            };
        }

        var finals = new List<BracketMatch>
        {
            new("GF1", "Finals", 1, 1, "Championship", null, null, null, null, MatchStatus.Waiting,
                WinnerTo: includeGrandFinalReset ? "GF2" : null)
        };
        if (includeGrandFinalReset)
        {
            finals.Add(new BracketMatch("GF2", "Finals", 2, 1, "If-needed reset", null, null, null, null,
                MatchStatus.Conditional, IsConditional: true));
        }

        return winnerRounds
            .Concat(loserRounds)
            .Append(new BracketRound("GF", "Championship", "Finals", 1, finals))
            .ToArray();
    }

    // Uses conventional balanced seed positions so top seeds cannot meet in the opening rounds.
    private static IReadOnlyList<Participant?> SeedParticipants(IReadOnlyList<Participant> participants, int size)
    {
        var ordered = participants
            .OrderBy(participant => participant.Seed ?? int.MaxValue)
            .ThenBy(participant => participant.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var positions = BuildSeedOrder(size);
        var slots = new Participant?[size];
        for (var index = 0; index < positions.Count; index++)
        {
            var participantIndex = positions[index] - 1;
            slots[index] = participantIndex < ordered.Length ? ordered[participantIndex] : null;
        }

        return slots;
    }

    private static IReadOnlyList<int> BuildSeedOrder(int size)
    {
        var order = new List<int> { 1, 2 };
        while (order.Count < size)
        {
            var nextSize = order.Count * 2;
            order = order.SelectMany(seed => new[] { seed, nextSize + 1 - seed }).ToList();
        }

        return order;
    }

    private static int NextPowerOfTwo(int value)
    {
        var power = 1;
        while (power < value)
        {
            power *= 2;
        }

        return power;
    }

    private static string RoundLabel(int round, int totalRounds) => (totalRounds - round) switch
    {
        0 => "Winners final",
        1 => "Winners semifinal",
        2 => "Winners quarterfinal",
        _ => $"Winners round {round}"
    };
}
