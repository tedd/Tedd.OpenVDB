using Tedd.OpenVDB.Math;
namespace Tedd.OpenVDB.Tests;

public class MathStencilTests
{
    [Fact]
    public void SnapshotMovementAndLocalUpdatesPreserveNativeCacheSemantics()
    {
        var grid = new Grid<double>(9); grid.Tree.SetValueOn(new(-1, -1, -1), 4);
        var stencil = new SevenPointStencil<double>(grid);
        Assert.Throws<InvalidOperationException>(() => stencil.CenterValue);
        stencil.MoveTo(new Coord(-1, -1, -1)); Assert.Equal(4, stencil.CenterValue);
        grid.Tree.SetValueOnly(new(-1, -1, -1), 6);
        stencil.MoveTo(new Vec3d(-0.5, -0.5, -0.5)); Assert.Equal(4, stencil.CenterValue);
        stencil.SetValue(Coord.Zero, 12); Assert.Equal(6, grid.Tree.GetValue(new(-1, -1, -1)));
        stencil.MoveTo(new Coord(-1, -1, -1)); Assert.Equal(6, stencil.CenterValue);
        stencil.MoveTo(new Coord(-1, -1, -1), 20); Assert.Equal(20, stencil.CenterValue);
        Assert.Equal(6, grid.Tree.GetValue(new(-1, -1, -1)));
    }
    [Fact]
    public void DenseCubeOrderingStatisticsAndCenterAreExplicit()
    {
        var grid = new Grid<double>(0); int value = 0;
        for (int x = -1; x <= 1; ++x) for (int y = -1; y <= 1; ++y) for (int z = -1; z <= 1; ++z) grid.Tree.SetValueOn(new(x, y, z), value++);
        var stencil = new DenseStencil<double>(grid, 1); stencil.MoveTo(Coord.Zero);
        Assert.Equal(27, stencil.Count); Assert.Equal(13, stencil.CenterIndex);
        Assert.Equal(13, stencil.CenterValue); Assert.Equal(13, stencil.Mean); Assert.Equal(13, stencil.Median);
        Assert.Equal(0, stencil.Min); Assert.Equal(26, stencil.Max);
        Assert.Equal(Enumerable.Range(0, 27).Select(x => (double)x), stencil.Values.ToArray());
        Assert.Equal(new Coord(-1, -1, -1), stencil.OffsetAt(0)); Assert.Equal(new Coord(1, 1, 1), stencil.OffsetAt(26));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DenseStencil<double>(grid, 0));
        Assert.Throws<OverflowException>(() => new DenseStencil<double>(grid, int.MaxValue));
    }
    [Fact]
    public void FaceIntersectionBitsUseStrictSignComparison()
    {
        var grid = new Grid<double>(2); var stencil = new SevenPointStencil<double>(grid); stencil.MoveTo(Coord.Zero);
        Assert.Equal(0, stencil.IntersectionMask());
        stencil.SetValue(new Coord(-1, 0, 0), -2); stencil.SetValue(new Coord(0, 1, 0), -3); stencil.SetValue(new Coord(0, 0, 1), -4);
        Assert.Equal(1 + 8 + 32, stencil.IntersectionMask()); Assert.True(stencil.Intersects());
        stencil.SetValue(Coord.Zero, 0); Assert.Equal(1 + 8 + 32, stencil.IntersectionMask());
        stencil.SetValue(Coord.Zero, -1); Assert.Equal(2 + 4 + 16, stencil.IntersectionMask());
    }
    [Fact]
    public void BoundingCoordinateFailureLeavesSnapshotUnchanged()
    {
        var stencil = new SevenPointStencil<double>(new Grid<double>(3)); stencil.MoveTo(new Coord(int.MaxValue - 1, 0, 0));
        var values = stencil.Values.ToArray(); Coord center = stencil.Center;
        Assert.Throws<OverflowException>(() => stencil.MoveTo(new Coord(int.MaxValue, 0, 0)));
        Assert.Equal(center, stencil.Center); Assert.Equal(values, stencil.Values.ToArray());
        Assert.Throws<ArgumentOutOfRangeException>(() => stencil.FirstDerivative(Axis.X, DScheme.CenteredFourth));
    }
    [Fact]
    public void BoxInterpolationGradientAndCornerCrossingMatchTrilinearPolynomial()
    {
        static double F(double x, double y, double z) => 1 + 2 * x + 3 * y + 4 * z + x * y + 2 * y * z + 3 * x * z + x * y * z;
        var grid = new Grid<double>(0) { Transform = new Transform(Mat4d.CreateScale(new Vec3d(2, 3, 4))) };
        for (int x = 0; x <= 1; ++x) for (int y = 0; y <= 1; ++y) for (int z = 0; z <= 1; ++z) grid.Tree.SetValueOn(new(x, y, z), F(x, y, z));
        var box = new BoxStencil<double>(grid); box.MoveTo(Coord.Zero);
        Vec3d p = new(0.2, 0.4, 0.7);
        Assert.InRange(System.Math.Abs(box.Interpolate(p) - F(p.X, p.Y, p.Z)), 0, 1e-13);
        Vec3d expected = new((2 + p.Y + 3 * p.Z + p.Y * p.Z) / 2, (3 + p.X + 2 * p.Z + p.X * p.Z) / 3, (4 + 2 * p.Y + 3 * p.X + p.X * p.Y) / 4);
        Assert.True(expected.ApproxEquals(box.Gradient(p), 1e-12));
        Assert.False(box.Intersects()); box.SetValue(new Coord(1, 1, 1), -1); Assert.True(box.Intersects());
        Assert.Throws<ArgumentOutOfRangeException>(() => box.Interpolate(new Vec3d(-0.1, 0, 0)));
    }
}
