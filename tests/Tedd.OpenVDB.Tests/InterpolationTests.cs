// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0

using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
using Tedd.OpenVDB.Tree;

namespace Tedd.OpenVDB.Tests;

public sealed class InterpolationTests
{
    [Fact]
    public void SamplerTraitsMatchUpstream()
    {
        Assert.Equal("point", PointSampler.Name); Assert.Equal(0, PointSampler.Order); Assert.Equal(0, PointSampler.Radius);
        Assert.False(PointSampler.Mipmap); Assert.True(PointSampler.Consistent); Assert.False(PointSampler.Staggered);
        Assert.Equal("box", BoxSampler.Name); Assert.Equal(1, BoxSampler.Order); Assert.Equal(1, BoxSampler.Radius);
        Assert.True(BoxSampler.Mipmap); Assert.True(BoxSampler.Consistent); Assert.False(BoxSampler.Staggered);
        Assert.Equal("quadratic", QuadraticSampler.Name); Assert.Equal(2, QuadraticSampler.Order); Assert.Equal(1, QuadraticSampler.Radius);
        Assert.True(QuadraticSampler.Mipmap); Assert.False(QuadraticSampler.Consistent); Assert.False(QuadraticSampler.Staggered);
    }

    [Theory]
    [InlineData(-1.5, -2)]
    [InlineData(-1.49, -1)]
    [InlineData(-0.5, -1)]
    [InlineData(-0.49, 0)]
    [InlineData(0, 0)]
    [InlineData(0.49, 0)]
    [InlineData(0.5, 1)]
    [InlineData(1.49, 1)]
    [InlineData(1.5, 2)]
    public void PointSamplerRoundsMidpointsAwayFromZero(double position, int expected)
    {
        var tree = new VdbTree<int>(-999);
        for (var x = -3; x <= 3; ++x) tree.SetValueOn(new Coord(x, x, x), x + 100);
        var coordinate = new Vec3d(position);
        Assert.True(PointSampler.Sample(tree, coordinate, out var value));
        Assert.Equal(expected + 100, value);
        Assert.Equal(value, PointSampler.Sample(tree, coordinate));
        var accessor = tree.GetAccessor();
        Assert.True(PointSampler.Sample(accessor, coordinate, out value));
        Assert.Equal(expected + 100, value);
        Assert.Equal(value, PointSampler.Sample(accessor, coordinate));
    }

    [Fact]
    public void PointSamplerPreservesInactiveValuesAndSupportsBlockIdentifiers()
    {
        var tree = new VdbTree<Block>(Block.Air);
        tree.SetValueOff(new Coord(-1, 4, 8), Block.Stone);
        Assert.False(PointSampler.Sample(tree, new Vec3d(-0.75, 4.1, 7.9), out var value));
        Assert.Equal(Block.Stone, value);
        Assert.False(PointSampler.Sample(tree, new Vec3d(500, 500, 500), out value));
        Assert.Equal(Block.Air, value);
    }

    [Theory]
    [InlineData(-9, -1, 7)]
    [InlineData(7, 127, 4095)]
    [InlineData(-4097, -129, -9)]
    public void BoxSamplerMatchesIndependentEightCornerWeightsAcrossNodeBoundaries(int x, int y, int z)
    {
        var tree = new VdbTree<double>();
        var corners = new double[] { -9, 17, 4, 8, 23, -14, 7, 91 };
        for (var dx = 0; dx < 2; ++dx)
            for (var dy = 0; dy < 2; ++dy)
                for (var dz = 0; dz < 2; ++dz)
                    tree.SetValueOn(new Coord(x + dx, y + dy, z + dz), corners[(dx * 2 + dy) * 2 + dz]);
        var random = new Random(117);
        var accessor = tree.GetAccessor();
        for (var sample = 0; sample < 100; ++sample)
        {
            var fraction = new Vec3d(random.NextDouble(), random.NextDouble(), random.NextDouble());
            var position = new Vec3d(x + fraction.X, y + fraction.Y, z + fraction.Z);
            var actualFraction = new Vec3d(position.X - x, position.Y - y, position.Z - z);
            double expected = 0;
            for (var dx = 0; dx < 2; ++dx)
                for (var dy = 0; dy < 2; ++dy)
                    for (var dz = 0; dz < 2; ++dz)
                        expected += corners[(dx * 2 + dy) * 2 + dz] *
                            (dx == 0 ? 1 - actualFraction.X : actualFraction.X) *
                            (dy == 0 ? 1 - actualFraction.Y : actualFraction.Y) *
                            (dz == 0 ? 1 - actualFraction.Z : actualFraction.Z);
            Assert.True(BoxSampler.Sample(tree, position, out var value));
            Assert.InRange(System.Math.Abs(value - expected), 0, 1e-12);
            Assert.Equal(value, BoxSampler.Sample(tree, position));
            Assert.True(BoxSampler.Sample(accessor, position, out var accessorValue));
            Assert.Equal(value, accessorValue);
            Assert.Equal(value, BoxSampler.Sample(accessor, position));
        }
    }

    [Fact]
    public void BoxSamplerReproducesAffineFieldsAtRandomPositions()
    {
        static double Field(double x, double y, double z) => 7 + 2.5 * x - 3 * y + 0.125 * z;
        var tree = new VdbTree<double>(-999);
        for (var x = -4; x <= 5; ++x)
            for (var y = -4; y <= 5; ++y)
                for (var z = -4; z <= 5; ++z)
                    tree.SetValueOn(new Coord(x, y, z), Field(x, y, z));
        var random = new Random(751);
        for (var index = 0; index < 1_000; ++index)
        {
            var position = new Vec3d(random.NextDouble() * 8 - 4, random.NextDouble() * 8 - 4, random.NextDouble() * 8 - 4);
            Assert.InRange(System.Math.Abs(BoxSampler.Sample(tree, position) - Field(position.X, position.Y, position.Z)), 0, 1e-12);
        }
    }

    [Fact]
    public void QuadraticSamplerReproducesIndependentTensorPolynomial()
    {
        static double Field(double x, double y, double z) =>
            2 + x * x + 2 * y * y - 0.5 * z * z + x * y + 0.25 * y * z + 0.125 * x * y * z + 0.0625 * x * x * y * y * z * z;
        var tree = new VdbTree<double>(-999);
        for (var x = -4; x <= 5; ++x)
            for (var y = -4; y <= 5; ++y)
                for (var z = -4; z <= 5; ++z)
                    tree.SetValueOn(new Coord(x, y, z), Field(x, y, z));
        var random = new Random(813);
        var accessor = tree.GetAccessor();
        for (var index = 0; index < 1_000; ++index)
        {
            var position = new Vec3d(random.NextDouble() * 7 - 3, random.NextDouble() * 7 - 3, random.NextDouble() * 7 - 3);
            Assert.True(QuadraticSampler.Sample(tree, position, out var value));
            Assert.InRange(System.Math.Abs(value - Field(position.X, position.Y, position.Z)), 0, 1e-9);
            Assert.Equal(value, QuadraticSampler.Sample(tree, position));
            Assert.True(QuadraticSampler.Sample(accessor, position, out var accessorValue));
            Assert.Equal(value, accessorValue);
            Assert.Equal(value, QuadraticSampler.Sample(accessor, position));
        }
    }

    [Fact]
    public void QuadraticStencilIsCenteredOnFloorForNegativeCoordinates()
    {
        var tree = new VdbTree<double>();
        tree.SetValueOn(new Coord(-1, -1, -1), 1);
        var expected = System.Math.Pow(1 - 0.75 * 0.75, 3);
        Assert.True(QuadraticSampler.Sample(tree, new Vec3d(-0.25), out var value));
        Assert.Equal(expected, value, 14);
    }

    [Fact]
    public void QuadraticSamplerMatchesIndependentLagrangeWeights()
    {
        static double Weight(int index, double t) => index switch { 0 => 0.5 * t * (t - 1), 1 => 1 - t * t, _ => 0.5 * t * (t + 1) };
        var random = new Random(48217);
        var values = Enumerable.Range(0, 27).Select(_ => random.NextDouble() * 100 - 50).ToArray();
        for (var sample = 0; sample < 1_000; ++sample)
        {
            var fraction = new Vec3d(random.NextDouble(), random.NextDouble(), random.NextDouble());
            double expected = 0;
            for (var x = 0; x < 3; ++x)
                for (var y = 0; y < 3; ++y)
                    for (var z = 0; z < 3; ++z)
                        expected += values[(x * 3 + y) * 3 + z] * Weight(x, fraction.X) * Weight(y, fraction.Y) * Weight(z, fraction.Z);
            Assert.InRange(System.Math.Abs(QuadraticSampler.TriquadraticInterpolation<double>(values, fraction) - expected), 0, 1e-12);
        }
    }

    [Fact]
    public void BoxActivityIncludesEverySupportVoxelEvenAtZeroWeight()
    {
        var tree = new VdbTree<double>(7);
        for (var x = 0; x < 2; ++x)
            for (var y = 0; y < 2; ++y)
                for (var z = 0; z < 2; ++z)
                {
                    tree.Clear();
                    tree.SetValueOn(new Coord(x, y, z), 7);
                    Assert.True(BoxSampler.Sample(tree, default, out var value));
                    Assert.Equal(7, value);
                }
        tree.Clear();
        tree.SetValueOff(new Coord(1, 1, 1), 15);
        Assert.False(BoxSampler.Sample(tree, new Vec3d(0.5), out var inactive));
        Assert.Equal(8, inactive);
    }

    [Fact]
    public void QuadraticActivityIncludesEverySupportVoxelEvenAtZeroWeight()
    {
        var tree = new VdbTree<double>(7);
        for (var x = -1; x <= 1; ++x)
            for (var y = -1; y <= 1; ++y)
                for (var z = -1; z <= 1; ++z)
                {
                    tree.Clear();
                    tree.SetValueOn(new Coord(x, y, z), 7);
                    Assert.True(QuadraticSampler.Sample(tree, default, out var value));
                    Assert.Equal(7, value);
                }
        tree.Clear();
        tree.SetValueOff(new Coord(0, 0, 0), 15);
        Assert.False(QuadraticSampler.Sample(tree, default, out var inactive));
        Assert.Equal(15, inactive);
    }

    [Fact]
    public void SamplingReadsTilesAndBackgroundWithoutMaterializingLeaves()
    {
        var tree = new VdbTree<double>(-4);
        tree.AddTile(3, default, 4, true);
        var position = new Vec3d(100.25, 200.5, 300.75);
        Assert.True(BoxSampler.Sample(tree, position, out var value)); Assert.Equal(4, value);
        Assert.True(QuadraticSampler.Sample(tree, position, out value)); Assert.Equal(4, value);
        Assert.True(PointSampler.Sample(tree, position, out value)); Assert.Equal(4, value);
        Assert.True(BoxSampler.Sample(tree, new Vec3d(4095.75, 2.5, 2.5), out value)); Assert.Equal(-2, value);
        Assert.False(BoxSampler.Sample(tree, new Vec3d(-10.5), out value)); Assert.Equal(-4, value);
        Assert.False(QuadraticSampler.Sample(tree, new Vec3d(-10.5), out value)); Assert.Equal(-4, value);
        Assert.Equal(0, tree.LeafCount);
        Assert.Equal(1, tree.RootEntryCount);
    }

    [Fact]
    public void SamplersRetainValueTypeRoundingAtEachInterpolationStage()
    {
        var integers = new[] { -10, 9, -10, 9, -10, 9, -10, 9 };
        Assert.Equal(-5, BoxSampler.TrilinearInterpolation<int>(integers, new Vec3d(0.7, 0.2, 0.3)));
        var single = new[] { 0f, 1f, 2f, 3f, 4f, 5f, 6f, 7f };
        Assert.Equal(3.5f, BoxSampler.TrilinearInterpolation<float>(single, new Vec3d(0.5)));
        var half = single.Select(static value => (Half)value).ToArray();
        Assert.Equal((Half)3.5f, BoxSampler.TrilinearInterpolation<Half>(half, new Vec3d(0.5)));
        var quadratic = new int[27];
        for (var x = 0; x < 3; ++x)
            for (var y = 0; y < 3; ++y)
                for (var z = 0; z < 3; ++z) quadratic[(x * 3 + y) * 3 + z] = z == 1 ? 3 : 0;
        // z coefficients: a=-3, b=0, c=3. The intermediate result truncates 2.25 to 2.
        Assert.Equal(2, QuadraticSampler.TriquadraticInterpolation<int>(quadratic, new Vec3d(0.25, 0.75, 0.5)));
    }

    [Fact]
    public void NaNsPropagateThroughAllInterpolationSupport()
    {
        var tree = new VdbTree<double>(1);
        tree.SetValueOn(new Coord(1, 1, 1), double.NaN);
        Assert.True(double.IsNaN(BoxSampler.Sample(tree, default)));
        Assert.True(double.IsNaN(QuadraticSampler.Sample(tree, default)));
        Assert.Equal(1, PointSampler.Sample(tree, default));
        Assert.True(double.IsNaN(PointSampler.Sample(tree, new Vec3d(1))));
        tree.Fill(new CoordBBox(new Coord(-1), new Coord(1)), double.PositiveInfinity);
        Assert.True(double.IsNaN(BoxSampler.Sample(tree, default)));
        Assert.True(double.IsNaN(QuadraticSampler.Sample(tree, default)));
        Assert.Equal(double.PositiveInfinity, PointSampler.Sample(tree, default));
    }

    [Fact]
    public void BoxProbeValuesWritesCanonicalLayoutAndPreservesExtraDestinationElements()
    {
        var tree = new VdbTree<int>();
        var origin = new Coord(-1, 7, 127);
        for (var x = 0; x < 2; ++x)
            for (var y = 0; y < 2; ++y)
                for (var z = 0; z < 2; ++z)
                    tree.SetValueOff(new Coord(origin.X + x, origin.Y + y, origin.Z + z), (x * 2 + y) * 2 + z + 1);
        var destination = Enumerable.Repeat(-1, 10).ToArray();
        Assert.False(BoxSampler.ProbeValues(tree, origin, destination.AsSpan()));
        Assert.Equal(Enumerable.Range(1, 8), destination.Take(8));
        Assert.Equal(-1, destination[8]); Assert.Equal(-1, destination[9]);
        tree.SetValueOn(origin);
        Assert.True(BoxSampler.ProbeValues(tree.GetAccessor(), origin, destination.AsSpan()));
        BoxSampler.GetValues(tree, origin, destination.AsSpan());
        BoxSampler.Extrema<int>(destination, out var minimum, out var maximum);
        Assert.Equal(1, minimum); Assert.Equal(8, maximum);
    }

    [Theory]
    [InlineData(double.NaN, 0, 0)]
    [InlineData(0, double.PositiveInfinity, 0)]
    [InlineData(0, 0, double.NegativeInfinity)]
    [InlineData(double.MaxValue, 0, 0)]
    [InlineData(double.MinValue, 0, 0)]
    public void InvalidSamplingCoordinatesAreRejected(double x, double y, double z)
    {
        var tree = new VdbTree<double>();
        var position = new Vec3d(x, y, z);
        Assert.Throws<ArgumentOutOfRangeException>(() => PointSampler.Sample(tree, position));
        Assert.Throws<ArgumentOutOfRangeException>(() => BoxSampler.Sample(tree, position));
        Assert.Throws<ArgumentOutOfRangeException>(() => QuadraticSampler.Sample(tree, position));
    }

    [Fact]
    public void StencilDomainBoundariesAreCheckedWithoutSignedOverflow()
    {
        var tree = new VdbTree<double>(1);
        var minimum = new Vec3d(int.MinValue);
        var maximum = new Vec3d(int.MaxValue);
        Assert.Equal(1, PointSampler.Sample(tree, minimum));
        Assert.Equal(1, PointSampler.Sample(tree, maximum));
        Assert.Equal(1, BoxSampler.Sample(tree, minimum));
        Assert.Equal(1, BoxSampler.Sample(tree, new Vec3d(int.MaxValue - 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => BoxSampler.Sample(tree, maximum));
        Assert.Throws<ArgumentOutOfRangeException>(() => QuadraticSampler.Sample(tree, minimum));
        Assert.Throws<ArgumentOutOfRangeException>(() => QuadraticSampler.Sample(tree, maximum));
        Assert.Equal(1, QuadraticSampler.Sample(tree, new Vec3d(int.MinValue + 1)));
        Assert.Equal(1, QuadraticSampler.Sample(tree, new Vec3d(int.MaxValue - 1)));
    }

    [Fact]
    public void ShortStencilsAndNullSourcesAreRejected()
    {
        var tree = new VdbTree<double>();
        Assert.Throws<ArgumentException>(() => BoxSampler.TrilinearInterpolation<double>(new double[7], default));
        Assert.Throws<ArgumentException>(() => QuadraticSampler.TriquadraticInterpolation<double>(new double[26], default));
        Assert.Throws<ArgumentException>(() => BoxSampler.Extrema<double>(new double[7], out _, out _));
        Assert.Throws<ArgumentException>(() => BoxSampler.ProbeValues(tree, default, new double[7].AsSpan()));
        Assert.Throws<ArgumentNullException>(() => PointSampler.Sample((VdbTree<double>)null!, default));
        Assert.Throws<ArgumentNullException>(() => BoxSampler.Sample((VdbTree<double>)null!, default));
        Assert.Throws<ArgumentNullException>(() => QuadraticSampler.Sample((VdbTree<double>)null!, default));
        Assert.Throws<ArgumentNullException>(() => PointSampler.Sample((ValueAccessor<double>)null!, default));
        Assert.Throws<ArgumentNullException>(() => BoxSampler.Sample((ValueAccessor<double>)null!, default));
        Assert.Throws<ArgumentNullException>(() => QuadraticSampler.Sample((ValueAccessor<double>)null!, default));
    }

    private enum Block : byte { Air, Stone }
}
