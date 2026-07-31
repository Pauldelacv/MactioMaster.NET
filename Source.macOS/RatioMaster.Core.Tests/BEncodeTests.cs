namespace RatioMaster.Core.Tests;

using RatioMaster.Core.BEncode;

using Xunit;

public class BEncodeTests
{
    [Fact]
    public void ParsesTheFourBencodeTypes()
    {
        var value = BEncodeParser.Parse(TorrentBuilder.Bytes("d3:inti42e3:str5:hello4:listli1ei2ee4:dictd1:ai0eee"));

        var root = Assert.IsType<BDictionary>(value);
        Assert.Equal(42, root.GetInt64("int"));
        Assert.Equal("hello", root.GetString("str"));

        var list = Assert.IsType<BList>(root["list"]);
        Assert.Equal(2, list.Count);
        Assert.Equal(1, BValue.AsInt64(list[0], -1));

        var nested = Assert.IsType<BDictionary>(root["dict"]);
        Assert.Equal(0, nested.GetInt64("a"));
    }

    [Fact]
    public void ParsesNegativeIntegers()
    {
        var root = Assert.IsType<BDictionary>(BEncodeParser.Parse(TorrentBuilder.Bytes("d1:ni-17ee")));

        Assert.Equal(-17, root.GetInt64("n"));
    }

    [Fact]
    public void KeepsBinaryStringsIntact()
    {
        var payload = TorrentBuilder.Bytes("d5:bytes4:")
            .Concat(new byte[] { 0x00, 0x81, 0xFF, 0x9D })
            .Concat(TorrentBuilder.Bytes("e"))
            .ToArray();

        var root = Assert.IsType<BDictionary>(BEncodeParser.Parse(payload));

        var bytes = Assert.IsType<BString>(root["bytes"]);
        Assert.Equal([0x00, 0x81, 0xFF, 0x9D], bytes.Bytes);
    }

    [Fact]
    public void RoundTripsThroughEncode()
    {
        var payload = TorrentBuilder.Bytes("d1:ai1e1:bl1:x1:yee");

        var encoded = BEncodeParser.Parse(payload).Encode();

        Assert.Equal(payload, encoded);
    }

    [Fact]
    public void SortsDictionaryKeysWhenEncoding()
    {
        var dictionary = new BDictionary
        {
            ["zebra"] = new BNumber(1),
            ["alpha"] = new BNumber(2),
        };

        var encoded = BValue.Binary.GetString(dictionary.Encode());

        Assert.Equal("d5:alphai2e5:zebrai1ee", encoded);
    }

    [Theory]
    [InlineData("d")]
    [InlineData("l")]
    [InlineData("i42")]
    [InlineData("5:abc")]
    [InlineData("d3:key")]
    public void ThrowsOnTruncatedInput(string payload)
    {
        Assert.Throws<BEncodeException>(() => BEncodeParser.Parse(TorrentBuilder.Bytes(payload)));
    }

    [Fact]
    public void RecordsTheByteRangeOfEachTopLevelValue()
    {
        var payload = TorrentBuilder.Bytes("d4:infod1:ai1eee");

        BEncodeParser.Parse(payload, out var ranges);

        Assert.True(ranges.ContainsKey("info"));
        Assert.Equal("d1:ai1ee", BValue.Binary.GetString(payload[ranges["info"]]));
    }
}
