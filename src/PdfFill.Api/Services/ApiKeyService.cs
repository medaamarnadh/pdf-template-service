using System.Security.Cryptography;
using System.Text;

namespace PdfFill.Api.Services;

public sealed record GeneratedKey(string Secret, string Prefix, string Hash);

/// <summary>
/// API key secrets. The secret is shown to the user exactly once; only the SHA-256 hash is stored,
/// so a database leak does not yield usable keys. The prefix is kept for display.
/// </summary>
public static class ApiKeyService
{
    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
    public const string LivePrefix = "pdfk_live_";

    /// <summary>Stored and displayed prefix: the tag plus 4 random characters. Not secret.</summary>
    public const int PrefixLength = 14;   // LivePrefix.Length (10) + 4 random characters

    public static GeneratedKey Generate()
    {
        var sb = new StringBuilder(LivePrefix, LivePrefix.Length + 32);
        // RandomNumberGenerator.GetInt32 draws uniformly from [0, Alphabet.Length) itself (it rejects and
        // redraws internally rather than using modulo), so this avoids the modulo bias that `byte % 62`
        // would introduce for a 62-character alphabet.
        for (var i = 0; i < 32; i++) sb.Append(Alphabet[RandomNumberGenerator.GetInt32(0, Alphabet.Length)]);
        var secret = sb.ToString();
        return new GeneratedKey(secret, secret[..PrefixLength], HashOf(secret));
    }

    public static string HashOf(string secret) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret))).ToLowerInvariant();
}
