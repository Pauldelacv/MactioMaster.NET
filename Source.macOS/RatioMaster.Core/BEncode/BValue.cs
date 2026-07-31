namespace RatioMaster.Core.BEncode;

using System.Text;

/// <summary>
/// Base type for every bencoded value.
/// </summary>
/// <remarks>
/// The original Windows code round-tripped bencoded payloads through
/// <c>Encoding.GetEncoding(1252)</c>. Windows-1252 is not a 1:1 byte mapping
/// (0x81, 0x8D, 0x8F, 0x90 and 0x9D are undefined and decode to U+FFFD), so any
/// torrent whose piece hashes contained one of those bytes produced a corrupted
/// info hash. This port keeps the raw bytes and only uses Latin-1 for the
/// textual view, which is a lossless byte&lt;-&gt;char mapping.
/// </remarks>
public abstract class BValue
{
    /// <summary>Lossless byte &lt;-&gt; char mapping used for the textual view of binary data.</summary>
    public static readonly Encoding Binary = Encoding.Latin1;

    public abstract void WriteTo(Stream output);

    public byte[] Encode()
    {
        using var buffer = new MemoryStream();
        this.WriteTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>Textual value of a string or number node, or <c>null</c> for containers.</summary>
    public static string? AsString(BValue? value) => value switch
    {
        BString s => s.Text,
        BNumber n => n.Text,
        _ => null,
    };

    public static long AsInt64(BValue? value, long fallback) => value switch
    {
        BNumber n => n.Value,
        BString s when long.TryParse(s.Text, out var parsed) => parsed,
        _ => fallback,
    };
}

public sealed class BString : BValue
{
    public BString(byte[] bytes) => this.Bytes = bytes;

    public BString(string text) => this.Bytes = Binary.GetBytes(text);

    public byte[] Bytes { get; }

    public string Text => Binary.GetString(this.Bytes);

    public int Length => this.Bytes.Length;

    public override void WriteTo(Stream output)
    {
        var prefix = Binary.GetBytes(this.Bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":");
        output.Write(prefix, 0, prefix.Length);
        output.Write(this.Bytes, 0, this.Bytes.Length);
    }

    public override string ToString() => this.Text;
}

public sealed class BNumber : BValue
{
    public BNumber(long value) => this.Value = value;

    public long Value { get; }

    public string Text => this.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public override void WriteTo(Stream output)
    {
        var bytes = Binary.GetBytes("i" + this.Text + "e");
        output.Write(bytes, 0, bytes.Length);
    }

    public override string ToString() => this.Text;
}

public sealed class BList : BValue
{
    private readonly List<BValue> items = [];

    public IReadOnlyList<BValue> Items => this.items;

    public int Count => this.items.Count;

    public BValue this[int index] => this.items[index];

    public void Add(BValue value) => this.items.Add(value);

    public override void WriteTo(Stream output)
    {
        output.WriteByte((byte)'l');
        foreach (var item in this.items)
        {
            item.WriteTo(output);
        }

        output.WriteByte((byte)'e');
    }
}

public sealed class BDictionary : BValue
{
    private readonly Dictionary<string, BValue> entries = [];
    private readonly List<string> insertionOrder = [];

    public IReadOnlyCollection<string> Keys => this.insertionOrder;

    public bool Contains(string key) => this.entries.ContainsKey(key);

    public BValue? this[string key]
    {
        get => this.entries.TryGetValue(key, out var value) ? value : null;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!this.entries.ContainsKey(key))
            {
                this.insertionOrder.Add(key);
            }

            this.entries[key] = value;
        }
    }

    public string GetString(string key) => AsString(this[key]) ?? string.Empty;

    public long GetInt64(string key, long fallback = 0) => AsInt64(this[key], fallback);

    /// <summary>
    /// Re-encodes the dictionary. Bencode mandates keys sorted by raw byte value;
    /// the info hash is only reproducible when that ordering is honoured.
    /// </summary>
    public override void WriteTo(Stream output)
    {
        output.WriteByte((byte)'d');
        foreach (var key in this.insertionOrder.OrderBy(Binary.GetBytes, ByteArrayComparer.Instance))
        {
            new BString(key).WriteTo(output);
            this.entries[key].WriteTo(output);
        }

        output.WriteByte((byte)'e');
    }

    private sealed class ByteArrayComparer : IComparer<byte[]>
    {
        internal static readonly ByteArrayComparer Instance = new();

        public int Compare(byte[]? x, byte[]? y) => x.AsSpan().SequenceCompareTo(y.AsSpan());
    }
}
