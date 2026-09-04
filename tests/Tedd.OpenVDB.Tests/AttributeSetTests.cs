// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using System.Buffers.Binary;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Points;

namespace Tedd.OpenVDB.Tests;

public sealed class AttributeSetTests
{
    private static readonly AttributeType Vec3fType = new("vec3s", "null");
    private static readonly AttributeType FloatType = new("float", "null");
    private static readonly AttributeType IntType = new("int32", "null");

    [Fact]
    public void DescriptorPreservesPositionOrderAndSupportsNativeDuplicateRules()
    {
        var descriptor = AttributeDescriptor.Create(Vec3fType);
        descriptor = descriptor.DuplicateAppend("density", FloatType);
        descriptor = descriptor.DuplicateAppend("id", IntType);
        Assert.Equal(3, descriptor.Size);
        Assert.Equal(["P", "density", "id"], descriptor.Entries().Select(static entry => entry.Name));
        Assert.Equal(0, descriptor.Find("P")); Assert.Equal(1, descriptor.Find("density")); Assert.Equal(2, descriptor.Find("id"));
        Assert.Equal(AttributeDescriptor.InvalidPosition, descriptor.Find("missing"));
        Assert.Equal(2, descriptor.Insert("id", IntType));
        Assert.Throws<ArgumentException>(() => descriptor.Insert("id", FloatType));
        var copy = new AttributeDescriptor(descriptor);
        Assert.Equal(descriptor, copy); Assert.True(descriptor.HasSameAttributes(copy));
        copy.Metadata.InsertMeta("tag", 3); Assert.NotEqual(descriptor, copy); Assert.True(descriptor.HasSameAttributes(copy));
    }

    public static TheoryData<string, bool> Names => new()
    {
        { "test1", true }, { "abc_def", true }, { "abc|def", true }, { "abc:def", true },
        { "Z", true }, { "0", true }, { "", false }, { "test1!", false }, { "abc=def", false },
        { "abc def", false }, { "abc*def", false }, { "^abc", false }, { "é", false }, { "\0", false }
    };

    [Theory, MemberData(nameof(Names))]
    public void NativeNameAlphabetIsEnforced(string name, bool valid)
    {
        Assert.Equal(valid, AttributeDescriptor.ValidName(name));
        var descriptor = new AttributeDescriptor();
        if (valid) Assert.Equal(0, descriptor.Insert(name, IntType));
        else Assert.Throws<ArgumentException>(() => descriptor.Insert(name, IntType));
    }

    public static TheoryData<string, string[], string[], bool> ParsedNames => new()
    {
        { "", [], [], false },
        { "group1", ["group1"], [], false },
        { "group1 group2", ["group1", "group2"], [], false },
        { "group1 ^group2", ["group1"], ["group2"], false },
        { "group1 !group2", ["group1"], ["group2"], false },
        { "^group1 group2", ["group2"], ["group1"], false },
        { "^group1 ^group2", [], ["group1", "group2"], false },
        { "group1 ^group2 ^group3 group4", ["group1", "group4"], ["group2", "group3"], false },
        { "*", [], [], true },
        { "before * ignored ^excluded", [], ["excluded"], true }
    };

    [Theory, MemberData(nameof(ParsedNames))]
    public void ParseNamesMatchesWildcardAndNegationRules(string text, string[] included, string[] excluded, bool all)
    {
        var actual = AttributeDescriptor.ParseNames(text);
        Assert.Equal(included, actual.Include); Assert.Equal(excluded, actual.Exclude); Assert.Equal(all, actual.IncludeAll);
    }

    [Theory]
    [InlineData("^")]
    [InlineData("!")]
    [InlineData("group$1")]
    [InlineData("^bad name*")]
    public void ParseNamesRejectsMalformedTokens(string text) =>
        Assert.Throws<ArgumentException>(() => AttributeDescriptor.ParseNames(text));

    [Fact]
    public void UniqueNamesAndRenamesFollowNativeSuffixAndCollisionRules()
    {
        var descriptor = AttributeDescriptor.Create([
            new("name", IntType), new("name0", IntType), new("name1", IntType), new("other", FloatType)]);
        Assert.Equal("name2", descriptor.UniqueName("name"));
        Assert.Equal("free", descriptor.UniqueName("free"));
        Assert.Equal(AttributeDescriptor.InvalidPosition, descriptor.Rename("name", "other"));
        Assert.Equal(0, descriptor.Rename("name", "renamed"));
        Assert.Equal(0, descriptor.Find("renamed")); Assert.Equal(AttributeDescriptor.InvalidPosition, descriptor.Find("name"));
        Assert.Equal(AttributeDescriptor.InvalidPosition, descriptor.Rename("missing", "unused"));
    }

    [Fact]
    public void DefaultsAreTypedRenamedAndPruned()
    {
        var descriptor = AttributeDescriptor.Create(Vec3fType).DuplicateAppend("density", FloatType).DuplicateAppend("id", IntType);
        Assert.Equal(0f, descriptor.GetDefaultValue<float>("density"));
        descriptor.SetDefaultValue("density", new MetadataValue(2.5f));
        descriptor.SetDefaultValue("id", new MetadataValue(17));
        Assert.True(descriptor.HasDefaultValue("density")); Assert.Equal(2.5f, descriptor.GetDefaultValue<float>("density"));
        Assert.Throws<InvalidCastException>(() => descriptor.SetDefaultValue("density", new MetadataValue(2)));
        Assert.Throws<KeyNotFoundException>(() => descriptor.SetDefaultValue("missing", new MetadataValue(2)));
        Assert.Equal(1, descriptor.Rename("density", "mass"));
        Assert.False(descriptor.HasDefaultValue("density")); Assert.Equal(2.5f, descriptor.GetDefaultValue<float>("mass"));
        AttributeDescriptor dropped = descriptor.DuplicateDrop([1]);
        Assert.False(dropped.Metadata.ContainsKey("default:mass")); Assert.True(dropped.Metadata.ContainsKey("default:id"));
        descriptor.RemoveDefaultValue("id"); Assert.False(descriptor.HasDefaultValue("id"));
    }

    [Fact]
    public void GroupOffsetsMapAcrossInterleavedGroupArrays()
    {
        var descriptor = AttributeDescriptor.Create([
            new("P", Vec3fType), new("__group0", AttributeTypes.Group), new("id", IntType),
            new("__group1", AttributeTypes.Group), new("__group2", AttributeTypes.Group)]);
        for (int offset = 0; offset < 24; offset++) descriptor.SetGroup("g" + offset, offset, checkValidOffset: true);
        Assert.Equal(24, descriptor.AvailableGroups); Assert.Equal(0, descriptor.UnusedGroups);
        for (int offset = 0; offset < 24; offset++)
        {
            GroupIndex index = descriptor.GroupIndex(offset);
            Assert.Equal((offset / 8) switch { 0 => 1, 1 => 3, _ => 4 }, index.AttributeIndex);
            Assert.Equal(offset % 8, index.BitOffset);
            Assert.Equal(offset, descriptor.GroupOffset(index));
            Assert.Equal(offset, descriptor.GroupOffset("g" + offset));
        }
        Assert.Throws<ArgumentException>(() => descriptor.SetGroup("duplicate", 0, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => descriptor.SetGroup("outside", 24, true));
        Assert.Throws<KeyNotFoundException>(() => descriptor.GroupIndex(24));
        Assert.Throws<KeyNotFoundException>(() => descriptor.GroupOffset(new GroupIndex(2, 0)));
    }

    [Fact]
    public void GroupCapacityCompactionAndCollisionsMatchDescriptorRules()
    {
        var descriptor = AttributeDescriptor.Create([
            new("P", Vec3fType), new("__group0", AttributeTypes.Group), new("__group1", AttributeTypes.Group)]);
        descriptor.SetGroup("a", 0); descriptor.SetGroup("b", 3); descriptor.SetGroup("c", 15);
        Assert.Equal(13, descriptor.UnusedGroups); Assert.True(descriptor.CanCompactGroups);
        Assert.Equal(1, descriptor.UnusedGroupOffset()); Assert.Equal(7, descriptor.UnusedGroupOffset(7));
        Assert.True(descriptor.TryGetRequiredGroupMove(out string name, out int source, out int target));
        Assert.Equal("b", name); Assert.Equal(3, source); Assert.Equal(1, target);
        Assert.Equal("a0", descriptor.UniqueGroupName("a")); Assert.Equal(0, descriptor.RenameGroup("a", "renamed"));
        var other = new AttributeDescriptor(descriptor); other.SetGroup("renamed", 4);
        Assert.True(descriptor.GroupIndexCollision(other));
        descriptor.DropGroup("c"); descriptor.ClearGroups(); Assert.Empty(descriptor.GroupMap);
    }

    [Fact]
    public void DuplicateDropRemovesGroupArraysAndRenumbersRemainingOffsets()
    {
        var descriptor = AttributeDescriptor.Create([
            new("test1", IntType), new("__group1", AttributeTypes.Group), new("test2", IntType),
            new("__group2", AttributeTypes.Group), new("__group3", AttributeTypes.Group), new("__group4", AttributeTypes.Group)]);
        descriptor.SetGroup("g1", 0); descriptor.SetGroup("g2", 8); descriptor.SetGroup("g3", 16); descriptor.SetGroup("g4", 28);
        AttributeDescriptor dropped = descriptor.DuplicateDrop([1, 2, 3]);
        Assert.Equal(["test1", "__group3", "__group4"], dropped.Entries().Select(static entry => entry.Name));
        Assert.False(dropped.HasGroup("g1")); Assert.False(dropped.HasGroup("g2"));
        Assert.Equal(0, dropped.GroupOffset("g3")); Assert.Equal(12, dropped.GroupOffset("g4"));
        Assert.Throws<ArgumentOutOfRangeException>(() => descriptor.DuplicateDrop([6]));
    }

    [Fact]
    public void DescriptorNativeFramingRoundTripsExactly()
    {
        var descriptor = AttributeDescriptor.Create([
            new("P", Vec3fType), new("density", FloatType), new("__group", AttributeTypes.Group)]);
        descriptor.SetGroup("visible", 3);
        descriptor.Metadata.InsertMeta("default:density", 1.25f);
        descriptor.Metadata.InsertMeta("purpose", "voxel game");
        using var first = new MemoryStream(); descriptor.Write(first);
        using var input = new MemoryStream(first.ToArray()); AttributeDescriptor decoded = AttributeDescriptor.Read(input);
        Assert.Equal(input.Length, input.Position); Assert.Equal(descriptor, decoded);
        using var second = new MemoryStream(); decoded.Write(second); Assert.Equal(first.ToArray(), second.ToArray());
        Assert.Equal((ulong)3, BinaryPrimitives.ReadUInt64LittleEndian(first.ToArray()));
    }

    [Fact]
    public void DescriptorReadRejectsBudgetsTruncationPositionsAndInvalidNames()
    {
        var descriptor = AttributeDescriptor.Create(Vec3fType);
        using var valid = new MemoryStream(); descriptor.Write(valid); byte[] bytes = valid.ToArray();
        Assert.Throws<InvalidDataException>(() => AttributeDescriptor.Read(new MemoryStream(bytes), maximumAttributes: 0));
        for (int length = 0; length < System.Math.Min(bytes.Length, 30); length++)
            Assert.ThrowsAny<Exception>(() => AttributeDescriptor.Read(new MemoryStream(bytes, 0, length)));
        byte[] badPosition = (byte[])bytes.Clone();
        int positionOffset = 8 + 4 + 5 + 4 + 4 + 4 + 1;
        BinaryPrimitives.WriteUInt64LittleEndian(badPosition.AsSpan(positionOffset), 1);
        Assert.Throws<InvalidDataException>(() => AttributeDescriptor.Read(new MemoryStream(badPosition)));
        byte[] badName = (byte[])bytes.Clone(); badName[8 + 4 + 5 + 4 + 4 + 4] = (byte)'!';
        Assert.Throws<InvalidDataException>(() => AttributeDescriptor.Read(new MemoryStream(badName)));
    }

    [Fact]
    public void DefaultRegistryCreatesRepresentativeBuiltInTypesAndCodecs()
    {
        var registry = AttributeArrayRegistry.CreateDefault();
        AttributeType[] types =
        [
            new("bool", "null"), new("half", "null"), new("float", "trnc"), new("vec3s", "fxpt8"),
            new("vec3d", "ufxpt16"), new("vec3s", "uvec"), AttributeTypes.Group, new("mat4d", "null"), new("quatd", "null")
        ];
        foreach (AttributeType type in types)
        {
            AttributeArray array = registry.Create(type, 7);
            Assert.Equal(type, array.Type); Assert.Equal(7, array.Size); Assert.True(array.IsUniform);
        }
    }

    [Fact]
    public void SetConstructionAppliesDefaultsAndResizeTransfersShapesAndSelectedFlags()
    {
        var descriptor = AttributeDescriptor.Create(Vec3fType).DuplicateAppend("id", IntType);
        descriptor.SetDefaultValue("P", new MetadataValue(new Vec3f(1, 2, 3)));
        descriptor.SetDefaultValue("id", new MetadataValue(7));
        using var source = new AttributeSet(descriptor, 5);
        Assert.Equal(new Vec3f(1, 2, 3), Assert.IsAssignableFrom<AttributeArray<Vec3f>>(source.GetConst("P")).Get(4));
        Assert.Equal(7, Assert.IsAssignableFrom<AttributeArray<int>>(source.GetConst("id")).Get(4));
        AttributeArray dynamic = source.AppendAttribute("dynamic", new("bool", "null"), 15, constantStride: false);
        dynamic.IsHidden = true; dynamic.IsTransient = true; dynamic.IsStreaming = true;
        using var resized = new AttributeSet(source, 10);
        AttributeArray resizedDynamic = Assert.IsAssignableFrom<AttributeArray<bool>>(resized.GetConst("dynamic"));
        Assert.Equal(10, resizedDynamic.Size); Assert.Equal(15, resizedDynamic.DataSize);
        Assert.True(resizedDynamic.IsHidden); Assert.True(resizedDynamic.IsTransient); Assert.False(resizedDynamic.IsStreaming);
        Assert.Throws<ArgumentOutOfRangeException>(() => new AttributeSet(source, 16));
    }

    [Fact]
    public void ShallowCopyUsesDeterministicCopyOnWrite()
    {
        var descriptor = AttributeDescriptor.Create(Vec3fType).DuplicateAppend("id", IntType);
        using var source = new AttributeSet(descriptor, 4);
        Assert.IsAssignableFrom<AttributeArray<int>>(source.Get("id")).Set(2, 9);
        using var copy = new AttributeSet(source);
        Assert.True(source.IsShared(0)); Assert.True(copy.IsShared(1));
        Assert.Equal(9, Assert.IsAssignableFrom<AttributeArray<int>>(copy.GetConst("id")).Get(2));
        Assert.IsAssignableFrom<AttributeArray<int>>(copy.Get("id")).Set(2, 12);
        Assert.False(copy.IsShared(1)); Assert.False(source.IsShared(1));
        Assert.Equal(9, Assert.IsAssignableFrom<AttributeArray<int>>(source.GetConst("id")).Get(2));
        Assert.Equal(12, Assert.IsAssignableFrom<AttributeArray<int>>(copy.GetConst("id")).Get(2));
    }

    [Fact]
    public void ReplacementRemovalAndUnsafeRemovalPreserveNativeOutcomes()
    {
        using var set = new AttributeSet(AttributeDescriptor.Create(Vec3fType).DuplicateAppend("id", IntType), 5);
        Assert.Equal(AttributeDescriptor.InvalidPosition, set.Replace(1, new TypedAttributeArray<float>(5)));
        var replacement = new TypedAttributeArray<int>(10, uniformValue: 4);
        Assert.Equal(1, set.Replace("id", replacement)); Assert.Same(replacement, set.GetConst(1));
        AttributeArray? removed = set.RemoveAttribute("id");
        Assert.Same(replacement, removed); Assert.Single(set.Descriptor.Entries()); Assert.Equal(1, set.Size);
        set.AppendAttribute("a", IntType); set.AppendAttribute("b", IntType);
        Assert.NotNull(set.RemoveAttributeUnsafe(1)); Assert.Equal(3, set.Size); Assert.Null(set.GetConst(1));
        Assert.Throws<InvalidOperationException>(() => set.Get(1));
        Assert.Null(set.RemoveAttribute(30));
    }

    [Fact]
    public void AppendDropRenameReorderAndResetValidateDescriptors()
    {
        using var set = new AttributeSet(AttributeDescriptor.Create(Vec3fType), 3);
        AttributeDescriptor before = set.Descriptor;
        AttributeDescriptor withId = before.DuplicateAppend("id", IntType);
        set.AppendAttribute(before, withId, 1, defaultValue: new MetadataValue(3));
        Assert.Equal(3, Assert.IsAssignableFrom<AttributeArray<int>>(set.GetConst("id")).Get(0));
        var wrong = AttributeDescriptor.Create(FloatType);
        Assert.Throws<InvalidOperationException>(() => set.AppendAttribute(wrong, withId, 1));
        AttributeDescriptor renamed = new(set.Descriptor); Assert.Equal(1, renamed.Rename("id", "identifier"));
        set.RenameAttributes(set.Descriptor, renamed); Assert.NotNull(set.GetConst("identifier"));
        AttributeDescriptor reordered = AttributeDescriptor.Create([
            new("identifier", IntType), new("P", Vec3fType)]);
        set.ReorderAttributes(reordered); Assert.IsAssignableFrom<AttributeArray<int>>(set.GetConst(0));
        Assert.Throws<InvalidOperationException>(() => set.ReorderAttributes(AttributeDescriptor.Create(FloatType)));
        Assert.Throws<InvalidOperationException>(() => set.ResetDescriptor(wrong));
        set.ResetDescriptor(wrong, allowMismatchingDescriptors: true); Assert.Same(wrong, set.Descriptor);
    }

    [Fact]
    public void DroppingAttributesHandlesUnsortedDuplicatesAndDescriptorSharing()
    {
        var descriptor = AttributeDescriptor.Create([
            new("P", Vec3fType), new("a", IntType), new("b", FloatType), new("c", IntType), new("d", FloatType)]);
        using var set = new AttributeSet(descriptor, 2);
        set.DropAttributes([3, 1, 3]);
        Assert.Equal(["P", "b", "d"], set.Descriptor.Entries().Select(static entry => entry.Name));
        Assert.Equal(3, set.Size);
        AttributeDescriptor expected = set.Descriptor, replacement = expected.DuplicateDrop([1]);
        set.DropAttributes([1], expected, replacement);
        Assert.Equal(["P", "d"], set.Descriptor.Entries().Select(static entry => entry.Name));
        Assert.Throws<ArgumentOutOfRangeException>(() => set.DropAttributes([2]));
    }

    [Fact]
    public void GroupArrayIndicesUseRuntimeArrayTypes()
    {
        using var set = new AttributeSet(AttributeDescriptor.Create([
            new("P", Vec3fType), new("__group0", AttributeTypes.Group), new("id", IntType), new("__group1", AttributeTypes.Group)]), 3);
        Assert.Equal([1, 3], set.GroupAttributeIndices());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SetNativeStreamsRoundTripRawCompressedAndTransientModes(bool compress, bool outputTransient)
    {
        var descriptor = AttributeDescriptor.Create(Vec3fType);
        descriptor.SetDefaultValue("P", new MetadataValue(new Vec3f(.25f, -.5f, .75f)));
        using var source = new AttributeSet(descriptor, 64);
        var positions = Assert.IsAssignableFrom<AttributeArray<Vec3f>>(source.Get("P")); positions.Expand();
        for (int i = 0; i < positions.DataSize; i++) positions.Set(i, new Vec3f(i * .01f, -i, i + .5f));
        var ids = Assert.IsAssignableFrom<AttributeArray<int>>(source.AppendAttribute("id", IntType, defaultValue: new MetadataValue(11)));
        ids.Expand(); for (int i = 0; i < ids.DataSize; i++) ids.Set(i, i * 17);
        source.AppendAttribute("temporary", FloatType).IsTransient = true;
        using var bytes = new MemoryStream(); source.Write(bytes, outputTransient, compress);
        bytes.Position = 0; using AttributeSet decoded = AttributeSet.Read(bytes);
        Assert.Equal(bytes.Length, bytes.Position);
        Assert.Equal(outputTransient ? 3 : 2, decoded.Size);
        Assert.Equal(ids.Get(37), Assert.IsAssignableFrom<AttributeArray<int>>(decoded.GetConst("id")).Get(37));
        Assert.Equal(positions.Get(51), Assert.IsAssignableFrom<AttributeArray<Vec3f>>(decoded.GetConst("P")).Get(51));
        Assert.Equal(outputTransient, decoded.GetConst("temporary") is not null);
        if (outputTransient) Assert.Equal(source, decoded);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SplitDescriptorMetadataAndAttributeIOEqualsCombinedOutput(bool compress)
    {
        using var source = new AttributeSet(AttributeDescriptor.Create(Vec3fType), 80);
        var p = Assert.IsAssignableFrom<AttributeArray<Vec3f>>(source.Get("P")); p.Expand();
        for (int i = 0; i < p.DataSize; i++) p.Set(i, new Vec3f(i, i + 1, i + 2));
        source.AppendAttribute("id", IntType, defaultValue: new MetadataValue(5));
        using var combined = new MemoryStream(); source.Write(combined, compress: compress);
        using var split = new MemoryStream();
        source.WriteDescriptor(split); source.WriteMetadata(split, compress: compress); source.WriteAttributes(split, compress: compress);
        Assert.Equal(combined.ToArray(), split.ToArray());
        split.Position = 0;
        using var decoded = new AttributeSet();
        decoded.ReadDescriptor(split); decoded.ReadMetadata(split); decoded.ReadAttributes(split);
        Assert.Equal(source, decoded); Assert.Equal(split.Length, split.Position);
    }

    [Fact]
    public void RuntimeTypedBufferReaderPreservesCodecAndStorage()
    {
        var source = new TypedAttributeArray<Vec3f, FixedPoint16Codec<Vec3f, PositionRange>>(20, uniformValue: new(.1f, -.2f, .3f));
        source.Expand(); source.Set(7, new(.49f, -.49f, .25f));
        using var stream = new MemoryStream(); AttributeStreams.Write(stream, source, compress: true);
        stream.Position = 0; AttributeStreamMetadata metadata = AttributeStreams.ReadMetadata(stream);
        AttributeArray decoded = AttributeStreams.ReadBuffers(stream, source.Type, AttributeArrayRegistry.CreateDefault(), metadata);
        Assert.Equal(source.Type, decoded.Type); Assert.True(source.StorageEquals(decoded)); Assert.Equal(stream.Length, stream.Position);
    }

    [Fact]
    public void SetReadIsTransactionalAndHonorsAllocationBudgets()
    {
        using var source = new AttributeSet(AttributeDescriptor.Create(Vec3fType), 32);
        Assert.IsAssignableFrom<AttributeArray<Vec3f>>(source.Get("P")).Expand();
        using var stream = new MemoryStream(); source.Write(stream);
        byte[] bytes = stream.ToArray();
        var options = new AttributeSetReadOptions { AttributeOptions = new() { MaximumStorageBytes = 10 } };
        Assert.Throws<InvalidDataException>(() => AttributeSet.Read(new MemoryStream(bytes), options: options));
        using var target = new AttributeSet(AttributeDescriptor.Create(FloatType), 2);
        Assert.ThrowsAny<Exception>(() => target.ReadFrom(new MemoryStream(bytes, 0, bytes.Length - 1)));
        Assert.Equal(FloatType, target.Descriptor.Type(0)); Assert.Equal(1, target.Size);
    }

    [Fact]
    public void ConcurrentShallowCopiesMutateIndependentArrays()
    {
        using var source = new AttributeSet(AttributeDescriptor.Create(IntType), 128);
        var failures = new System.Collections.Concurrent.ConcurrentQueue<Exception>();
        Parallel.For(0, 128, i =>
        {
            try
            {
                using var copy = new AttributeSet(source);
                var values = Assert.IsAssignableFrom<AttributeArray<int>>(copy.Get(0)); values.Set(i, i + 1);
                Assert.Equal(i + 1, values.Get(i));
            }
            catch (Exception exception) { failures.Enqueue(exception); }
        });
        Assert.Empty(failures);
        var unchanged = Assert.IsAssignableFrom<AttributeArray<int>>(source.GetConst(0));
        for (int i = 0; i < unchanged.DataSize; i++) Assert.Equal(0, unchanged.Get(i));
    }

    [Fact]
    public void DisposedSetsReleaseTrackedOwnersAndRejectFurtherUse()
    {
        using var source = new AttributeSet(AttributeDescriptor.Create(IntType), 3);
        var copy = new AttributeSet(source); Assert.True(source.IsShared(0));
        copy.Dispose(); Assert.False(source.IsShared(0));
        Assert.Throws<ObjectDisposedException>(() => _ = copy.Size);
        copy.Dispose();
    }

    [Fact]
    public void FailedDescriptorSharingMutationsLeaveSetAndOwnershipUnchanged()
    {
        var descriptor = AttributeDescriptor.Create(IntType).DuplicateAppend("b", FloatType);
        using var source = new AttributeSet(descriptor, 4);
        using var shared = new AttributeSet(source);
        AttributeArray first = source.GetConst(0)!;
        AttributeDescriptor wrongSized = new();
        Assert.Throws<ArgumentException>(() => source.DropAttributes([0], descriptor, wrongSized));
        Assert.Equal(2, source.Size); Assert.Same(first, source.GetConst(0)); Assert.True(source.IsShared(0));
        AttributeDescriptor tooLarge = descriptor.DuplicateAppend("c", IntType).DuplicateAppend("d", IntType);
        Assert.Throws<ArgumentException>(() => source.AppendAttribute(descriptor, tooLarge, 2));
        Assert.Equal(2, source.Size); Assert.Same(descriptor, source.Descriptor);
    }

    [Fact]
    public void NativeGroupIndexAcceptsFullByteBitOffset()
    {
        var descriptor = AttributeDescriptor.Create([new AttributeNameAndType("__group", AttributeTypes.Group)]);
        Assert.Equal(255, descriptor.GroupOffset(new GroupIndex(0, byte.MaxValue)));
    }
    [Fact]
    public void GroupHandlesResolveDescriptorBitsAndUseCopyOnWrite()
    {
        var descriptor = AttributeDescriptor.Create([
            new AttributeNameAndType("P", Vec3fType), new AttributeNameAndType("__group", AttributeTypes.Group)]);
        descriptor.SetGroup("selected", 5, checkValidOffset: true);
        using var source = new AttributeSet(descriptor, 6);
        using var copy = new AttributeSet(source);
        copy.GetGroupWriteHandle("selected").Set(3, true);
        Assert.True(copy.GetGroupHandle("selected").Get(3));
        Assert.False(source.GetGroupHandle("selected").Get(3));
        Assert.False(copy.IsShared(1)); Assert.False(source.IsShared(1));
        Assert.Throws<KeyNotFoundException>(() => source.GetGroupHandle("missing"));
    }

    [Fact]
    public void DefaultRegistriesAreIndependentValidatedFactoryCopies()
    {
        var first = AttributeArrayRegistry.CreateDefault();
        var second = AttributeArrayRegistry.CreateDefault();
        Assert.True(first.Unregister(IntType));
        Assert.False(first.IsRegistered(IntType)); Assert.True(second.IsRegistered(IntType));
        Assert.IsAssignableFrom<AttributeArray<int>>(second.Create(IntType, 3));
    }
}
