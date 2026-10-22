using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;

namespace Platform.Integration.Tests.Infrastructure;

/// <summary>RFC 6238 TOTP (SHA-1, 30 s, 6 digits), as an authenticator app computes it from the Base32 shared key.</summary>
public static class Totp
{
    /// <param name="stepOffset">Time steps from now; the server accepts a small window around the current step.</param>
    public static string Code(string base32Key, int stepOffset = 0)
    {
        ArgumentNullException.ThrowIfNull(base32Key);
        var step = (DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30) + stepOffset;
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, step);
#pragma warning disable CA5350 // RFC 6238 authenticators use HMAC-SHA1.
        var hash = HMACSHA1.HashData(Base32Decode(base32Key), counter);
#pragma warning restore CA5350
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    }

    private static byte[] Base32Decode(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var clean = input.Replace(" ", string.Empty, StringComparison.Ordinal).TrimEnd('=').ToUpperInvariant();
        var bytes = new List<byte>(clean.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (var c in clean)
        {
            buffer = (buffer << 5) | alphabet.IndexOf(c, StringComparison.Ordinal);
            bits += 5;
            if (bits >= 8)
            {
                bytes.Add((byte)(buffer >> (bits - 8)));
                bits -= 8;
            }
        }

        return [.. bytes];
    }
}
