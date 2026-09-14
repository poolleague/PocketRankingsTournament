using System.Security.Cryptography;
using System.Text;
using QRCoder;

namespace PocketRankingsTournament.Services;

public static class LiveTournamentLinks
{
    private const string Alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";
    public const int CodeLength = 10;

    // Uses an ambiguity-free alphabet and cryptographic selection so venue codes are both readable and unguessable.
    public static string GenerateCode()
    {
        Span<char> code = stackalloc char[CodeLength];
        for (var index = 0; index < code.Length; index++)
        {
            code[index] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }
        return new string(code);
    }

    // Normalizes only the documented code shape before hashing so alternate attacker-controlled encodings never compare equal.
    public static bool TryHash(string? code, out byte[] hash)
    {
        hash = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(code)) return false;
        var normalized = code.Trim().ToUpperInvariant();
        if (normalized.Length != CodeLength || normalized.Any(character => !Alphabet.Contains(character))) return false;
        hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return true;
    }

    // Produces a self-contained SVG rather than sending the private product URL to an external QR service.
    public static string CreateQrSvgDataUri(string absoluteUrl)
    {
        using var data = QRCodeGenerator.GenerateQrCode(absoluteUrl, QRCodeGenerator.ECCLevel.Q);
        var svg = new SvgQRCode(data).GetGraphic(5);
        return $"data:image/svg+xml;base64,{Convert.ToBase64String(Encoding.UTF8.GetBytes(svg))}";
    }
}
