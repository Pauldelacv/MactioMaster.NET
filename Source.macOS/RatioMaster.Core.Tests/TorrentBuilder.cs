namespace RatioMaster.Core.Tests;

using System.Security.Cryptography;
using System.Text;

/// <summary>Builds synthetic .torrent payloads so the tests need no fixtures on disk.</summary>
internal static class TorrentBuilder
{
    /// <summary>
    /// A single-file torrent. <paramref name="pieceHashBytes"/> lets a test inject
    /// byte values that Windows-1252 cannot round-trip.
    /// </summary>
    internal static byte[] SingleFile(string announce, string name, long length, byte[]? pieceHashBytes = null)
    {
        var pieces = pieceHashBytes ?? RandomNumberGenerator.GetBytes(20);
        var info = Bytes($"d6:lengthi{length}e4:name{name.Length}:{name}12:piece lengthi262144e6:pieces{pieces.Length}:")
            .Concat(pieces)
            .Concat(Bytes("e"))
            .ToArray();

        return Bytes($"d8:announce{announce.Length}:{announce}4:info")
            .Concat(info)
            .Concat(Bytes("e"))
            .ToArray();
    }

    internal static byte[] MultiFile(string announce, string name, params (string Path, long Length)[] files)
    {
        var pieces = RandomNumberGenerator.GetBytes(40);
        var fileList = new List<byte>(Bytes("l"));
        foreach (var (path, length) in files)
        {
            fileList.AddRange(Bytes($"d6:lengthi{length}e4:pathl{path.Length}:{path}ee"));
        }

        fileList.AddRange(Bytes("e"));

        var info = Bytes("d5:files")
            .Concat(fileList)
            .Concat(Bytes($"4:name{name.Length}:{name}12:piece lengthi262144e6:pieces{pieces.Length}:"))
            .Concat(pieces)
            .Concat(Bytes("e"))
            .ToArray();

        return Bytes($"d8:announce{announce.Length}:{announce}4:info")
            .Concat(info)
            .Concat(Bytes("e"))
            .ToArray();
    }

    /// <summary>SHA-1 over the exact bytes of the info dictionary.</summary>
    internal static byte[] ExpectedInfoHash(byte[] torrent)
    {
        var marker = Bytes("4:info");
        var start = IndexOf(torrent, marker) + marker.Length;

        // The info dictionary runs to the last byte before the outer 'e'.
        return SHA1.HashData(torrent.AsSpan(start, torrent.Length - start - 1).ToArray());
    }

    internal static byte[] Bytes(string text) => Encoding.Latin1.GetBytes(text);

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i + needle.Length <= haystack.Length; i++)
        {
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle))
            {
                return i;
            }
        }

        return -1;
    }
}
