// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using System.IO.Compression;
using System.Text.Json;
using Tedd.OpenVDB.Points;

namespace Tedd.OpenVDB.Tests;

public sealed class PagedStreamsNativeTests
{
    private const string Commit = "f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf";
    private static readonly Lazy<JsonDocument> Fixture = new(() =>
    {
        using var file = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "openvdb-13.0.1-paged-stream.json.gz"));
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    });

    public static IEnumerable<object[]> Cases() =>
        Fixture.Value.RootElement.GetProperty("cases").EnumerateArray()
            .Select(item => new object[] { item.GetProperty("profile").GetInt32() });

    [Theory, MemberData(nameof(Cases))]
    public void NativeAndManagedPagedBytesAndReadsMatch(int profile)
    {
        Assert.Equal(Commit, Fixture.Value.RootElement.GetProperty("commit").GetString());
        JsonElement item = Fixture.Value.RootElement.GetProperty("cases")[profile];
        Assert.Equal(profile, item.GetProperty("profile").GetInt32());
        byte[][] blocks = BuildBlocks(profile);
        Assert.Equal(item.GetProperty("sizes").EnumerateArray().Select(static value => value.GetInt32()), blocks.Select(static block => block.Length));
        var encoded = Encode(blocks);
        byte[] nativeHeaders = Convert.FromHexString(item.GetProperty("headers").GetString()!);
        byte[] nativeData = Convert.FromHexString(item.GetProperty("data").GetString()!);
        HeaderRecord[] nativeRecords = ParseHeaders(nativeHeaders);
        HeaderRecord[] managedRecords = ParseHeaders(encoded.Header);
        Assert.Equal(nativeRecords.Select(static record => record.DecodedBytes), managedRecords.Select(static record => record.DecodedBytes));
        Assert.Equal(nativeRecords.Select(static record => record.IsCompressed), managedRecords.Select(static record => record.IsCompressed));
        if (nativeRecords.All(static record => !record.IsCompressed))
        {
            Assert.Equal(nativeHeaders, encoded.Header);
            Assert.Equal(nativeData, encoded.Data);
        }
        using var stream = new MemoryStream([.. nativeHeaders, .. nativeData, 0xA9]);
        var sizeReader = new PagedInputStream(stream);
        PageHandle[] handles = blocks.Select(block => sizeReader.CreateHandle(block.Length)).ToArray();
        Assert.Equal(nativeHeaders.Length, stream.Position);
        var dataReader = new PagedInputStream(stream);
        for (int i = 0; i < handles.Length; i++) dataReader.Read(handles[i], blocks[i].Length, delayed: false);
        Assert.Equal(nativeHeaders.Length + nativeData.Length, stream.Position); Assert.Equal(0xA9, stream.ReadByte());
        for (int i = 0; i < handles.Length; i++) Assert.Equal(blocks[i], handles[i].Read());

        string? export = Environment.GetEnvironmentVariable("VDB_PAGED_STREAM_EXPORT");
        if (!string.IsNullOrEmpty(export))
        {
            Directory.CreateDirectory(export);
            string path = Path.Combine(export, profile + ".bin");
            File.WriteAllBytes(path + ".tmp", [.. encoded.Header, .. encoded.Data]);
            File.Move(path + ".tmp", path, true);
        }
    }

    private static (byte[] Header, byte[] Data) Encode(IReadOnlyList<byte[]> blocks)
    {
        using var header = new MemoryStream();
        using (var writer = new PagedOutputStream(header, sizeOnly: true))
        {
            foreach (byte[] block in blocks) writer.Write(block);
            writer.Flush();
        }
        using var data = new MemoryStream();
        using (var writer = new PagedOutputStream(data))
        {
            foreach (byte[] block in blocks) writer.Write(block);
            writer.Flush();
        }
        return (header.ToArray(), data.ToArray());
    }

    private static HeaderRecord[] ParseHeaders(ReadOnlySpan<byte> headers)
    {
        var records = new List<HeaderRecord>();
        while (!headers.IsEmpty)
        {
            Assert.True(headers.Length >= 4);
            int encoded = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(headers);
            headers = headers[4..];
            Assert.NotEqual(0, encoded);
            if (encoded < 0) records.Add(new HeaderRecord(false, -encoded));
            else
            {
                Assert.True(headers.Length >= 4);
                int decoded = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(headers);
                headers = headers[4..];
                Assert.True(decoded > 0);
                records.Add(new HeaderRecord(true, decoded));
            }
        }
        return records.ToArray();
    }

    private readonly record struct HeaderRecord(bool IsCompressed, int DecodedBytes);

    private static int[] Sizes(int profile)
    {
        int[] thresholds = [1, 47, 48, 60, 127, 128, 129, 4096];
        if (profile < 8) return [thresholds[profile]];
        if (profile == 8) return [PagedOutputStream.PageSize];
        if (profile == 9) return [PagedOutputStream.PageSize + 1];
        if (profile == 10) return [PagedOutputStream.PageSize / 3, PagedOutputStream.PageSize / 3, PagedOutputStream.PageSize / 3, PagedOutputStream.PageSize / 3, 1];
        if (profile == 11) return [PagedOutputStream.PageSize - 1, 2];
        if (profile == 12) return [200, 200, PagedOutputStream.PageSize];
        if (profile == 13) return [PagedOutputStream.PageSize + 1, PagedOutputStream.PageSize + 1, 3];
        int count = 2 + profile % 5;
        var result = new int[count];
        for (int i = 0; i < count; i++) result[i] = 17 + ((profile * 7919 + i * 104729) % 50000);
        return result;
    }

    private static int Mode(int profile) => profile is >= 8 and <= 13 ? 0 : profile % 4;

    private static byte[][] BuildBlocks(int profile)
    {
        int[] sizes = Sizes(profile);
        var result = new byte[sizes.Length][];
        for (int block = 0; block < sizes.Length; block++)
        {
            byte[] value = new byte[sizes[block]];
            int kind = Mode(profile);
            if (kind == 0) value.AsSpan().Fill((byte)((profile * 13 + block * 7) & 255));
            else if (kind == 1)
                for (int i = 0; i < value.Length; i++) value[i] = (byte)((i + block * 17 + profile) & 255);
            else if (kind == 2)
            {
                uint state = unchecked(0x9e3779b9u ^ (uint)(profile * 65537 + block));
                for (int i = 0; i < value.Length; i++)
                {
                    state ^= state << 13; state ^= state >> 17; state ^= state << 5; value[i] = (byte)state;
                }
            }
            else
                for (int i = 0; i < value.Length; i++) value[i] = (byte)(((i / 31) + block * 7 + profile) & 255);
            result[block] = value;
        }
        return result;
    }
}
