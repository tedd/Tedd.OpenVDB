// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Points;
using Tedd.OpenVDB.Tools;
namespace Tedd.OpenVDB.Tests;

public sealed class PointDataLeafNodeTests
{
    private static readonly AttributeDescriptor PositionDescriptor =
        AttributeDescriptor.Create(new AttributeType("vec3s", "null"));

    [Fact]
    public void ConstructionAlignsOriginAndStartsWithZeroOffsetsAndNoAttributes()
    {
        var leaf = new PointDataLeafNode(new Coord(20, 30, 40));
        Assert.Equal(new Coord(16, 24, 40), leaf.Origin);
        Assert.Equal(512, leaf.Offsets.Length);
        Assert.All(leaf.Offsets.ToArray(), value => Assert.Equal(0u, value));
        Assert.True(leaf.IsEmpty);
        Assert.Equal(0, leaf.AttributeCount);
        Assert.Equal(0, leaf.PointCount);
        Assert.Equal(0, leaf.OnPointCount);
        Assert.Equal(0, leaf.OffPointCount);
    }

    [Fact]
    public void PrefixRangesAndPointModesRemainIndependentOfVoxelActivity()
    {
        var leaf = new PointDataLeafNode(Coord.Zero);
        leaf.InitializeAttributes(PositionDescriptor, 3);
        uint[] offsets = new uint[512];
        Array.Fill(offsets, 3u);
        offsets[0] = 2;
        offsets[1] = 2;
        leaf.SetOffsets(offsets);
        leaf.ValidateOffsets();

        Assert.Equal(new PointDataRange(0, 2), leaf.GetPointRange(0));
        Assert.Equal(new PointDataRange(2, 2), leaf.GetPointRange(new Coord(0, 0, 1)));
        Assert.Equal(new PointDataRange(2, 3), leaf.GetPointRange(2));
        Assert.Equal(0, leaf.GetVoxelOffsetForPoint(0));
        Assert.Equal(2, leaf.GetVoxelOffsetForPoint(2));
        Assert.Equal(new[] { 0, 1, 2 }, leaf.EnumerateIndices().ToArray());
        Assert.Equal(new[] { 0, 1, 2 }, leaf.EnumerateIndices(PointDataIndexMode.Active).ToArray());
        Assert.Empty(leaf.EnumerateIndices(PointDataIndexMode.Inactive));

        leaf.SetActiveState(0, false);
        leaf.SetActiveState(1, true); // an active empty voxel is legal
        Assert.Equal(new[] { 2 }, leaf.EnumerateIndices(PointDataIndexMode.Active).ToArray());
        Assert.Equal(new[] { 0, 1 }, leaf.EnumerateIndices(PointDataIndexMode.Inactive).ToArray());
        Assert.Equal(1, leaf.OnPointCount);
        Assert.Equal(2, leaf.OffPointCount);
        Assert.Equal(2, leaf.OnVoxelCount);
        Assert.Throws<ArgumentOutOfRangeException>(() => leaf.GetVoxelOffsetForPoint(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => leaf.GetPointRange(512));
    }

    [Fact]
    public void BulkOffsetsPreserveMaskOnRequestAndRejectBeforePartialMutation()
    {
        var leaf = new PointDataLeafNode(Coord.Zero);
        for (int offset = 0; offset < 512; ++offset) leaf.SetOffsetOn(offset, 7);
        leaf.SetOffsets(new uint[512], updateValueMask: false);
        Assert.Equal(512, leaf.OnVoxelCount);
        Assert.All(leaf.Offsets.ToArray(), value => Assert.Equal(0u, value));
        leaf.UpdateValueMask();
        Assert.True(leaf.IsEmpty);

        uint[] baseline = Enumerable.Repeat(11u, 512).ToArray();
        leaf.SetOffsets(baseline, updateValueMask: false);
        uint[] rejected = Enumerable.Repeat(23u, 512).ToArray();
        rejected[^1] = uint.MaxValue;
        Assert.Throws<ArgumentOutOfRangeException>(() => leaf.SetOffsets(rejected));
        Assert.Equal(baseline, leaf.Offsets.ToArray());
        Assert.Throws<ArgumentException>(() => leaf.SetOffsets(new uint[511]));

        uint[] nonmonotone = new uint[512];
        nonmonotone[0] = 1;
        leaf.SetOffsets(nonmonotone);
        Assert.Throws<InvalidOperationException>(leaf.ValidateOffsets);
    }

    [Fact]
    public void AttributeInitializationClearAndNonzeroFillFollowNativeLeafRestrictions()
    {
        var leaf = new PointDataLeafNode(Coord.Zero);
        Assert.Throws<ArgumentException>(() => leaf.InitializeAttributes(new AttributeDescriptor(), 1));
        Assert.Throws<ArgumentException>(() => leaf.InitializeAttributes(
            AttributeDescriptor.Create(new AttributeType("float", "null")), 1));
        Assert.Throws<ArgumentException>(() => leaf.InitializeAttributes(
            PositionDescriptor.DuplicateAppend("id", new AttributeType("int32", "null")), 1));

        leaf.InitializeAttributes(PositionDescriptor, 5);
        uint[] offsets = new uint[512]; offsets[^1] = 5;
        leaf.SetOffsets(offsets);
        Assert.Equal(5, leaf.PointCount);
        Assert.True(leaf.IsValueOn(511));
        leaf.ClearAttributes(updateValueMask: false);
        Assert.Equal(1, leaf.GetConstAttributeArray("P").Size);
        Assert.Equal(0, leaf.PointCount);
        Assert.True(leaf.IsValueOn(511));
        Assert.Throws<InvalidOperationException>(leaf.ValidateOffsets);
        leaf.ClearAttributes();
        Assert.True(leaf.IsEmpty);
        Assert.Throws<InvalidOperationException>(() => leaf.Fill(1, true));
        Assert.Throws<InvalidOperationException>(() => leaf.Fill(leaf.Bounds, 1, true));
        leaf.Fill(0, true);
        Assert.Equal(512, leaf.OnVoxelCount);
        leaf.Fill(new CoordBBox(Coord.Zero, Coord.Zero), 0, false);
        Assert.False(leaf.IsValueOn(Coord.Zero));
    }

    [Fact]
    public void DeepCopySharesAttributeStorageUntilWritableAccessMakesItUnique()
    {
        var leaf = new PointDataLeafNode(Coord.Zero);
        leaf.InitializeAttributes(PositionDescriptor, 2);
        uint[] offsets = Enumerable.Repeat(2u, 512).ToArray();
        leaf.SetOffsets(offsets);
        var original = Assert.IsAssignableFrom<AttributeArray<Vec3f>>(leaf.GetAttributeArray("P"));
        original.Set(0, new Vec3f(.1f, .2f, .3f));
        original.Set(1, new Vec3f(-.1f, -.2f, -.3f));
        var copy = leaf.DeepCopy();
        Assert.Same(leaf.GetConstAttributeArray("P"), copy.GetConstAttributeArray("P"));
        bool originalState = leaf.IsValueOn(0);
        copy.SetActiveState(0, !originalState);
        Assert.Equal(originalState, leaf.IsValueOn(0));
        copy.SetActiveState(0, originalState);

        var writableCopy = Assert.IsAssignableFrom<AttributeArray<Vec3f>>(copy.GetAttributeArray("P"));
        Assert.NotSame(leaf.GetConstAttributeArray("P"), writableCopy);
        writableCopy.Set(0, new Vec3f(9, 8, 7));
        Assert.Equal(new Vec3f(.1f, .2f, .3f),
            Assert.IsAssignableFrom<AttributeArray<Vec3f>>(leaf.GetConstAttributeArray("P")).Get(0));
        Assert.Equal(new Vec3f(9, 8, 7), writableCopy.Get(0));
        Assert.True(leaf.NativeEquals(leaf));
        Assert.False(leaf.NativeEquals(copy));
    }

    [Fact]
    public void AttributeSchemaWrappersAppendRenameReorderAndDropWithoutChangingPointRanges()
    {
        var leaf = new PointDataLeafNode(Coord.Zero);
        leaf.InitializeAttributes(PositionDescriptor, 3);
        uint[] offsets = Enumerable.Repeat(3u, 512).ToArray();
        leaf.SetOffsets(offsets);
        AttributeDescriptor positionOnly = leaf.Descriptor;
        AttributeDescriptor withId = positionOnly.DuplicateAppend("id", new AttributeType("int32", "null"));
        AttributeArray id = leaf.AppendAttribute(positionOnly, withId, withId.Find("id"), defaultValue: new MetadataValue(7));
        Assert.Equal(2, leaf.AttributeCount);
        Assert.Equal(3, id.Size);
        Assert.Equal(7, Assert.IsAssignableFrom<AttributeArray<int>>(id).Get(0));

        var renamed = new AttributeDescriptor(withId);
        Assert.Equal(1, renamed.Rename("id", "identifier"));
        leaf.RenameAttributes(withId, renamed);
        Assert.True(leaf.HasAttribute("identifier"));
        Assert.False(leaf.HasAttribute("id"));
        AttributeDescriptor reordered = AttributeDescriptor.Create(
            [new AttributeNameAndType("identifier", new AttributeType("int32", "null")),
             new AttributeNameAndType("P", new AttributeType("vec3s", "null"))]);
        leaf.ReorderAttributes(reordered);
        Assert.Equal("identifier", leaf.Descriptor.Entries()[0].Name);
        Assert.Equal("P", leaf.Descriptor.Entries()[1].Name);
        AttributeDescriptor positionAgain = reordered.DuplicateDrop([0]);
        leaf.DropAttributes([0], reordered, positionAgain);
        Assert.Equal(1, leaf.AttributeCount);
        Assert.True(leaf.HasAttribute("P"));
        Assert.Equal(3, leaf.PointCount);
        leaf.ValidateOffsets();
    }
    [Fact]
    public void StealAndReplaceTransferAttributeOwnershipAndValidateDescriptors()
    {
        var leaf = new PointDataLeafNode(Coord.Zero);
        leaf.InitializeAttributes(PositionDescriptor, 2);
        AttributeSet stolen = leaf.StealAttributeSet();
        Assert.Equal(0, leaf.AttributeCount);
        Assert.Equal(1, stolen.Size);
        using var wrong = new AttributeSet(PositionDescriptor, 2);
        Assert.Throws<InvalidOperationException>(() => leaf.ReplaceAttributeSet(wrong));
        leaf.ReplaceAttributeSet(stolen, allowMismatchingDescriptors: true);
        Assert.Equal(1, leaf.AttributeCount);
        Assert.Equal("vec3s", leaf.Descriptor.ValueType(0));
    }
}

public sealed class PointDataGridTests
{
    [Fact]
    public void CreateLocalizesPositionsAndPreservesNativeTopologyAndSourceOrder()
    {
        Transform transform = Transform.CreateLinearTransform(.75);
        transform.PostTranslate(new Vec3d(.25, -.375, .125));
        transform.PostRotate(.2, Axis.Z);
        Vec3d[] index = [new(-8.5, 1.25, 7.5), new(.5), Vec3d.Zero, new(8.5, -2.25, .125), Vec3d.Zero];
        Vec3d[] world = index.Select(transform.IndexToWorld).ToArray();
        var source = new PointArray(world);
        var grid = PointDataGrid.Create(source, transform);

        Assert.Equal(typeof(PointDataIndex32), grid.ValueType);
        Assert.Equal("Tree_ptdataidx32_5_4_3", grid.NativeTreeType);
        Assert.Equal((ulong)world.Length, grid.PointCount);
        Assert.Equal((ulong)world.Length, grid.OnPointCount);
        Assert.Equal(0ul, grid.OffPointCount);
        Assert.Equal(grid.Tree.LeafCount, grid.Tree.NodeCount()[0]);
        Assert.Equal(world.Length, grid.EnumeratePositions().Count());
        Vec3d[] reconstructed = grid.EnumeratePositions().OrderBy(v => v.X).ThenBy(v => v.Y).ThenBy(v => v.Z).ToArray();
        Vec3d[] expected = world.OrderBy(v => v.X).ThenBy(v => v.Y).ThenBy(v => v.Z).ToArray();
        for (int i = 0; i < expected.Length; ++i) AssertVectorNear(expected[i], reconstructed[i], 2e-7);
        Assert.All(grid.Tree.Leaves, leaf =>
        {
            Assert.Equal("vec3s", leaf.Descriptor.Type(0).ValueType);
            Assert.Equal("null", leaf.Descriptor.Type(0).Codec);
            leaf.ValidateOffsets();
        });
    }

    [Fact]
    public void NonfiniteSourcePositionsAreOmittedLikeNativePointConversion()
    {
        var source = new PointArray([Vec3d.Zero, new Vec3d(double.NaN), new Vec3d(double.PositiveInfinity), new Vec3d(1)]);
        PointDataGrid grid = PointDataGrid.Create(source);
        Assert.Equal(2ul, grid.PointCount);
        Assert.Equal(2, grid.EnumeratePositions().Count());
        Assert.All(grid.EnumeratePositions(), position => Assert.True(position.IsFinite));
    }

    [Fact]
    public void ExistingPointIndexMayReferenceASubsetOfALargerSource()
    {
        var subset = new PointArray([Vec3d.Zero, new Vec3d(8)]);
        PointIndexGrid index = PointIndexGrid.Create(subset);
        var larger = new PointArray([Vec3d.Zero, new Vec3d(8), new Vec3d(100)]);
        PointDataGrid grid = PointDataGrid.Create<NullCodec<Vec3f>>(index, larger, Transform.CreateLinearTransform(1));
        Assert.Equal(2ul, grid.PointCount);
        Assert.Equal(new[] { Vec3d.Zero, new Vec3d(8) }, grid.EnumeratePositions().OrderBy(v => v.X).ThenBy(v => v.Y).ThenBy(v => v.Z).ToArray());
    }

    [Fact]
    public void FixedPointPositionCodecQuantizesLocalsAndReconstructsWithinItsResolution()
    {
        Vec3d[] values = [new(.123456, -.234567, .345678), new(7.49999, -8.49999, .00001)];
        var grid = PointDataGrid.Create<FixedPoint16Codec<Vec3f, PositionRange>>(
            new PointArray(values), Transform.CreateLinearTransform(1));
        Assert.All(grid.Tree.Leaves, leaf => Assert.IsType<TypedAttributeArray<Vec3f, FixedPoint16Codec<Vec3f, PositionRange>>>(leaf.GetConstAttributeArray("P")));
        Vec3d[] decoded = grid.EnumeratePositions().OrderBy(v => v.X).ThenBy(v => v.Y).ThenBy(v => v.Z).ToArray();
        Vec3d[] expected = values.OrderBy(v => v.X).ThenBy(v => v.Y).ThenBy(v => v.Z).ToArray();
        for (int i = 0; i < expected.Length; ++i) AssertVectorNear(expected[i], decoded[i], 2.0 / ushort.MaxValue);
    }

    [Fact]
    public void DeepCopySharesAttributesButOwnsTopologyTransformAndMetadata()
    {
        var grid = PointDataGrid.Create(new PointArray([Vec3d.Zero, new Vec3d(8), new Vec3d(4096)]));
        grid.Name = "points"; grid.SetMetadata("count", 3);
        var copy = grid.DeepCopy();
        Assert.Equal("points", copy.Name);
        Assert.Equal(3, copy.GetMetadata<int>("count"));
        Assert.NotSame(grid.Tree.Leaves[0], copy.Tree.Leaves[0]);
        Assert.Same(grid.Tree.Leaves[0].GetConstAttributeArray("P"), copy.Tree.Leaves[0].GetConstAttributeArray("P"));
        copy.Transform.PostTranslate(Vec3d.One);
        copy.RemoveMetadata("count");
        Assert.Equal(Vec3d.Zero, grid.Transform.IndexToWorld(Vec3d.Zero));
        Assert.Equal(3, grid.GetMetadata<int>("count"));

        Grid<uint> diagnostic = grid.CopyOffsetGrid();
        diagnostic.Tree.Clear();
        diagnostic.Transform.PostScale(2);
        Assert.Equal(3ul, grid.PointCount);
        Assert.Equal(1, grid.Transform.VoxelSize.X);
    }

    [Fact]
    public void TreeDescriptorsCanBeMadeUniquelySharedAcrossLeaves()
    {
        var grid = PointDataGrid.Create(new PointArray([Vec3d.Zero, new Vec3d(8), new Vec3d(4096)]));
        Assert.True(grid.Tree.LeafCount > 1);
        AttributeDescriptor before = grid.Tree.Leaves[0].Descriptor;
        Assert.All(grid.Tree.Leaves, leaf => Assert.Same(before, leaf.Descriptor));
        AttributeDescriptor replacement = Assert.IsType<AttributeDescriptor>(grid.Tree.MakeDescriptorUnique());
        Assert.NotSame(before, replacement);
        Assert.All(grid.Tree.Leaves, leaf => Assert.Same(replacement, leaf.Descriptor));
        Assert.Null(PointDataGrid.Create(new PointArray([])).Tree.MakeDescriptorUnique());
    }

    [Fact]
    public void TreeQueriesActivityModesStreamingAndPrefetchUseCoupledLeafState()
    {
        var grid = PointDataGrid.Create(new PointArray([Vec3d.Zero, Vec3d.Zero, new Vec3d(8)]));
        PointDataLeafNode leaf = grid.Tree.ProbeLeaf(Coord.Zero)!;
        Assert.Equal(2u, grid.Tree.GetValue(Coord.Zero));
        Assert.True(grid.Tree.IsValueOn(Coord.Zero));
        Assert.Equal(3, grid.Tree.GetValueDepth(Coord.Zero));
        leaf.SetActiveState(Coord.Zero, false);
        Assert.Equal(2ul, grid.OffPointCount);
        Assert.Equal(grid.PointCount - 2, grid.OnPointCount);
        Assert.Equal(2, grid.EnumeratePositions(PointDataIndexMode.Inactive).Count());
        grid.Tree.SetStreamingMode();
        Assert.All(grid.Tree.Leaves, item => Assert.True(item.GetConstAttributeArray("P").IsStreaming));
        grid.Tree.Prefetch();
        grid.Tree.Prefetch(position: false, otherAttributes: false);
        grid.Tree.CompactAttributes();
        grid.Tree.SetStreamingMode(false);
        Assert.All(grid.Tree.Leaves, item => Assert.False(item.GetConstAttributeArray("P").IsStreaming));
    }

    [Fact]
    public void PointDataIndex32IsADistinctSignedMarkerWithInvariantFormatting()
    {
        PointDataIndex32 value = (PointDataIndex32)(-17);
        Assert.Equal(-17, (int)value);
        Assert.Equal("-17", value.ToString());
        Assert.NotEqual(typeof(int), typeof(PointDataIndex32));
    }
    [Fact]
    public void CancellationAndSourceFailuresPublishNoPartialGrid()
    {
        Assert.Throws<OperationCanceledException>(() => PointDataGrid.Create(new PointArray([Vec3d.Zero]), 1, cancellationToken: new(true)));
        Assert.Throws<ArithmeticException>(() => PointDataGrid.Create(new ThrowingPointSource()));
        var negative = new NegativePointSource();
        Assert.Throws<ArgumentOutOfRangeException>(() => PointDataGrid.Create(negative));
    }

    [Fact]
    public async Task FrozenPointDataGridSupportsConcurrentReadOnlyTraversal()
    {
        Vec3d[] values = Enumerable.Range(0, 200).Select(i => new Vec3d(i * 7 - 300, i % 17, -(i % 23))).ToArray();
        var grid = PointDataGrid.Create(new PointArray(values), .75);
        Vec3d[] baseline = grid.EnumeratePositions().ToArray();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            for (int iteration = 0; iteration < 20; ++iteration)
            {
                Assert.Equal(baseline, grid.EnumeratePositions().ToArray());
                Assert.Equal((ulong)values.Length, grid.Tree.PointCount);
            }
        })));
    }

    private static void AssertVectorNear(Vec3d expected, Vec3d actual, double tolerance)
    {
        Assert.True((expected - actual).Length <= tolerance,
            $"expected {expected}, actual {actual}, distance {(expected - actual).Length}");
    }
    private sealed class ThrowingPointSource : IPointSource
    {
        public int Count => 2;
        public Vec3d GetPosition(int index) => index == 0 ? Vec3d.Zero : throw new ArithmeticException();
    }
    private sealed class NegativePointSource : IPointSource
    {
        public int Count => -1;
        public Vec3d GetPosition(int index) => throw new InvalidOperationException();
    }
}
