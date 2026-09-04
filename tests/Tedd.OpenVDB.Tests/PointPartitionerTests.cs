using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
namespace Tedd.OpenVDB.Tests;

public sealed class PointPartitionerTests
{
    [Fact]
    public void CellAndNodeCenteredCoordinatesDifferAtNegativeHalfBoundaries()
    {
        var points = new PointArray([new(-.5), new(.5), new(7.5), new(-8.5)]); var transform = Transform.CreateLinearTransform(); var cell = PointPartitioner.Create(points, transform, recordVoxelOffsets: true); var node = PointPartitioner.Create(points, transform, recordVoxelOffsets: true, cellCenteredTransform: false);
        Assert.Equal(new[] { 0, 73, 0, 0 }, cell.VoxelOffsets.ToArray()); Assert.Equal(new[] { 511, 0, 511, 511 }, node.VoxelOffsets.ToArray());
    }
    [Fact]
    public void NativeBinOrderDiffersFromGlobalLexicographicLeafOrder()
    {
        var partition = PointPartitioner.Create(new PointArray([new(8, 0, 0), new(0, 256, 0)]), Transform.CreateLinearTransform()); Assert.Equal(new Coord(8, 0, 0), partition.Origin(0)); Assert.Equal(new Coord(0, 256, 0), partition.Origin(1));
    }
    [Fact]
    public void VoxelSortIsStableAndOffsetsCanBeDiscarded()
    {
        var points = new PointArray([new(3, 0, 0), new(1, 0, 0), new(3, 0, 0), new(0, 1, 0)]); var partition = PointPartitioner.Create(points, Transform.CreateLinearTransform(), voxelOrder: true);
        Assert.Equal(new[] { 3, 1, 0, 2 }, partition.Indices(0).ToArray()); Assert.True(partition.VoxelOffsets.IsEmpty); Assert.Equal(4, partition.PointCount);
    }
    [Fact]
    public void NonfinitePointsHaveSafeAbsentOffsetsAndNeverEnterBuckets()
    {
        var partition = PointPartitioner.Create(new PointArray([new(double.NaN, 0, 0), new(0, double.PositiveInfinity, 0), Vec3d.Zero]), Transform.CreateLinearTransform(), recordVoxelOffsets: true);
        Assert.Equal(new[] { -1, -1, 0 }, partition.VoxelOffsets.ToArray()); Assert.Equal(new[] { 2 }, partition.Indices(0).ToArray());
    }
    [Fact]
    public void ConstructIsAtomicWhenCancelledOrPositionCallbackThrows()
    {
        var partition = PointPartitioner.Create(new PointArray([Vec3d.Zero]), Transform.CreateLinearTransform()); Assert.Throws<OperationCanceledException>(() => partition.Construct(new PointArray([new(100)]), Transform.CreateLinearTransform(), cancellationToken: new(true)));
        Assert.Throws<ArithmeticException>(() => partition.Construct(new ThrowingSource(), Transform.CreateLinearTransform())); Assert.Single(partition.Indices(0).ToArray()); Assert.Equal(Coord.Zero, partition.Origin(0));
    }
    [Fact]
    public void ReuseClearSwapAndBoundsRetainIndependentArrays()
    {
        var a = PointPartitioner.Create(new PointArray([Vec3d.Zero]), Transform.CreateLinearTransform()); var b = PointPartitioner.Create(new PointArray([new(-1)]), Transform.CreateLinearTransform(), cellCenteredTransform: false);
        a.Swap(b); Assert.False(a.UsingCellCenteredTransform); Assert.Equal(new Coord(-8), a.Origin(0)); Assert.Equal(new CoordBBox(new(-8), new(-1)), a.GetBounds(0)); a.Clear(); Assert.True(a.IsEmpty); Assert.True(a.UsingCellCenteredTransform); Assert.Equal(Coord.Zero, b.Origin(0));
        Assert.Throws<ArgumentException>(() => b.Swap(new(4))); Assert.Throws<ArgumentOutOfRangeException>(() => b.Indices(-1));
    }
    [Fact]
    public void HighCoordinateBucketsAndMaximumManagedOffsetsStayInBounds()
    {
        var p = PointPartitioner.Create(new PointArray([new(int.MinValue), new(int.MaxValue)]), Transform.CreateLinearTransform(), recordVoxelOffsets: true, cellCenteredTransform: false, log2Dimension: 10);
        Assert.Equal(new[] { 0, 1073741823 }, p.VoxelOffsets.ToArray()); Assert.Equal(new Coord(int.MaxValue), p.GetBounds(1).Max); Assert.Throws<ArgumentOutOfRangeException>(() => new PointPartitioner(11));
    }
    private sealed class ThrowingSource : IPointSource { public int Count => 2; public Vec3d GetPosition(int index) => index == 0 ? Vec3d.Zero : throw new ArithmeticException(); }
}
