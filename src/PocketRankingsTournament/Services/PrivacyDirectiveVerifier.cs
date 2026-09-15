using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PocketRankingsTournament.Models;

namespace PocketRankingsTournament.Services;

// Verifies the Account signature plus purpose, audience, installation, and time before exposing identity.
public sealed class PrivacyDirectiveVerifier : IDisposable
{
    private readonly RSA? publicKey;
    private readonly string installationKey = "";

    public PrivacyDirectiveVerifier(IConfiguration configuration)
    {
        if(!configuration.GetValue<bool>("PrivacyReceiver:Enabled"))return;
        var pem = configuration["AccountIdentity:PublicKeyPem"];
        installationKey = configuration["Installation:Key"] ?? "";
        if (string.IsNullOrWhiteSpace(pem) || string.IsNullOrWhiteSpace(installationKey)) return;
        publicKey = RSA.Create(); publicKey.ImportFromPem(pem);
    }

    // Raw bearer material is verified in memory, never logged, returned, or stored.
    public bool TryVerify(string token, DateTimeOffset now, out PlayerDataAnonymizationDirective? directive)
    {
        directive=null;
        if (publicKey is null) return false;
        try
        {
            var parts=token.Split('.'); if(parts.Length!=3)return false;
            using var header=JsonDocument.Parse(Decode(parts[0]));
            if(header.RootElement.GetProperty("alg").GetString()!="RS256"||header.RootElement.GetProperty("kid").GetString()!="account-privacy-1")return false;
            if(!publicKey.VerifyData(Encoding.UTF8.GetBytes($"{parts[0]}.{parts[1]}"),DecodeBytes(parts[2]),HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1))return false;
            using var payload=JsonDocument.Parse(Decode(parts[1])); var root=payload.RootElement;
            if(root.GetProperty("iss").GetString()!="pocketrankings-account"||root.GetProperty("aud").GetString()!="pocketrankings-tournament"||root.GetProperty("action").GetString()!="person.player_data_erasure_requested.v1"||root.GetProperty("installation_key").GetString()!=installationKey)return false;
            var issued=DateTimeOffset.FromUnixTimeSeconds(root.GetProperty("iat").GetInt64()); var expires=DateTimeOffset.FromUnixTimeSeconds(root.GetProperty("exp").GetInt64());
            if(issued>now.AddSeconds(30)||expires<=now||expires<=issued||expires-issued>TimeSpan.FromMinutes(2))return false;
            directive=new(root.GetProperty("request_id").GetGuid(),root.GetProperty("person_id").GetGuid(),root.GetProperty("jti").GetGuid(),issued); return true;
        }
        catch(Exception exception) when(exception is FormatException or JsonException or KeyNotFoundException or InvalidOperationException or ArgumentOutOfRangeException){return false;}
    }
    // JWT base64url text is decoded locally so usable bearer material never reaches another subsystem.
    private static string Decode(string value)=>Encoding.UTF8.GetString(DecodeBytes(value));
    // Restores base64 padding only for the bounded JWT segment being verified.
    private static byte[] DecodeBytes(string value){value=value.Replace('-','+').Replace('_','/');return Convert.FromBase64String(value.PadRight(value.Length+(4-value.Length%4)%4,'='));}
    public void Dispose()=>publicKey?.Dispose();
}
