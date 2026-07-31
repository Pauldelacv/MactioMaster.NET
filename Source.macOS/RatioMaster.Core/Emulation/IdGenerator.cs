namespace RatioMaster.Core.Emulation;

using System.Text;

/// <summary>
/// Generates the random identifier fragments (peer id tails, tracker keys) and
/// performs the byte-oriented percent-encoding the tracker query strings need.
/// </summary>
public static class IdGenerator
{
    private const string Alphanumeric = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
    private const string Numeric = "0123456789";
    private const string UpperHex = "0123456789ABCDEF";

    public static string Random(int length) => FromAlphabet(length, Alphanumeric);

    public static string RandomNumeric(int length) => FromAlphabet(length, Numeric);

    public static string RandomHex(int length) => FromAlphabet(length, UpperHex);

    /// <summary>Arbitrary bytes in the 0-254 range, matching the original generator.</summary>
    public static string RandomBytes(int length)
    {
        var builder = new StringBuilder(length);
        for (var i = 0; i < length; i++)
        {
            builder.Append((char)System.Random.Shared.Next(255));
        }

        return builder.ToString();
    }

    private static string FromAlphabet(int length, string alphabet)
    {
        var builder = new StringBuilder(length);
        for (var i = 0; i < length; i++)
        {
            builder.Append(alphabet[System.Random.Shared.Next(alphabet.Length)]);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Percent-encodes a Latin-1 string the way BitTorrent clients encode
    /// <c>info_hash</c> and <c>peer_id</c>: ASCII letters and digits pass through,
    /// everything else becomes %XX.
    /// </summary>
    public static string PercentEncode(string value, bool upperCase)
    {
        var builder = new StringBuilder(value.Length * 3);
        foreach (var c in value)
        {
            if (c < 127 && char.IsLetterOrDigit(c))
            {
                builder.Append(c);
                continue;
            }

            var hex = ((int)c).ToString(upperCase ? "X2" : "x2", System.Globalization.CultureInfo.InvariantCulture);

            // Values above 0xFF cannot occur for Latin-1 payloads, but a hand-typed
            // peer id could contain one; keep the low byte rather than emitting %XXXX.
            builder.Append('%').Append(hex.Length > 2 ? hex[^2..] : hex);
        }

        return builder.ToString();
    }

    /// <summary>Percent-encodes raw bytes, used for the 20-byte info hash.</summary>
    public static string PercentEncode(ReadOnlySpan<byte> bytes, bool upperCase)
    {
        var builder = new StringBuilder(bytes.Length * 3);
        foreach (var b in bytes)
        {
            if (b < 127 && char.IsLetterOrDigit((char)b))
            {
                builder.Append((char)b);
            }
            else
            {
                builder.Append('%').Append(b.ToString(upperCase ? "X2" : "x2", System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        return builder.ToString();
    }

    public static int RandomPort() => System.Random.Shared.Next(1025, 65535);
}
