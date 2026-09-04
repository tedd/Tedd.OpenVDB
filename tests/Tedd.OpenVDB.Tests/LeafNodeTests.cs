// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Tests exercise source-derived OpenVDB leaf addressing, state, and topology behavior.

using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;

namespace Tedd.OpenVDB.Tests;

public sealed class LeafNodeTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(7, 0)]
    [InlineData(8, 8)]
    [InlineData(9, 8)]
    [InlineData(-1, -8)]
    [InlineData(-7, -8)]
    [InlineData(-8, -8)]
    [InlineData(-9, -16)]
    [InlineData(int.MinValue, int.MinValue)]
    [InlineData(int.MaxValue, int.MaxValue - 7)]
    public void OriginAlignmentUsesFloorDivisionAcrossSignedCoordinateDomain(int coordinate, int expected)
    {
        var leaf = new LeafNode<int>(new Coord(coordinate, coordinate, coordinate), 17);
        Assert.Equal(new Coord(expected, expected, expected), leaf.Origin);
        Assert.Equal(leaf.Origin, leaf.Bounds.Min);
        Assert.Equal(new Coord(expected + 7, expected + 7, expected + 7), leaf.Bounds.Max);
        Assert.Equal(leaf.Bounds, leaf.BoundingBox);
        Assert.Equal(512, leaf.Values.Length);
        Assert.True(leaf.IsEmpty);
        Assert.False(leaf.IsDense);
        Assert.Equal(0, leaf.OnVoxelCount);
        Assert.Equal(512, leaf.OffVoxelCount);
        for (var index = 0; index < 512; ++index) Assert.Equal(17, leaf.GetValue(index));
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(-1, -9, 17)]
    [InlineData(93, -82, 71)]
    [InlineData(int.MinValue, int.MaxValue, -1)]
    [InlineData(int.MaxValue, int.MinValue, 0)]
    public void EveryOffsetRoundTripsInZThenYThenXOrder(int x, int y, int z)
    {
        var leaf = new LeafNode<int>(new Coord(x, y, z));
        for (var offset = 0; offset < LeafNode<int>.Size; ++offset)
        {
            var local = LeafNode<int>.OffsetToLocalCoord(offset);
            Assert.Equal(new Coord(offset / 64, offset / 8 % 8, offset % 8), local);
            var global = leaf.OffsetToGlobalCoord(offset);
            Assert.Equal(leaf.Origin.X + local.X, global.X);
            Assert.Equal(leaf.Origin.Y + local.Y, global.Y);
            Assert.Equal(leaf.Origin.Z + local.Z, global.Z);
            Assert.Equal(offset, LeafNode<int>.CoordToOffset(global));
            Assert.True(leaf.Bounds.Contains(global));
            leaf.SetValueOn(global, offset * 11);
            Assert.Equal(offset * 11, leaf.GetValue(offset));
        }
        Assert.True(leaf.IsDense);
        Assert.Equal(512, leaf.OnVoxelCount);
    }

    [Fact]
    public void CoordinateAccessMasksHighBitsAsUpstreamLeafAccessDoes()
    {
        var leaf = new LeafNode<int>(new Coord(16, 24, 32));
        leaf.SetValueOn(new Coord(17, 26, 35), 912);
        Assert.Equal(912, leaf.GetValue(new Coord(1, 2, 3)));
        Assert.Equal(912, leaf.GetValue(new Coord(-7, -6, -5)));
        Assert.Equal(912, leaf.GetValue(new Coord(801, -798, 1603)));
        Assert.Equal(1, leaf.OnVoxelCount);
    }

    [Fact]
    public void StoredValuesAndActiveStateAreIndependent()
    {
        var leaf = new LeafNode<int>(default, 10);
        var point = new Coord(1, 2, 3);
        var offset = LeafNode<int>.CoordToOffset(point);
        Assert.False(leaf.ProbeValue(point, out var value)); Assert.Equal(10, value);
        leaf.SetValueOnly(point, 20);
        Assert.False(leaf.IsValueOn(point)); Assert.Equal(20, leaf.GetValue(point));
        leaf.SetValueOn(point);
        Assert.True(leaf.ProbeValue(offset, out value)); Assert.Equal(20, value);
        leaf.SetValueOnly(offset, 30);
        Assert.True(leaf.IsValueOn(offset)); Assert.Equal(30, leaf.GetValue(offset));
        leaf.SetValueOff(point);
        Assert.False(leaf.ProbeValue(point, out value)); Assert.Equal(30, value);
        leaf.SetValueOn(offset, 40);
        Assert.True(leaf.IsValueOn(point)); Assert.Equal(40, leaf.GetValue(point));
        leaf.SetValueOff(offset, 50);
        Assert.True(leaf.IsValueOff(offset)); Assert.Equal(50, leaf.GetValue(offset));
        leaf.SetActiveState(point, true);
        Assert.True(leaf.IsValueOn(point)); Assert.Equal(50, leaf.GetValue(point));
        leaf.SetActiveState(offset, false);
        Assert.True(leaf.IsValueOff(point)); Assert.Equal(50, leaf.GetValue(point));
        leaf.SetValueOn(offset);
        leaf.SetValueOff(offset);
        leaf.SetValueOff(point, 60);
        Assert.True(leaf.IsEmpty); Assert.Equal(60, leaf.GetValue(point));
    }

    [Fact]
    public void FillWithoutStatePreservesMaskAndFillWithStateReplacesIt()
    {
        var leaf = new LeafNode<float>(default, 2f);
        leaf.SetValueOn(3, 3f);
        leaf.SetValueOn(411, 4f);
        leaf.Fill(-7f);
        Assert.Equal(new[] { 3, 411 }, leaf.ActiveOffsets);
        for (var i = 0; i < LeafNode<float>.Size; ++i) Assert.Equal(-7f, leaf.GetValue(i));
        leaf.Fill(19f, true);
        Assert.True(leaf.IsDense);
        Assert.True(leaf.IsConstant(out var value, out var active));
        Assert.True(active); Assert.Equal(19f, value);
        leaf.Fill(-23f, false);
        Assert.True(leaf.IsEmpty);
        Assert.True(leaf.IsConstant(out value, out active));
        Assert.False(active); Assert.Equal(-23f, value);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(-9, 17, -1)]
    [InlineData(int.MinValue, int.MinValue, int.MinValue)]
    [InlineData(int.MaxValue, int.MaxValue, int.MaxValue)]
    public void PartialFillClipsInclusiveBoundsWithoutIntegerOverflow(int x, int y, int z)
    {
        var leaf = new LeafNode<int>(new Coord(x, y, z), 100, true);
        var origin = leaf.Origin;
        var box = new CoordBBox(new Coord(origin.X + 2, origin.Y + 3, origin.Z + 4),
            new Coord(origin.X + 7, origin.Y + 6, origin.Z + 7));
        leaf.Fill(box, -5, false);
        Assert.Equal(512 - 6 * 4 * 4, leaf.OnVoxelCount);
        for (var offset = 0; offset < LeafNode<int>.Size; ++offset)
        {
            var inside = box.Contains(leaf.OffsetToGlobalCoord(offset));
            Assert.Equal(inside ? -5 : 100, leaf.GetValue(offset));
            Assert.Equal(!inside, leaf.IsValueOn(offset));
        }
        leaf.Fill(leaf.Bounds, 55, true);
        Assert.True(leaf.IsDense);
        Assert.True(leaf.IsConstant(out var value, out var active));
        Assert.Equal(55, value); Assert.True(active);
    }

    [Fact]
    public void PartialFillAcceptsOversizedBoxAndIgnoresDisjointAndEmptyBoxes()
    {
        var leaf = new LeafNode<int>(new Coord(-8, 8, 0), 9);
        var crossing = new CoordBBox(new Coord(-100, 10, -100), new Coord(-5, 100, 3));
        leaf.Fill(crossing, 8);
        Assert.Equal(4 * 6 * 4, leaf.OnVoxelCount);
        for (var offset = 0; offset < 512; ++offset)
        {
            var inside = crossing.Contains(leaf.OffsetToGlobalCoord(offset));
            Assert.Equal(inside, leaf.IsValueOn(offset));
            Assert.Equal(inside ? 8 : 9, leaf.GetValue(offset));
        }
        var before = leaf.DeepCopy();
        leaf.Fill(new CoordBBox(new Coord(100, 100, 100), new Coord(101, 101, 101)), 55, true);
        leaf.Fill(new CoordBBox(new Coord(1, 1, 1), new Coord(0, 0, 0)), 66, false);
        Assert.True(before.HasSameTopology(leaf));
        Assert.Equal(before.Values.ToArray(), leaf.Values.ToArray());
        leaf.Fill(new CoordBBox(new Coord(int.MinValue, int.MinValue, int.MinValue),
            new Coord(int.MaxValue, int.MaxValue, int.MaxValue)), -1, true);
        Assert.True(leaf.IsDense);
        Assert.True(leaf.IsConstant(out var value, out _)); Assert.Equal(-1, value);
    }

    [Fact]
    public void SingleVoxelFillChangesExactlyOneVoxel()
    {
        var leaf = new LeafNode<int>(default);
        for (var i = 0; i < 512; ++i)
        {
            var point = leaf.OffsetToGlobalCoord(i);
            leaf.Fill(new CoordBBox(point, point), i + 1, true);
            Assert.Equal(i + 1, leaf.OnVoxelCount);
            Assert.Equal(i + 1, leaf.GetValue(i));
        }
        Assert.True(leaf.IsDense);
    }

    [Fact]
    public void ClipRetainsInsideActivityAndStoredValues()
    {
        var leaf = new LeafNode<int>(new Coord(-8, -8, -8));
        for (var i = 0; i < 512; ++i)
        {
            leaf.SetValueOnly(i, i);
            leaf.SetActiveState(i, (i & 1) == 0);
        }
        var box = new CoordBBox(new Coord(-6, -5, -4), new Coord(-3, -2, -1));
        leaf.Clip(box, -99);
        for (var i = 0; i < 512; ++i)
        {
            var inside = box.Contains(leaf.OffsetToGlobalCoord(i));
            Assert.Equal(inside ? i : -99, leaf.GetValue(i));
            Assert.Equal(inside && (i & 1) == 0, leaf.IsValueOn(i));
        }
        var before = leaf.DeepCopy();
        leaf.Clip(leaf.Bounds, -88);
        Assert.Equal(before.Values.ToArray(), leaf.Values.ToArray());
        Assert.True(before.HasSameTopology(leaf));
        leaf.Clip(new CoordBBox(new Coord(100, 100, 100), new Coord(110, 110, 110)), -77);
        Assert.True(leaf.IsEmpty);
        Assert.True(leaf.IsConstant(out var value, out var active));
        Assert.Equal(-77, value); Assert.False(active);
    }

    [Fact]
    public void CopyAndMaskSnapshotsAreIndependent()
    {
        var original = new LeafNode<long>(new Coord(-7, 9, 18), 123, true);
        original.SetValueOff(511, long.MaxValue);
        var copy = original.DeepCopy();
        Assert.Equal(original.Origin, copy.Origin);
        Assert.Equal(original.Values.ToArray(), copy.Values.ToArray());
        Assert.True(original.HasSameTopology(copy));
        copy.SetValueOn(511, long.MinValue);
        copy.SetValueOff(0, 987);
        Assert.Equal(long.MaxValue, original.GetValue(511));
        Assert.False(original.IsValueOn(511));
        Assert.Equal(123, original.GetValue(0));
        Assert.True(original.IsValueOn(0));
        var mask = original.ValueMask;
        mask.SetOff();
        Assert.Equal(511, original.OnVoxelCount);
        original.SetValueOn(511, 9);
        Assert.True(mask.IsOff());
    }

    [Fact]
    public void IsConstantRequiresUniformValuesAndActivity()
    {
        var leaf = new LeafNode<int>(default, 22);
        Assert.True(leaf.IsConstant(out var value, out var active));
        Assert.Equal(22, value); Assert.False(active);
        leaf.SetValueOn(0, 22);
        Assert.False(leaf.IsConstant(out _, out _));
        leaf.SetValueOff(0, 22);
        leaf.SetValueOnly(511, 23);
        Assert.False(leaf.IsConstant(out _, out _));
        leaf.Fill(22, true);
        Assert.True(leaf.IsConstant(out value, out active));
        Assert.Equal(22, value); Assert.True(active);
        leaf.SetValueOnly(0, 23);
        Assert.False(leaf.IsConstant(out _, out _));
    }

    [Fact]
    public void FloatingPointUniformityHandlesNaNInfinityAndSignedZeroConservatively()
    {
        var single = new LeafNode<float>(default, float.NaN, true);
        Assert.False(single.IsConstant(out _, out _));
        single.Fill(float.PositiveInfinity);
        Assert.True(single.IsConstant(out _, out _));
        single.SetValueOnly(111, float.NegativeInfinity);
        Assert.False(single.IsConstant(out _, out _));
        single.Fill(0f);
        single.SetValueOnly(123, -0f);
        Assert.True(single.IsConstant(out _, out _));
        single.SetValueOnly(456, float.NaN);
        Assert.False(single.IsConstant(out _, out _));
        var vector = new LeafNode<Vec3d>(default, new Vec3d(0, double.NaN, 0));
        Assert.False(vector.IsConstant(out _, out _));
        vector.Fill(new Vec3d(double.PositiveInfinity, 0, -0d));
        Assert.True(vector.IsConstant(out _, out _));
        var real = new LeafNode<double>(default, double.NaN);
        Assert.False(real.IsConstant(out _, out _));
        real.Fill(double.PositiveInfinity);
        Assert.True(real.IsConstant(out _, out _));
        var half = new LeafNode<Half>(default, Half.NaN);
        Assert.False(half.IsConstant(out _, out _));
        half.Fill(Half.PositiveInfinity);
        Assert.True(half.IsConstant(out _, out _));
    }

    [Fact]
    public void BulkSpanOperationsCopyAndValidateBeforeMutation()
    {
        var source = Enumerable.Range(0, 512).Select(static i => i * 7).ToArray();
        var leaf = new LeafNode<int>(default, -1);
        leaf.SetValues(source, true);
        Assert.True(leaf.IsDense);
        Assert.Equal(source, leaf.Values.ToArray());
        source[0] = 999;
        Assert.Equal(0, leaf.GetValue(0));
        var destination = Enumerable.Repeat(-99, 520).ToArray();
        leaf.CopyValuesTo(destination);
        Assert.Equal(leaf.Values.ToArray(), destination[..512]);
        Assert.All(destination[512..], static value => Assert.Equal(-99, value));
        Assert.Throws<ArgumentException>(() => leaf.SetValues(new int[511], false));
        Assert.Throws<ArgumentException>(() => leaf.SetValues(new int[513], false));
        Assert.Throws<ArgumentException>(() => leaf.CopyValuesTo(new int[511]));
        Assert.True(leaf.IsDense);
        Assert.Equal(7, leaf.GetValue(1));
        leaf.SetValues(leaf.Values, false); // The source is allowed to alias the destination.
        Assert.True(leaf.IsEmpty);
        Assert.Equal(7, leaf.GetValue(1));
    }

    [Fact]
    public void GenericVoxelTypesNeedNoFloatingPointOrManagedObjectStorage()
    {
        var blocks = new LeafNode<Block>(default, Block.Stone, true);
        blocks.SetValueOff(123, Block.Air);
        Assert.Equal(Block.Air, blocks.GetValue(123));
        Assert.Equal(Block.Stone, blocks.GetValue(124));
        var voxels = new LeafNode<Voxel>(default, new Voxel(12, 3, 8));
        Assert.True(voxels.IsConstant(out var value, out _));
        Assert.Equal(new Voxel(12, 3, 8), value);
        voxels.SetValueOn(9, new Voxel(77, 0, 15));
        Assert.False(voxels.IsConstant(out _, out _));
        var booleans = new LeafNode<bool>(default, true);
        Assert.True(booleans.IsConstant(out var boolean, out var active));
        Assert.True(boolean); Assert.False(active);
        booleans.SetValueOn(123, false);
        Assert.True(booleans.IsValueOn(123)); Assert.False(booleans.GetValue(123));
    }

    [Fact]
    public void TopologyBooleanOperationsPreserveValuesAndAllowDifferentVoxelTypes()
    {
        var left = new LeafNode<int>(default, 17);
        var right = new LeafNode<byte>(new Coord(8, 8, 8), 29);
        for (var i = 0; i < 512; ++i)
        {
            left.SetActiveState(i, i % 2 == 0);
            right.SetActiveState(i, i % 3 == 0);
        }
        Assert.False(left.HasSameTopology(right));
        var union = left.DeepCopy(); union.TopologyUnion(right);
        var intersection = left.DeepCopy(); intersection.TopologyIntersection(right);
        var difference = left.DeepCopy(); difference.TopologyDifference(right);
        for (var i = 0; i < 512; ++i)
        {
            Assert.Equal(i % 2 == 0 || i % 3 == 0, union.IsValueOn(i));
            Assert.Equal(i % 2 == 0 && i % 3 == 0, intersection.IsValueOn(i));
            Assert.Equal(i % 2 == 0 && i % 3 != 0, difference.IsValueOn(i));
            Assert.Equal(17, union.GetValue(i));
            Assert.Equal(17, intersection.GetValue(i));
            Assert.Equal(17, difference.GetValue(i));
        }
        left.TopologyUnion(left);
        Assert.Equal(256, left.OnVoxelCount);
        left.TopologyIntersection(left);
        Assert.Equal(256, left.OnVoxelCount);
        left.TopologyDifference(left);
        Assert.True(left.IsEmpty);
        right.Fill(0, false);
        Assert.True(left.HasSameTopology(right));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(512)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void InvalidOffsetsThrowBeforeMutation(int offset)
    {
        var leaf = new LeafNode<int>(default, 11, true);
        Assert.Throws<ArgumentOutOfRangeException>(() => leaf.GetValue(offset));
        Assert.Throws<ArgumentOutOfRangeException>(() => leaf.ProbeValue(offset, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => leaf.IsValueOn(offset));
        Assert.Throws<ArgumentOutOfRangeException>(() => leaf.IsValueOff(offset));
        Assert.Throws<ArgumentOutOfRangeException>(() => leaf.SetValueOn(offset, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => leaf.SetValueOff(offset, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => leaf.SetValueOnly(offset, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => leaf.SetActiveState(offset, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => LeafNode<int>.OffsetToLocalCoord(offset));
        Assert.Throws<ArgumentOutOfRangeException>(() => leaf.OffsetToGlobalCoord(offset));
        Assert.True(leaf.IsConstant(out var value, out var active));
        Assert.Equal(11, value); Assert.True(active);
    }

    [Theory]
    [InlineData(113)]
    [InlineData(4721)]
    [InlineData(872719)]
    public void RandomVoxelMutationsAndBoxFillsMatchDenseReference(int seed)
    {
        var random = new Random(seed);
        var origin = new Coord(-24, 64, -48);
        var leaf = new LeafNode<int>(origin, -17);
        var values = Enumerable.Repeat(-17, 512).ToArray();
        var activity = new bool[512];
        for (var step = 0; step < 10_000; ++step)
        {
            var offset = random.Next(512);
            var value = random.Next();
            var active = random.Next(2) == 0;
            var coordinate = leaf.OffsetToGlobalCoord(offset);
            switch (random.Next(7))
            {
                case 0: leaf.SetValueOn(coordinate, value); values[offset] = value; activity[offset] = true; break;
                case 1: leaf.SetValueOff(offset, value); values[offset] = value; activity[offset] = false; break;
                case 2: leaf.SetValueOnly(coordinate, value); values[offset] = value; break;
                case 3: leaf.SetActiveState(coordinate, active); activity[offset] = active; break;
                case 4: leaf.Fill(value, active); Array.Fill(values, value); Array.Fill(activity, active); break;
                case 5: leaf.Fill(value); Array.Fill(values, value); break;
                default:
                    var a = new Coord(origin.X + random.Next(-3, 11), origin.Y + random.Next(-3, 11), origin.Z + random.Next(-3, 11));
                    var b = new Coord(origin.X + random.Next(-3, 11), origin.Y + random.Next(-3, 11), origin.Z + random.Next(-3, 11));
                    var box = new CoordBBox(a, b);
                    leaf.Fill(box, value, active);
                    for (var i = 0; i < 512; ++i)
                        if (box.Contains(leaf.OffsetToGlobalCoord(i))) { values[i] = value; activity[i] = active; }
                    break;
            }
            Assert.Equal(values[offset], leaf.GetValue(coordinate));
            Assert.Equal(activity[offset], leaf.IsValueOn(offset));
            if (step % 97 != 0) continue;
            Assert.Equal(values, leaf.Values.ToArray());
            Assert.Equal(activity.Count(static on => on), leaf.OnVoxelCount);
            Assert.Equal(Enumerable.Range(0, 512).Where(i => activity[i]), leaf.ActiveOffsets);
            var expectedConstant = activity.All(a => a == activity[0]) && values.All(v => v == values[0]);
            Assert.Equal(expectedConstant, leaf.IsConstant(out _, out _));
        }
    }

    private enum Block : ushort { Air, Stone, Dirt }
    private readonly record struct Voxel(ushort Material, byte Light, byte Flags);
}


public sealed class NumericLeafTests
{
    [Fact]
    public void FirstValueToleranceAndRangeToleranceHaveDistinctUpstreamSemantics()
    {
        var leaf = new LeafNode<double>(default, 10, true);
        leaf.SetValueOnly(17, 9);
        leaf.SetValueOnly(511, 11);
        Assert.False(leaf.IsConstant(out _, out _));
        Assert.True(leaf.IsConstant(out var first, out var active, 1));
        Assert.Equal(10, first); Assert.True(active);
        Assert.False(leaf.IsConstant(out _, out _, out _, 1));
        Assert.True(leaf.IsConstant(out var minimum, out var maximum, out active, 2));
        Assert.Equal(9, minimum); Assert.Equal(11, maximum); Assert.True(active);
        leaf.SetValueOff(0);
        Assert.False(leaf.IsConstant(out _, out _, 100));
        Assert.False(leaf.IsConstant(out _, out _, out _, 100));
    }

    [Fact]
    public void IntegerToleranceCannotWrapAcrossTheSignedDomain()
    {
        var leaf = new LeafNode<int>(default, int.MinValue);
        leaf.SetValueOnly(511, int.MaxValue);
        Assert.False(leaf.IsConstant(out _, out _, int.MaxValue));
        Assert.False(leaf.IsConstant(out _, out _, out _, int.MaxValue));
        var wide = new LeafNode<long>(default, long.MinValue);
        wide.SetValueOnly(511, long.MaxValue);
        Assert.False(wide.IsConstant(out _, out _, long.MaxValue));
        var unsigned = new LeafNode<ulong>(default, ulong.MaxValue);
        unsigned.SetValueOnly(511, 0);
        Assert.True(unsigned.IsConstant(out _, out _, ulong.MaxValue));
        Assert.False(unsigned.IsConstant(out _, out _, ulong.MaxValue - 1));
    }

    [Fact]
    public void FloatingPointToleranceConservativelyHandlesNaNAndInfinity()
    {
        var leaf = new LeafNode<float>(default, float.PositiveInfinity);
        Assert.True(leaf.IsConstant(out _, out _, 0f));
        Assert.True(leaf.IsConstant(out _, out _, out _, 0f));
        leaf.SetValueOnly(17, float.NegativeInfinity);
        Assert.False(leaf.IsConstant(out _, out _, float.MaxValue));
        leaf.Fill(float.NaN);
        Assert.False(leaf.IsConstant(out _, out _, 1f));
        Assert.False(leaf.IsConstant(out _, out _, out _, 1f));
        leaf.Fill(0f);
        leaf.SetValueOnly(511, float.NaN);
        Assert.False(leaf.IsConstant(out _, out _, 1f));
        Assert.False(leaf.IsConstant(out _, out _, out _, 1f));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void InvalidTolerancesAreRejected(double tolerance)
    {
        var leaf = new LeafNode<double>(default);
        Assert.Throws<ArgumentOutOfRangeException>(() => leaf.IsConstant(out _, out _, tolerance));
        Assert.Throws<ArgumentOutOfRangeException>(() => leaf.IsConstant(out _, out _, out _, tolerance));
    }

    [Fact]
    public void ExactZeroToleranceAndDecimalToleranceAreSupported()
    {
        var integer = new LeafNode<int>(default, 123);
        Assert.True(integer.IsConstant(out var minimum, out var maximum, out var active));
        Assert.Equal(123, minimum); Assert.Equal(123, maximum); Assert.False(active);
        integer.SetValueOnly(111, 124);
        Assert.False(integer.IsConstant(out _, out _, 0));
        Assert.True(integer.IsConstant(out _, out _, 1));
        var fractional = new LeafNode<decimal>(default, 1m);
        fractional.SetValueOnly(4, 1.01m);
        Assert.False(fractional.IsConstant(out _, out _, 0.009m));
        Assert.True(fractional.IsConstant(out _, out _, 0.01m));
        Assert.True(fractional.IsConstant(out _, out _, out _, 0.01m));
    }
}
