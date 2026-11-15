using PdfFill.Api.Services;

namespace PdfFill.Api.Tests;

public class ApiKeyServiceTests
{
    [Fact]
    public void Generated_key_has_the_documented_shape()
    {
        var k = ApiKeyService.Generate();
        Assert.StartsWith("pdfk_live_", k.Secret);
        Assert.True(k.Secret.Length >= 40, $"secret was {k.Secret.Length} chars");
        Assert.Equal(k.Secret[..ApiKeyService.PrefixLength], k.Prefix);
        Assert.Equal(64, k.Hash.Length);                        // sha-256 hex
        Assert.Matches("^[0-9a-f]+$", k.Hash);
    }

    [Fact]
    public void Generated_keys_are_unique()
    {
        var secrets = Enumerable.Range(0, 200).Select(_ => ApiKeyService.Generate().Secret).ToHashSet();
        Assert.Equal(200, secrets.Count);
    }

    [Fact]
    public void Hash_is_stable_and_specific()
    {
        var k = ApiKeyService.Generate();
        Assert.Equal(k.Hash, ApiKeyService.HashOf(k.Secret));
        Assert.NotEqual(k.Hash, ApiKeyService.HashOf(k.Secret + "x"));
    }

    [Fact]
    public void The_alphabet_is_drawn_without_modulo_bias()
    {
        // `byte % 62` over a uniform byte skews '0'..'7' (indices 0-7, hit by both 0-247 and 248-255) to
        // 5/256 probability versus 4/256 for every other character - a 25% relative overrepresentation.
        // RandomNumberGenerator.GetInt32(0, 62) draws uniformly instead, so with enough samples every one
        // of the 62 characters should land close to the mean count.
        //
        // 200 secrets * 32 body chars = 6400 draws over 62 buckets -> mean ~= 103.2 per bucket. Under the
        // old biased generator, '0'-'7' would land near 129 (mean * 5/4), comfortably past any reasonable
        // threshold. A threshold of 1.5x the mean gives generous headroom above normal binomial variation
        // (stddev here is roughly sqrt(6400 * (1/62) * (61/62)) ~= 10, so 1.5x mean is about 5 standard
        // deviations away) while still catching the ~25% bias the old code produced.
        const int secretCount = 200;
        const string alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
        var counts = new Dictionary<char, int>();
        foreach (var ch in alphabet) counts[ch] = 0;

        var totalChars = 0;
        for (var i = 0; i < secretCount; i++)
        {
            var body = ApiKeyService.Generate().Secret[ApiKeyService.LivePrefix.Length..];
            foreach (var ch in body)
            {
                counts[ch] = counts[ch] + 1;
                totalChars++;
            }
        }

        var mean = (double)totalChars / alphabet.Length;
        foreach (var (ch, count) in counts)
            Assert.True(count <= mean * 1.5, $"'{ch}' appeared {count} times; mean was {mean:F1} (threshold {mean * 1.5:F1})");
    }
}
