// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using System.Buffers.Binary;
using Tedd.OpenVDB.Points;

namespace Tedd.OpenVDB.Tests;

public class StringAttributeTests
{
    [Fact]
    public void CodecsUseNativeTypeNamesAndUnsignedStorage()
    {
        var full = new StringAttributeArray(3, uniformIndex: uint.MaxValue);
        Assert.Equal(new AttributeType("uint32", "str"), full.Type);
        Assert.True(StringAttributeArray.IsString(full));
        Assert.Equal(4, full.StorageTypeSize);
        Assert.Equal(uint.MaxValue, full.Get(2));
        Assert.Equal("ffffffff", Convert.ToHexStringLower(full.GetStorageBytes()));

        var truncated = new TypedAttributeArray<uint, TruncatedStringCodec>(
            3,
            uniformValue: uint.MaxValue - 1);
        Assert.Equal(new AttributeType("uint32", "str_trnc"), truncated.Type);
        Assert.False(StringAttributeArray.IsString(truncated));
        Assert.Equal(2, truncated.StorageTypeSize);
        Assert.Equal(65534U, truncated.Get(2));
        Assert.Equal("feff", Convert.ToHexStringLower(truncated.GetStorageBytes()));
    }

    [Fact]
    public void DefaultRegistryCreatesBothNativeStringCodecLayouts()
    {
        AttributeArrayRegistry registry = AttributeArrayRegistry.CreateDefault();
        AttributeArray full = registry.Create(new AttributeType("uint32", "str"), 5);
        AttributeArray truncated = registry.Create(new AttributeType("uint32", "str_trnc"), 5);
        Assert.IsType<TypedAttributeArray<uint, StringCodec>>(full);
        Assert.IsType<TypedAttributeArray<uint, TruncatedStringCodec>>(truncated);
        Assert.True(StringAttributeArray.IsString(full));
    }

    [Theory]
    [InlineData("string:")]
    [InlineData("string:abc")]
    [InlineData("string:-1")]
    [InlineData("string:+1")]
    [InlineData("string: 1")]
    [InlineData("string:1 ")]
    [InlineData("string:1x")]
    [InlineData("string:4294967295")]
    [InlineData("string:999999999999999999999999")]
    public void CacheRejectsMalformedOrReservedIndices(string key)
    {
        var metadata = new MetaMap();
        metadata.InsertMeta(key, "bad");
        Assert.Throws<InvalidDataException>(() => new StringMetaCache(metadata));
    }

    [Fact]
    public void CacheIgnoresWrongTypesAndNonprefixNames()
    {
        var metadata = new MetaMap();
        metadata.InsertMeta("string:not-an-index", 17);
        metadata.InsertMeta("String:0", "wrong case");
        metadata.InsertMeta("prefix-string:0", "wrong prefix");
        var cache = new StringMetaCache(metadata);
        Assert.True(cache.IsEmpty);
    }

    [Fact]
    public void CacheAcceptsLeadingZerosAndMaximumPositiveIndex()
    {
        var metadata = new MetaMap();
        metadata.InsertMeta("string:0002", "three");
        metadata.InsertMeta("string:4294967294", "maximum");
        var cache = new StringMetaCache(metadata);
        Assert.True(cache.TryGetIndex("three", out uint three));
        Assert.Equal(3U, three);
        Assert.True(cache.TryGetIndex("maximum", out uint maximum));
        Assert.Equal(uint.MaxValue, maximum);
    }

    [Fact]
    public void CacheAndWriterRemainSnapshotsUntilReset()
    {
        var metadata = new MetaMap();
        metadata.InsertMeta("string:0", "one");
        var cache = new StringMetaCache(metadata);
        var array = new StringAttributeArray(2);
        using var writer = new StringAttributeWriteHandle(array, metadata);
        metadata.InsertMeta("string:1", "two");

        Assert.False(cache.TryGetIndex("two", out _));
        Assert.False(writer.Contains("two"));
        Assert.Throws<KeyNotFoundException>(() => writer.Set(0, "two"));

        cache.Reset(metadata);
        writer.ResetCache();
        Assert.True(cache.TryGetIndex("two", out uint index));
        Assert.Equal(2U, index);
        writer.Set(0, "two");
        Assert.Equal(2U, array.Get(0));
    }

    [Fact]
    public void ReadHandleResolvesLiveMetadataAndRejectsInvalidIndices()
    {
        var metadata = new MetaMap();
        var array = new StringAttributeArray(3);
        array.Expand();
        array.Set(0, 0);
        array.Set(1, 2);
        array.Set(2, uint.MaxValue);
        using var handle = new StringAttributeHandle(array, metadata);

        Assert.Equal(string.Empty, handle.Get(0));
        Assert.Throws<KeyNotFoundException>(() => handle.Get(1));
        Assert.Throws<KeyNotFoundException>(() => handle.Get(2));

        metadata.InsertMeta("string:1", "two");
        metadata.InsertMeta("string:4294967294", "maximum");
        Assert.Equal("two", handle.Get(1));
        Assert.Equal("maximum", handle.Get(2));
        metadata.RemoveMeta("string:1");
        Assert.Throws<KeyNotFoundException>(() => handle.Get(1));
    }

    [Fact]
    public void StringHandlesRejectOtherCodecLayouts()
    {
        Assert.Throws<ArgumentException>(() =>
            new StringAttributeHandle(new TypedAttributeArray<uint>(1), new MetaMap()));
        Assert.Throws<ArgumentException>(() =>
            new StringAttributeHandle(
                new TypedAttributeArray<uint, TruncatedStringCodec>(1),
                new MetaMap()));
    }

    [Fact]
    public void InserterUsesExistingValuesFreeIndicesAndHints()
    {
        var metadata = new MetaMap();
        metadata.InsertMeta("string:0", "one");
        metadata.InsertMeta("string:2", "three");
        var inserter = new StringMetaInserter(metadata);

        Assert.True(inserter.HasKey("one"));
        Assert.True(inserter.HasIndex(1));
        Assert.False(inserter.HasIndex(0));
        Assert.Equal(1U, inserter.Insert("one"));
        Assert.Equal(2U, inserter.Insert("two"));
        Assert.Equal(100U, inserter.Insert("hundred", 100));
        Assert.Equal("two", metadata.MetaValue<string>("string:1"));
        Assert.Equal("hundred", metadata.MetaValue<string>("string:99"));

        metadata.RemoveMeta("string:1");
        inserter.ResetCache();
        Assert.False(inserter.HasKey("two"));
        Assert.Equal(2U, inserter.Insert("replacement"));
    }

    [Fact]
    public void WriteHandleSupportsStridesCollapseFillAndEmptySentinel()
    {
        var metadata = new MetaMap();
        metadata.InsertMeta("string:0", "one");
        metadata.InsertMeta("string:1", "two");
        var array = new StringAttributeArray(2, 3);
        using var writer = new StringAttributeWriteHandle(array, metadata);

        writer.Set(0, 0, "one");
        writer.Set(0, 1, "two");
        writer.Set(0, 2, "");
        writer.Set(1, 0, "two");
        Assert.Equal("one", writer.Get(0, 0));
        Assert.Equal("two", writer.Get(0, 1));
        Assert.Equal(string.Empty, writer.Get(0, 2));
        Assert.False(writer.Compact());

        writer.Fill("one");
        Assert.True(writer.Compact());
        Assert.True(array.IsUniform);
        Assert.Equal("one", writer.Get(1, 2));
        writer.Collapse();
        Assert.Equal(string.Empty, writer.Get(1, 2));
        writer.Collapse("two");
        Assert.Equal("two", writer.Get(0, 0));
    }

    [Fact]
    public void DisposeThroughBaseTypeReleasesBothHandles()
    {
        var metadata = new MetaMap();
        metadata.InsertMeta("string:0", "one");
        var array = new StringAttributeArray(4);
        StringAttributeHandle handle = new StringAttributeWriteHandle(array, metadata);
        ((StringAttributeWriteHandle)handle).Set(0, "one");
        handle.Dispose();

        Assert.Throws<ObjectDisposedException>(() => handle.Get(0));
        Assert.Throws<ObjectDisposedException>(() => _ = handle.Array);
        Assert.Throws<ObjectDisposedException>(() => ((StringAttributeWriteHandle)handle).Contains("one"));
        Assert.Throws<ObjectDisposedException>(() => ((StringAttributeWriteHandle)handle).Set(0, "one"));
        handle.Dispose();
    }

    [Fact]
    public void StreamingReadHandleCollapsesOnDisposeButWriterDoesNot()
    {
        var metadata = new MetaMap();
        metadata.InsertMeta("string:0", "one");

        var readArray = new StringAttributeArray(4) { IsStreaming = true };
        readArray.Expand();
        readArray.Fill(1);
        var reader = new StringAttributeHandle(readArray, metadata);
        reader.Dispose();
        Assert.True(readArray.IsUniform);
        Assert.Equal(0U, readArray.Get(0));

        var writeArray = new StringAttributeArray(4) { IsStreaming = true };
        var writer = new StringAttributeWriteHandle(writeArray, metadata);
        writer.Set(0, "one");
        writer.Dispose();
        Assert.False(writeArray.IsUniform);
        Assert.Equal(1U, writeArray.Get(0));
    }

    [Fact]
    public void DeepCopyPreservesStorageFlagsIndependenceAndExpansionBudget()
    {
        var source = new StringAttributeArray(8, maximumStorageBytes: 32)
        {
            IsHidden = true,
            IsStreaming = true
        };
        StringAttributeArray copy = source.DeepCopy();
        Assert.True(source.StorageEquals(copy));
        copy.Expand();
        copy.Set(7, 17);
        Assert.Equal(0U, source.Get(7));
        Assert.Equal(17U, copy.Get(7));
        Assert.True(copy.IsHidden);
        Assert.True(copy.IsStreaming);

        var bounded = new StringAttributeArray(9, maximumStorageBytes: 32);
        StringAttributeArray boundedCopy = bounded.DeepCopy();
        Assert.Throws<InvalidOperationException>(() => bounded.Expand());
        Assert.Throws<InvalidOperationException>(() => boundedCopy.Expand());
    }

    [Fact]
    public void StreamRoundTripRetainsInvalidIndexUntilStringResolution()
    {
        var source = new StringAttributeArray(2, uniformIndex: 73);
        using var stream = new MemoryStream();
        AttributeStreams.Write(stream, source, compress: true);
        stream.Position = 0;
        var decoded = AttributeStreams.Read<uint, StringCodec>(stream);
        Assert.Equal(73U, decoded.Get(1));

        using var handle = new StringAttributeHandle(decoded, new MetaMap());
        Assert.Throws<KeyNotFoundException>(() => handle.Get(1));
    }

    [Fact]
    public void TruncatedCodecWrapsBeforeRawStreamSerialization()
    {
        var array = new TypedAttributeArray<uint, TruncatedStringCodec>(3);
        array.Expand();
        array.Set(0, 70_000);
        array.Set(1, uint.MaxValue);
        array.Set(2, 65_537);
        Assert.Equal(new uint[] { 4_464, 65_535, 1 }, Enumerable.Range(0, 3).Select(array.Get));

        using var stream = new MemoryStream();
        AttributeStreams.Write(stream, array);
        stream.Position = 0;
        var decoded = AttributeStreams.Read<uint, TruncatedStringCodec>(stream);
        Assert.Equal(new uint[] { 4_464, 65_535, 1 }, Enumerable.Range(0, 3).Select(decoded.Get));
        Assert.Equal("7011ffff0100", Convert.ToHexStringLower(decoded.GetStorageBytes()));
    }

    [Fact]
    public void CodecReadsAndWritesLittleEndianWords()
    {
        Span<byte> full = stackalloc byte[4];
        StringCodec.Encode(0x89ABCDEF, full);
        Assert.Equal(0x89ABCDEFU, BinaryPrimitives.ReadUInt32LittleEndian(full));
        Assert.Equal(0x89ABCDEFU, StringCodec.Decode(full));

        Span<byte> truncated = stackalloc byte[2];
        TruncatedStringCodec.Encode(0x1234ABCD, truncated);
        Assert.Equal(0xABCDU, TruncatedStringCodec.Decode(truncated));
    }
}
