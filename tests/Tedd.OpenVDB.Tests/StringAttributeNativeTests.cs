// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using System.IO.Compression;
using System.Text.Json;
using Tedd.OpenVDB.Points;

namespace Tedd.OpenVDB.Tests;

public class StringAttributeNativeTests
{
    private static readonly Lazy<JsonDocument> Fixture = new(() =>
    {
        using var file = File.OpenRead(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "openvdb-13.0.1-string-attributes.json.gz"));
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    });

    public static IEnumerable<object[]> StreamCases()
    {
        foreach (JsonElement item in Fixture.Value.RootElement.GetProperty("cases").EnumerateArray())
        {
            if (item.GetProperty("kind").GetString() != "stream") continue;
            int id = item.GetProperty("id").GetInt32();
            string label = (item.GetProperty("truncated").GetInt32() != 0 ? "str_trnc/" : "str/") +
                item.GetProperty("profile").GetInt32();
            yield return [id, label, false];
            yield return [id, label, true];
        }
    }

    [Theory, MemberData(nameof(StreamCases))]
    public void NativeStreamsAndManagedExportsPreserveStringIndices(int id, string label, bool compress)
    {
        Assert.NotEmpty(label);
        Assert.Equal(
            "f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf",
            Fixture.Value.RootElement.GetProperty("commit").GetString());

        JsonElement item = FindStream(id);
        if (item.GetProperty("truncated").GetInt32() != 0)
            RunStream<TruncatedStringCodec>(item, compress);
        else
            RunStream<StringCodec>(item, compress);
    }

    private static void RunStream<TCodec>(JsonElement item, bool compress)
        where TCodec : unmanaged, IAttributeCodec<uint>
    {
        int length = item.GetProperty("n").GetInt32();
        int strideOrTotal = item.GetProperty("s").GetInt32();
        bool constant = item.GetProperty("constant").GetInt32() != 0;
        uint initial = item.GetProperty("initial").GetUInt32();
        AttributeArray<uint> source = typeof(TCodec) == typeof(StringCodec)
            ? new StringAttributeArray(length, strideOrTotal, constant, initial)
            : new TypedAttributeArray<uint, TCodec>(length, strideOrTotal, constant, initial);

        if (item.GetProperty("expanded").GetInt32() != 0)
        {
            source.Expand();
            int index = 0;
            foreach (JsonElement input in item.GetProperty("inputs").EnumerateArray())
                source.Set(index++, input.GetUInt32());
        }

        AttributeFlags flags = (AttributeFlags)item.GetProperty("flags").GetInt32();
        source.IsHidden = flags.HasFlag(AttributeFlags.Hidden);
        source.IsTransient = flags.HasFlag(AttributeFlags.Transient);
        source.IsStreaming = flags.HasFlag(AttributeFlags.Streaming);
        Snapshot(item, source);

        string encoding = compress ? "blosc" : "raw";
        byte[] nativeBytes = Convert.FromHexString(item.GetProperty(encoding).GetString()!);
        using var managedStream = new MemoryStream();
        AttributeStreams.Write(managedStream, source, compress, outputTransient: true);
        byte[] managedBytes = managedStream.ToArray();
        if (!compress) Assert.Equal(nativeBytes, managedBytes);
        Export(id: item.GetProperty("id").GetInt32(), encoding, managedBytes);

        using var nativeStream = new MemoryStream([.. nativeBytes, 0xA7]);
        TypedAttributeArray<uint, TCodec> decoded = AttributeStreams.Read<uint, TCodec>(nativeStream);
        Snapshot(item, decoded);
        Assert.Equal(nativeBytes.Length, nativeStream.Position);
        Assert.Equal(0xA7, nativeStream.ReadByte());

        nativeStream.Position = 0;
        AttributeStreamMetadata metadata = AttributeStreams.ReadMetadata(nativeStream);
        AttributeArray dynamic = AttributeStreams.ReadBuffers(
            nativeStream,
            new AttributeType("uint32", TCodec.Name),
            AttributeArrayRegistry.CreateDefault(),
            metadata);
        Snapshot(item, Assert.IsAssignableFrom<AttributeArray<uint>>(dynamic));
        Assert.Equal(nativeBytes.Length, nativeStream.Position);

        managedStream.Position = 0;
        Snapshot(item, AttributeStreams.Read<uint, TCodec>(managedStream));
        Assert.Equal(managedStream.Length, managedStream.Position);
    }

    private static void Snapshot(JsonElement expected, AttributeArray<uint> array)
    {
        Assert.Equal(expected.GetProperty("size").GetInt32(), array.Size);
        Assert.Equal(expected.GetProperty("stride").GetInt32(), array.Stride);
        Assert.Equal(expected.GetProperty("dataSize").GetInt32(), array.DataSize);
        Assert.Equal(expected.GetProperty("storageSize").GetInt32(), array.StorageTypeSize);
        Assert.Equal(expected.GetProperty("flags").GetInt32(), (int)array.Flags);
        Assert.Equal(expected.GetProperty("uniform").GetInt32() != 0, array.IsUniform);
        Assert.Equal(
            expected.GetProperty("storage").GetString(),
            Convert.ToHexStringLower(array.GetStorageBytes()));

        JsonElement indices = expected.GetProperty("indices");
        Assert.Equal(indices.GetArrayLength(), array.DataSize);
        for (int i = 0; i < indices.GetArrayLength(); ++i)
            Assert.Equal(indices[i].GetUInt32(), array.Get(i));
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("one")]
    [InlineData("ignored")]
    [InlineData("duplicates")]
    public void NativeCacheSnapshotsMatch(string name)
    {
        var metadata = new MetaMap();
        if (name != "empty") metadata.InsertMeta("string:0", "alpha");
        if (name is "ignored" or "duplicates")
        {
            metadata.InsertMeta("string:5", 7);
            metadata.InsertMeta("other", "ignored");
        }
        if (name == "duplicates")
        {
            metadata.InsertMeta("string:9", "alpha");
            metadata.InsertMeta("string:0002", "beta");
            metadata.InsertMeta("string:17", "på雪");
        }

        var actual = new StringMetaCache(metadata).Map
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => (pair.Key, pair.Value))
            .ToArray();
        JsonElement expected = Find("cache", name).GetProperty("entries");
        Assert.Equal(expected.GetArrayLength(), actual.Length);
        for (int i = 0; i < actual.Length; ++i)
        {
            Assert.Equal(expected[i][0].GetString(), actual[i].Key);
            Assert.Equal(expected[i][1].GetUInt32(), actual[i].Value);
        }
    }

    [Fact]
    public void NativeInserterBlockAndHintSequenceMatches()
    {
        var metadata = new MetaMap();
        metadata.InsertMeta("string:0", "A");
        metadata.InsertMeta("string:2", "C");
        metadata.InsertMeta("irrelevant", 4);
        var inserter = new StringMetaInserter(metadata);
        var actual = new List<(string Value, uint Index)>();
        void Add(string value, uint hint = 0) => actual.Add((value, inserter.Insert(value, hint)));

        Add("A");
        Add("B");
        Add("D", 1000);
        Add("E", 1000);
        metadata.RemoveMeta("string:1");
        inserter.ResetCache();
        Add("F");
        Add("G", 999);
        Add("H", 1001);
        Add("");

        JsonElement expected = Find("inserter").GetProperty("ops");
        Assert.Equal(expected.GetArrayLength(), actual.Count);
        for (int i = 0; i < actual.Count; ++i)
        {
            Assert.Equal(expected[i][0].GetString(), actual[i].Value);
            Assert.Equal(expected[i][1].GetUInt32(), actual[i].Index);
        }

        var strings = metadata
            .Where(pair => pair.Key.StartsWith("string:", StringComparison.Ordinal) &&
                pair.Value.TryGetValue<string>(out _))
            .Select(pair => (pair.Key, pair.Value.GetValue<string>()))
            .ToArray();
        JsonElement expectedMetadata = Find("inserter").GetProperty("metadata");
        Assert.Equal(expectedMetadata.GetArrayLength(), strings.Length);
        for (int i = 0; i < strings.Length; ++i)
        {
            Assert.Equal(expectedMetadata[i][0].GetString(), strings[i].Key);
            Assert.Equal(expectedMetadata[i][1].GetString(), strings[i].Item2);
        }
    }

    [Fact]
    public void NativeHandleMutationAndLiveMetadataSequenceMatches()
    {
        JsonElement expected = Find("handle");
        var metadata = new MetaMap();
        var array = new StringAttributeArray(4);
        using var handle = new StringAttributeWriteHandle(array, metadata);
        metadata.InsertMeta("string:0", "one");
        metadata.InsertMeta("string:1", "two");

        bool stale = false;
        try { handle.Set(0, "one"); }
        catch (KeyNotFoundException) { stale = true; }
        Assert.Equal(expected.GetProperty("stale").GetInt32() != 0, stale);

        handle.ResetCache();
        handle.Set(0, "one");
        handle.Set(1, "two");
        handle.Set(2, "one");
        handle.Set(3, "");
        JsonElement initial = expected.GetProperty("initial");
        for (int i = 0; i < initial.GetArrayLength(); ++i)
            Assert.Equal(initial[i].GetString(), handle.Get(i));

        Assert.Equal(expected.GetProperty("compact1").GetInt32() != 0, handle.Compact());
        handle.Set(1, "one");
        handle.Set(3, "one");
        Assert.Equal(expected.GetProperty("compact2").GetInt32() != 0, handle.Compact());
        handle.Expand();
        handle.Set(2, "two");
        handle.Fill("two");
        handle.Collapse("one");
        handle.Expand();
        handle.Set(3, "two");

        using var live = new StringAttributeHandle(array, metadata);
        metadata.RemoveMeta("string:1");
        Assert.Throws<KeyNotFoundException>(() => live.Get(3));
        metadata.InsertMeta("string:1", "changed");
        Assert.Equal(expected.GetProperty("changed").GetString(), live.Get(3));

        JsonElement indices = expected.GetProperty("indices");
        for (int i = 0; i < indices.GetArrayLength(); ++i)
            Assert.Equal(indices[i].GetUInt32(), array.Get(i));
    }

    [Fact]
    public void NativeMalformedCacheKeysThrow()
    {
        foreach (JsonElement item in Fixture.Value.RootElement.GetProperty("cases").EnumerateArray())
        {
            if (item.GetProperty("kind").GetString() != "malformed") continue;
            var metadata = new MetaMap();
            metadata.InsertMeta(item.GetProperty("key").GetString()!, "bad");
            if (item.GetProperty("error").GetInt32() != 0)
                Assert.Throws<InvalidDataException>(() => new StringMetaCache(metadata));
        }
    }

    private static JsonElement FindStream(int id)
    {
        foreach (JsonElement item in Fixture.Value.RootElement.GetProperty("cases").EnumerateArray())
        {
            if (item.GetProperty("kind").GetString() == "stream" &&
                item.GetProperty("id").GetInt32() == id)
                return item;
        }
        throw new InvalidDataException($"Missing string stream fixture {id}.");
    }

    private static JsonElement Find(string kind, string? name = null)
    {
        foreach (JsonElement item in Fixture.Value.RootElement.GetProperty("cases").EnumerateArray())
        {
            if (item.GetProperty("kind").GetString() != kind) continue;
            if (name is null || item.GetProperty("name").GetString() == name) return item;
        }
        throw new InvalidDataException($"Missing {kind}/{name} fixture.");
    }

    private static void Export(int id, string encoding, byte[] bytes)
    {
        string? directory = Environment.GetEnvironmentVariable("VDB_STRING_ATTRIBUTE_EXPORT");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, id + "-" + encoding + ".bin");
        File.WriteAllBytes(path + ".tmp", bytes);
        File.Move(path + ".tmp", path, true);
    }
}
