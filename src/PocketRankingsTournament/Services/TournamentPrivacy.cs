using System.Security.Cryptography;
using System.Text;

namespace PocketRankingsTournament.Services;

// Generates installation-local anonymous labels and keyed identity suppressions for irreversible opt-out.
public sealed class TournamentPrivacy
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private readonly byte[] key;

    public TournamentPrivacy(string keyValue)
    {
        if (string.IsNullOrWhiteSpace(keyValue) || keyValue.Length < 32)
            throw new InvalidOperationException("Tournament privacy suppression key must contain at least 32 characters.");
        key = Encoding.UTF8.GetBytes(keyValue);
    }

    // Avoids ambiguous characters so volunteer organizers can distinguish anonymized people in old brackets.
    public string CreateAnonymousLabel()
    {
        var bytes = RandomNumberGenerator.GetBytes(4);
        return "Deleted player " + string.Concat(bytes.Select(value => Alphabet[value % Alphabet.Length]));
    }

    // Retains a one-way product-local suppression capable of blocking a future relink without storing PersonId.
    public string Hash(Guid personId) => Convert.ToHexString(HMACSHA256.HashData(key, personId.ToByteArray())).ToLowerInvariant();
}
