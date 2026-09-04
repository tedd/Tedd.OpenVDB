using System.Collections;
using Tedd.OpenVDB.Math;

namespace Tedd.OpenVDB.Tests;

public class MathCoordTests
{
    [Fact]
    public void CoordinatesHaveValueEqualityAndLexicographicOrder()
    {
        Coord coordinate = new(1, -2, 3);
        Assert.Equal(new Coord(1, -2, 3), coordinate);
        Assert.Equal(coordinate.GetHashCode(), new Coord(1, -2, 3).GetHashCode());
        Assert.True(new Coord(0, 100, 100) < coordinate);
        Assert.True(new Coord(1, -3, 100) < coordinate);
        Assert.True(new Coord(1, -2, 2) < coordinate);
        Assert.True(coordinate <= new Coord(1, -2, 3));
        Assert.True(coordinate >= new Coord(1, -2, 3));
        Assert.Equal(new[] { 1, -2, 3 }, Enumerable.Range(0, 3).Select(i => coordinate[i]));
        Assert.Throws<ArgumentOutOfRangeException>(() => coordinate[-1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => coordinate[3]);
        Assert.Equal(new Coord(1, 8, 3), coordinate.WithComponent(1, 8));
    }
    [Fact]
    public void ArithmeticAndBitwiseOperationsMatchCoordinateComponents()
    {
        Coord a = new(-8, 17, -1), b = new(4, -7, 2);
        Assert.Equal(new Coord(-4, 10, 1), a + b);
        Assert.Equal(new Coord(-12, 24, -3), a - b);
        Assert.Equal(new Coord(8, -17, 1), -a);
        Assert.Equal(new Coord(-16, 34, -2), a * 2);
        Assert.Equal(new Coord(-4, 8, -1), a >> 1);
        Assert.Equal(new Coord(-16, 34, -2), a << 1);
        Assert.Equal(new Coord(0, 1, 7), a & 7);
        Assert.Equal(new Coord(-1, 23, -1), a | 7);
        Assert.Equal(new Coord(4, 17, 2), Coord.Max(a, b));
        Assert.Equal(new Coord(-8, -7, -1), Coord.Min(a, b));
        Assert.Equal(Coord.MinValue, Coord.MaxValue.OffsetBy(1));
        Assert.Equal(Coord.MaxValue, Coord.MinValue.OffsetBy(-1));
    }
    [Theory]
    [InlineData(-2147483648, 3, -2147483648)]
    [InlineData(-9, 3, -16)]
    [InlineData(-8, 3, -8)]
    [InlineData(-1, 3, -8)]
    [InlineData(0, 3, 0)]
    [InlineData(7, 3, 0)]
    [InlineData(8, 3, 8)]
    [InlineData(2147483647, 3, 2147483640)]
    [InlineData(2147483647, 31, 0)]
    [InlineData(-1, 31, -2147483648)]
    public void AlignmentRoundsTowardNegativeInfinity(int value, int bits, int expected) =>
        Assert.Equal(new Coord(expected), new Coord(value).AlignDown(bits));

    [Fact]
    public void AlignmentMatchesDivisionOracleAcrossRandomCoordinates()
    {
        Random random = new(19837);
        for (int i = 0; i < 10000; ++i)
        {
            int value = (int)random.NextInt64(int.MinValue, (long)int.MaxValue + 1);
            int bits = random.Next(32);
            long dimension = 1L << bits;
            int expected = checked((int)(System.Math.Floor((double)value / dimension) * dimension));
            Assert.Equal(new Coord(expected), new Coord(value).AlignDown(bits));
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => Coord.Zero.AlignDown(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Coord.Zero.AlignDown(32));
    }
    [Theory]
    [InlineData(-2.5, -2, -3, -2)]
    [InlineData(-1.5, -1, -2, -1)]
    [InlineData(-0.5, 0, -1, 0)]
    [InlineData(0.5, 1, 0, 1)]
    [InlineData(1.5, 2, 1, 2)]
    [InlineData(2.5, 3, 2, 3)]
    public void CoordinateRoundingUsesOpenVdbHalfTowardPositiveInfinity(double value, int rounded, int floor, int ceiling)
    {
        Assert.Equal(new Coord(rounded), Coord.Round(new Vec3d(value)));
        Assert.Equal(new Coord(floor), Coord.Floor(new Vec3d(value)));
        Assert.Equal(new Coord(ceiling), Coord.Ceiling(new Vec3d(value)));
    }
    [Fact]
    public void InvalidFloatingCoordinatesThrowRatherThanSilentlySaturate()
    {
        Assert.Throws<OverflowException>(() => Coord.Round(new Vec3d(double.NaN)));
        Assert.Throws<OverflowException>(() => Coord.Floor(new Vec3d(double.PositiveInfinity)));
        Assert.Throws<OverflowException>(() => Coord.Ceiling(new Vec3d((double)int.MaxValue + 1)));
        Assert.Equal(Coord.MinValue, Coord.Floor(new Vec3d(int.MinValue)));
        Assert.Equal(Coord.MaxValue, Coord.Floor(new Vec3d(int.MaxValue)));
    }
    [Fact]
    public void HashUsesUpstreamPrimeMultipliersAndUnsignedCoordinates()
    {
        Coord coordinate = new(-1, 73, int.MinValue);
        uint expected = unchecked((uint)-1 * 73856093u ^ 73u * 19349669u ^ (uint)int.MinValue * 83492791u);
        Assert.Equal(expected, coordinate.Hash(32));
        Assert.Equal(expected & 1048575u, coordinate.Hash());
        Assert.Equal(0u, coordinate.Hash(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => coordinate.Hash(33));
        Assert.Throws<ArgumentOutOfRangeException>(() => coordinate.Hash(-1));
    }
    [Fact]
    public void DefaultBoundingBoxIsEmptyAndSingleCoordinateHasUnitVolume()
    {
        var empty = default(CoordBBox);
        Assert.True(empty.IsEmpty);
        Assert.Equal(Coord.MaxValue, empty.Min);
        Assert.Equal(Coord.MinValue, empty.Max);
        Assert.Equal(0UL, empty.Volume);
        Assert.Empty(empty);
        Assert.False(empty.Contains(Coord.Zero));
        Assert.False(empty.Intersects(CoordBBox.Infinite));
        var single = new CoordBBox(new Coord(5), new Coord(5));
        Assert.False(single.IsEmpty);
        Assert.Equal((1L, 1L, 1L), single.Dimensions);
        Assert.Equal(1UL, single.Volume);
        Assert.Equal(new Vec3d(5), single.Center);
        Assert.Equal(new Coord(5), Assert.Single(single));
    }
    [Fact]
    public void FullDomainDimensionsAndVolumeDoNotWrap()
    {
        var box = CoordBBox.Infinite;
        Assert.Equal((4294967296L, 4294967296L, 4294967296L), box.Dimensions);
        Assert.Equal(UInt128.One << 96, box.Volume128);
        Assert.Throws<OverflowException>(() => box.Volume);
        Assert.Equal(new Vec3d(-0.5), box.Center);
        Assert.True(box.Contains(Coord.MinValue));
        Assert.True(box.Contains(Coord.MaxValue));
        var line = new CoordBBox(new Coord(int.MinValue, 0, 0), new Coord(int.MaxValue, 0, 0));
        Assert.Equal(4294967296UL, line.Volume);
    }
    [Fact]
    public void BoundingBoxSetOperationsRespectInclusiveFaces()
    {
        var first = CoordBBox.CreateCube(Coord.Zero, 8);
        var second = new CoordBBox(new Coord(7), new Coord(10));
        Assert.Equal(new CoordBBox(new Coord(7), new Coord(7)), first.Intersection(second));
        Assert.True(first.Intersects(second));
        Assert.Equal(new CoordBBox(Coord.Zero, new Coord(10)), first.Expand(second));
        Assert.Equal(first, first.Expand(CoordBBox.Empty));
        Assert.Equal(first, CoordBBox.Empty.Expand(first));
        Assert.Equal(new CoordBBox(new Coord(-1), new Coord(8)), first.Expand(1));
        Assert.Equal(new CoordBBox(new Coord(1), new Coord(6)), first.Expand(-1));
        Assert.True(first.Expand(-4).IsEmpty);
        Assert.True(first.Intersection(new CoordBBox(new Coord(8), new Coord(9))).IsEmpty);
        Assert.Equal(first, first.Translate(new Coord(8)).Translate(new Coord(-8)));
        Assert.True(first.Contains(new CoordBBox(new Coord(1), new Coord(7))));
        Assert.False(first.Contains(CoordBBox.Empty));
    }
    [Fact]
    public void BoundingBoxConstructionAndPaddingDetectOverflow()
    {
        Assert.True(CoordBBox.CreateCube(Coord.Zero, 0).IsEmpty);
        Assert.Throws<ArgumentOutOfRangeException>(() => CoordBBox.CreateCube(Coord.Zero, -1));
        Assert.Throws<OverflowException>(() => CoordBBox.CreateCube(Coord.MaxValue, 2));
        Assert.Throws<OverflowException>(() => CoordBBox.Infinite.Expand(1));
        Assert.Throws<OverflowException>(() => CoordBBox.Infinite.Translate(new Coord(1)));
        Assert.True(new CoordBBox(new Coord(0, 5, 0), new Coord(1, 4, 1)).IsEmpty);
    }
    [Theory]
    [InlineData(-2147483648)]
    [InlineData(-2)]
    [InlineData(2147483646)]
    public void EnumerationHandlesBothIntegerEndpointsAndTraversalOrders(int start)
    {
        var box = CoordBBox.CreateCube(new Coord(start), 2);
        Coord[] zyx = box.ToArray(), xyz = box.EnumerateXYZ().ToArray();
        Assert.Equal(8, zyx.Length);
        Assert.Equal(8, xyz.Length);
        for (int i = 0; i < 8; ++i)
        {
            Assert.Equal(new Coord(start + (i >> 2), start + ((i >> 1) & 1), start + (i & 1)), zyx[i]);
            Assert.Equal(new Coord(start + (i & 1), start + ((i >> 1) & 1), start + (i >> 2)), xyz[i]);
        }
        IEnumerator enumerator = ((IEnumerable)box).GetEnumerator();
        Assert.Throws<InvalidOperationException>(() => enumerator.Current);
        for (int i = 0; i < 8; ++i) Assert.True(enumerator.MoveNext());
        Assert.False(enumerator.MoveNext());
        Assert.False(enumerator.MoveNext());
        Assert.Throws<InvalidOperationException>(() => enumerator.Current);
        enumerator.Reset();
        Assert.True(enumerator.MoveNext());
        Assert.Equal(box.Min, enumerator.Current);
    }
    [Fact]
    public void BoundingBoxIntersectionMatchesExhaustiveIntegerOracle()
    {
        Random random = new(8492);
        for (int iteration = 0; iteration < 250; ++iteration)
        {
            var a = RandomBox(random);
            var b = RandomBox(random);
            var intersection = a.Intersection(b);
            Coord[] expected = a.Where(b.Contains).ToArray();
            Assert.Equal(expected, intersection.ToArray());
            Assert.Equal((ulong)expected.Length, intersection.Volume);
            Assert.Equal(intersection, b.Intersection(a));
            Assert.Equal(a.Expand(b), b.Expand(a));
        }
    }
    private static CoordBBox RandomBox(Random random) => CoordBBox.CreateCube(new Coord(random.Next(-4, 5), random.Next(-4, 5), random.Next(-4, 5)), random.Next(1, 5));
}
