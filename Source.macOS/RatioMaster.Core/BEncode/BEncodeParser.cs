namespace RatioMaster.Core.BEncode;

public sealed class BEncodeException : Exception
{
    public BEncodeException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Bencode reader working over an in-memory buffer.
/// </summary>
/// <remarks>
/// Parsing from a buffer rather than a <see cref="Stream"/> lets the parser record
/// the exact byte range each top-level value occupied. <see cref="Torrents.TorrentMetadata"/>
/// hashes the recorded range of the <c>info</c> dictionary instead of re-encoding it,
/// which is the only way to reproduce the info hash of a torrent whose keys are not
/// canonically ordered.
/// </remarks>
public static class BEncodeParser
{
    public static BValue Parse(byte[] data) => Parse(data, out _);

    public static BValue Parse(byte[] data, out IReadOnlyDictionary<string, Range> topLevelRanges)
    {
        ArgumentNullException.ThrowIfNull(data);
        var position = 0;
        var ranges = new Dictionary<string, Range>(StringComparer.Ordinal);
        var value = ReadValue(data, ref position, ranges);
        topLevelRanges = ranges;
        return value;
    }

    public static BValue Parse(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return Parse(buffer.ToArray());
    }

    private static BValue ReadValue(byte[] data, ref int position, Dictionary<string, Range>? recordInto = null)
    {
        if (position >= data.Length)
        {
            throw new BEncodeException("Unexpected end of bencoded data.");
        }

        return (char)data[position] switch
        {
            'd' => ReadDictionary(data, ref position, recordInto),
            'l' => ReadList(data, ref position),
            'i' => ReadNumber(data, ref position),
            _ => ReadString(data, ref position),
        };
    }

    private static BDictionary ReadDictionary(byte[] data, ref int position, Dictionary<string, Range>? recordInto)
    {
        position++; // 'd'
        var dictionary = new BDictionary();
        while (true)
        {
            if (position >= data.Length)
            {
                throw new BEncodeException("Unterminated dictionary.");
            }

            if (data[position] == (byte)'e')
            {
                position++;
                return dictionary;
            }

            var key = ReadString(data, ref position).Text;
            var valueStart = position;
            dictionary[key] = ReadValue(data, ref position);
            recordInto?.TryAdd(key, new Range(valueStart, position));
        }
    }

    private static BList ReadList(byte[] data, ref int position)
    {
        position++; // 'l'
        var list = new BList();
        while (true)
        {
            if (position >= data.Length)
            {
                throw new BEncodeException("Unterminated list.");
            }

            if (data[position] == (byte)'e')
            {
                position++;
                return list;
            }

            list.Add(ReadValue(data, ref position));
        }
    }

    private static BNumber ReadNumber(byte[] data, ref int position)
    {
        position++; // 'i'
        var start = position;
        while (position < data.Length && data[position] != (byte)'e')
        {
            position++;
        }

        if (position >= data.Length)
        {
            throw new BEncodeException("Unterminated integer.");
        }

        var text = BValue.Binary.GetString(data, start, position - start);
        position++; // 'e'
        if (!long.TryParse(text, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
        {
            throw new BEncodeException($"'{text}' is not a valid bencoded integer.");
        }

        return new BNumber(parsed);
    }

    private static BString ReadString(byte[] data, ref int position)
    {
        var start = position;
        while (position < data.Length && data[position] != (byte)':')
        {
            position++;
        }

        if (position >= data.Length)
        {
            throw new BEncodeException("Unterminated string length prefix.");
        }

        var prefix = BValue.Binary.GetString(data, start, position - start);
        if (!int.TryParse(prefix, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var length))
        {
            throw new BEncodeException($"'{prefix}' is not a valid string length.");
        }

        position++; // ':'
        if (position + length > data.Length)
        {
            throw new BEncodeException("String runs past the end of the buffer.");
        }

        var bytes = new byte[length];
        Buffer.BlockCopy(data, position, bytes, 0, length);
        position += length;
        return new BString(bytes);
    }
}
