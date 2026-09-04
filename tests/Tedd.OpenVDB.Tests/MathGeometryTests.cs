using Tedd.OpenVDB.Math;

namespace Tedd.OpenVDB.Tests;

public class MathGeometryTests
{
    [Fact]
    public void FloatingBoundsSeparateZeroVolumeFromInvalidBounds()
    {
        Assert.True(BBoxd.Empty.IsEmpty);
        Assert.False(BBoxd.Empty.IsValid);
        var point = new BBoxd(new Vec3d(1), new Vec3d(1));
        Assert.True(point.IsEmpty);
        Assert.True(point.IsValid);
        Assert.Equal(0, point.Volume);
        Assert.Equal(new Vec3d(1), point.Center);
        Assert.True(point.Contains(new Vec3d(1)));
        var expanded = point.Expand(new Vec3d(4));
        Assert.Equal(new Vec3d(1), expanded.Min);
        Assert.Equal(new Vec3d(4), expanded.Max);
        Assert.Equal(27, expanded.Volume);
        Assert.Equal(expanded, BBoxd.Empty.Expand(point).Expand(new Vec3d(4)));
    }
    [Fact]
    public void FloatingBoundsPreserveUpstreamPaddingAndStrictBoxContainment()
    {
        var box = new BBoxd(Vec3d.Zero, new Vec3d(4));
        Assert.Equal(box.Expand(1), box.Expand(-1));
        Assert.True(box.Contains(new BBoxd(new Vec3d(1), new Vec3d(3))));
        Assert.False(box.Contains(box));
        Assert.True(box.Intersects(new BBoxd(new Vec3d(4), new Vec3d(5))));
        Assert.False(box.Intersects(new BBoxd(new Vec3d(5), new Vec3d(6))));
        Assert.Equal(box, new BBoxd(new Vec3d(4), Vec3d.Zero, sorted: false));
        Assert.Equal(box, box.Translate(new Vec3d(3)).Translate(new Vec3d(-3)));
    }
    [Fact]
    public void TransformedCoordinateBoundsDescribeCentersWithoutAddingVoxelWidth()
    {
        var transform = Transform.CreateLinearTransform(2);
        transform.PostTranslate(new Vec3d(1));
        var box = transform.IndexToWorld(new CoordBBox(Coord.Zero, new Coord(3)));
        Assert.Equal(new Vec3d(1), box.Min);
        Assert.Equal(new Vec3d(7), box.Max);
        var single = transform.IndexToWorld(new CoordBBox(new Coord(2), new Coord(2)));
        Assert.True(single.IsEmpty);
        Assert.True(single.IsValid);
        Assert.Equal(new Vec3d(5), single.Min);
        Assert.Equal(single.Min, single.Max);
        Assert.False(transform.IndexToWorld(CoordBBox.Empty).IsValid);
        Assert.True(transform.WorldToIndexNodeCentered(BBoxd.Empty).IsEmpty);
    }
    [Fact]
    public void TransformedBoundsRoundBothCornersAccordingToGridCentering()
    {
        var transform = Transform.CreateLinearTransform(2);
        var world = new BBoxd(new Vec3d(-1, -3, -5), new Vec3d(1, 3, 5));
        Assert.Equal(new CoordBBox(new Coord(0, -1, -2), new Coord(1, 2, 3)), transform.WorldToIndexCellCentered(world));
        Assert.Equal(new CoordBBox(new Coord(-1, -2, -3), new Coord(0, 1, 2)), transform.WorldToIndexNodeCentered(world));
    }
    [Fact]
    public void RandomAffineBoundsMatchIndependentCenterExtentFormula()
    {
        Random random = new(73531);
        for (int iteration = 0; iteration < 300; ++iteration)
        {
            var transform = new Transform();
            transform.PostScale(new Vec3d(2, -3, 4));
            transform.PostRotate(random.NextDouble() * 6, Axis.X);
            transform.PostRotate(random.NextDouble() * 6, Axis.Y);
            transform.PostShear(random.NextDouble(), Axis.X, Axis.Z);
            transform.PostTranslate(new Vec3d(3, -4, 7));
            var min = new Vec3d(random.NextDouble() * -5, random.NextDouble() * -5, random.NextDouble() * -5);
            var max = new Vec3d(random.NextDouble() * 8, random.NextDouble() * 8, random.NextDouble() * 8);
            var bounds = transform.IndexToWorld(new BBoxd(min, max));
            var center = transform.IndexToWorld((min + max) * 0.5);
            var extent = (max - min) * 0.5;
            var m = transform.Matrix;
            var expectedExtent = new Vec3d(
                System.Math.Abs(m.M00) * extent.X + System.Math.Abs(m.M10) * extent.Y + System.Math.Abs(m.M20) * extent.Z,
                System.Math.Abs(m.M01) * extent.X + System.Math.Abs(m.M11) * extent.Y + System.Math.Abs(m.M21) * extent.Z,
                System.Math.Abs(m.M02) * extent.X + System.Math.Abs(m.M12) * extent.Y + System.Math.Abs(m.M22) * extent.Z);
            Assert.True(bounds.Min.ApproxEquals(center - expectedExtent, 1e-12));
            Assert.True(bounds.Max.ApproxEquals(center + expectedExtent, 1e-12));
        }
    }
    [Theory]
    [InlineData(-1, 0.5, 0.5, 1, 0, 0, 1, 2)]
    [InlineData(2, 0.5, 0.5, -1, 0, 0, 1, 2)]
    [InlineData(0.5, -1, 0.5, 0, 1, 0, 1, 2)]
    [InlineData(0.5, 0.5, 2, 0, 0, -1, 1, 2)]
    [InlineData(-1, 0, 0, 1, 0, 0, 1, 2)]
    [InlineData(-1, 1, 1, 1, 0, 0, 1, 2)]
    [InlineData(-2, -2, -2, 2, 2, 2, 1, 1.5)]
    public void SlabRayIntersectionHandlesSignsAndParallelFaces(double x, double y, double z, double dx, double dy, double dz, double expectedEnter, double expectedExit)
    {
        var ray = new Rayd(new Vec3d(x, y, z), new Vec3d(dx, dy, dz));
        var box = new BBoxd(Vec3d.Zero, Vec3d.One);
        Assert.True(ray.Intersects(box, out var enter, out var exit));
        Assert.Equal(expectedEnter, enter);
        Assert.Equal(expectedExit, exit);
        Assert.True(ray.TryClip(box, out var clipped));
        Assert.Equal(ray.At(enter), clipped.Start);
        Assert.Equal(ray.At(exit), clipped.End);
    }
    [Fact]
    public void RaysRejectMissesInvalidIntervalsAndEmptyBounds()
    {
        var box = new BBoxd(Vec3d.Zero, Vec3d.One);
        Assert.False(new Rayd(new Vec3d(-1, 2, 0), Vec3d.UnitX).Intersects(box));
        Assert.False(new Rayd(new Vec3d(2, 0.5, 0.5), Vec3d.UnitX).Intersects(box));
        Assert.False(new Rayd(new Vec3d(-1, 0.5, 0.5), Vec3d.UnitX, 0, 0.5).Intersects(box));
        Assert.False(new Rayd(new Vec3d(-1, 0.5, 0.5), Vec3d.UnitX, 3, 2).Intersects(box));
        Assert.False(new Rayd().Intersects(BBoxd.Empty));
        Assert.Throws<ArgumentException>(() => new Rayd(Vec3d.Zero, Vec3d.Zero));
        Assert.Throws<ArgumentException>(() => new Rayd(new Vec3d(double.NaN), Vec3d.UnitX));
    }
    [Fact]
    public void ClippingKeepsExistingParameterBounds()
    {
        var ray = new Rayd(new Vec3d(-1, 0, 0), Vec3d.UnitX, 1.25, 1.75);
        Assert.True(ray.Intersects(new BBoxd(Vec3d.Zero, Vec3d.One), out double enter, out double exit));
        Assert.Equal(1.25, enter);
        Assert.Equal(1.75, exit);
        Assert.True(ray.Test(1.25));
        Assert.True(ray.Test(1.75));
        Assert.False(ray.Test(2));
    }
    [Fact]
    public void SphereIntersectionsHandleTangencyAndOriginsOnSurface()
    {
        Assert.True(new Rayd(new Vec3d(-2, 0, 0), Vec3d.UnitX, 0).IntersectsSphere(Vec3d.Zero, 1, out double enter, out double exit));
        Assert.Equal(1, enter);
        Assert.Equal(3, exit);
        Assert.True(new Rayd(new Vec3d(-2, 1, 0), Vec3d.UnitX, 0).IntersectsSphere(Vec3d.Zero, 1, out enter, out exit));
        Assert.Equal(2, enter);
        Assert.Equal(2, exit);
        Assert.True(new Rayd(new Vec3d(0, 1, 0), Vec3d.UnitX, 0).IntersectsSphere(Vec3d.Zero, 1, out enter, out exit));
        Assert.Equal(0, enter);
        Assert.Equal(0, exit);
        Assert.False(new Rayd(new Vec3d(-2, 2, 0), Vec3d.UnitX).IntersectsSphere(Vec3d.Zero, 1, out _, out _));
        Assert.True(new Rayd(Vec3d.Zero, Vec3d.UnitX, 0).IntersectsSphere(Vec3d.Zero, 1, out enter, out exit));
        Assert.Equal(0, enter);
        Assert.Equal(1, exit);
    }
    [Fact]
    public void PlaneIntersectionsRejectParallelAndBehindRay()
    {
        var ray = new Rayd(Vec3d.Zero, Vec3d.UnitX);
        Assert.True(ray.IntersectsPlane(Vec3d.UnitX, 3, out double time));
        Assert.Equal(3, time);
        Assert.False(ray.IntersectsPlane(Vec3d.UnitY, 3, out _));
        Assert.False(ray.IntersectsPlane(Vec3d.UnitX, -3, out _));
        Assert.True(ray.IntersectsPlane(Vec3d.UnitX, new Vec3d(3, 5, 7), out time));
        Assert.Equal(3, time);
    }
    [Fact]
    public void RayAffineRoundTripPreservesGeometryAndParameterScale()
    {
        var transform = new Transform();
        transform.PostScale(new Vec3d(2, -3, 5));
        transform.PostRotate(0.65, Axis.Y);
        transform.PostTranslate(new Vec3d(-11, 3, 5));
        var ray = new Rayd(new Vec3d(3, 4, 5), new Vec3d(1, 2, 3).Unit(), 0.01, 8);
        var world = ray.IndexToWorld(transform);
        Assert.True(transform.IndexToWorld(ray.Start).ApproxEquals(world.Start, 1e-12));
        Assert.True(transform.IndexToWorld(ray.End).ApproxEquals(world.End, 1e-12));
        Assert.InRange(System.Math.Abs(world.Direction.Length - 1), 0, 1e-12);
        var restored = world.WorldToIndex(transform);
        Assert.True(ray.Eye.ApproxEquals(restored.Eye, 1e-12));
        Assert.True(ray.Direction.ApproxEquals(restored.Direction, 1e-12));
        Assert.InRange(System.Math.Abs(ray.MinTime - restored.MinTime), 0, 1e-12);
        Assert.InRange(System.Math.Abs(ray.MaxTime - restored.MaxTime), 0, 1e-12);
        Assert.Throws<InvalidOperationException>(() => new Rayd(Vec3d.Zero, new Vec3d(2, 0, 0)).IndexToWorld(transform));
    }
    [Fact]
    public void GridClassParsingTrimsAndIgnoresAsciiCase() => Assert.Equal(GridClass.LevelSet, Grid<byte>.ParseGridClass("  LEVEL SET \t"));
}
