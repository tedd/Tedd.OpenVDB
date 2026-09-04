using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tests;

public class VelocityFieldTests
{
    [Fact]
    public void DiscreteFieldCapturesTreeAndTransformForItsLifetimeAndCopies()
    {
        var grid = new Grid<Vec3d>(new Vec3d(1)); grid.Tree.SetValue(Coord.Zero, new(3, 4, 5));
        var oldTree = grid.Tree; var oldTransform = grid.Transform;
        var field = new DiscreteField<Vec3d>(grid, SamplingOrder.Point);
        grid.SetTree(new VdbTree<Vec3d>(new(100))); grid.Transform = Transform.CreateLinearTransform(2);
        var copy = field.Copy();
        Assert.Same(oldTransform, copy.Transform); Assert.Equal(new Vec3d(3, 4, 5), field.Sample(Vec3d.Zero)); Assert.Equal(field.Sample(Vec3d.Zero), copy.Sample(Vec3d.Zero));
        oldTransform.PostTranslate(new(2, 0, 0)); oldTree.SetValue(Coord.Zero, new(6, 7, 8));
        Assert.Equal(new Vec3d(6, 7, 8), field.Sample(new Vec3d(2, 0, 0))); Assert.Equal(new Vec3d(1), field.Sample(Vec3d.Zero));
        Assert.Equal(new Vec3d(6, 7, 8), copy.Sample(Coord.Zero));
        Assert.Equal(field.Sample(new Vec3d(2, 0, 0), double.NaN), field.Sample(new Vec3d(2, 0, 0), double.PositiveInfinity));
    }
    [Fact]
    public void VelocitySamplerUsesLiveGridTransformButCopiesCaptureTheCurrentTree()
    {
        var grid = new Grid<Vec3d>(new Vec3d(1)); grid.Tree.SetValue(Coord.Zero, new(3));
        var sampler = new VelocitySampler<Vec3d>(grid, SamplingOrder.Point); var integrator = new VelocityIntegrator<Vec3d>(grid, SamplingOrder.Point);
        grid.Transform = Transform.CreateLinearTransform(2); grid.Transform.PostTranslate(new(4, 0, 0));
        Assert.Equal(new Vec3d(3), sampler.Sample(new Vec3d(4, 0, 0)));
        grid.SetTree(new VdbTree<Vec3d>(new(7)));
        Assert.Equal(new Vec3d(3), sampler.Sample(new Vec3d(4, 0, 0))); Assert.Equal(new Vec3d(7), sampler.Copy().Sample(new Vec3d(4, 0, 0)));
        Assert.Equal(new Vec3d(7, 3, 3), integrator.RungeKutta(1, new Vec3d(4, 0, 0), 1));
        Assert.Equal(new Vec3d(11, 7, 7), integrator.Copy().RungeKutta(1, new Vec3d(4, 0, 0), 1));
    }
    [Theory]
    [InlineData(SamplingOrder.Point)]
    [InlineData(SamplingOrder.Box)]
    [InlineData(SamplingOrder.Quadratic)]
    public void IntegerDiscreteSamplingBypassesStaggeredOffsetsAndInterpolation(SamplingOrder order)
    {
        var grid = new Grid<Vec3f>(new Vec3f(2)); grid.Tree.SetValue(Coord.Zero, new(9, 8, 7));
        var field = new DiscreteField<Vec3f>(grid, order, true);
        Assert.Equal(new Vec3f(9, 8, 7), field.Sample(Coord.Zero));
        Assert.Equal(new Vec3d(9, 8, 7), ((IVelocityField)field).Sample(Coord.Zero, 42));
        Assert.Equal(field.Sample(Vec3d.Zero).AsVec3d(), ((IVelocityField)field).Sample(Vec3d.Zero, 0));
    }
    [Fact]
    public void StaggeredSamplerActivityIsTheUnionOfComponentSupports()
    {
        var grid = new Grid<Vec3d>(); grid.Tree.SetValue(new(1, 0, 0), new(4, 5, 6));
        var staggered = new VelocitySampler<Vec3d>(grid, SamplingOrder.Point, true);
        Assert.True(staggered.Sample(Vec3d.Zero, out var value)); Assert.Equal(new Vec3d(4, 0, 0), value);
        grid.Tree.SetValueOff(new(1, 0, 0)); Assert.False(staggered.Sample(Vec3d.Zero, out value)); Assert.Equal(new Vec3d(4, 0, 0), value);
    }
    [Fact]
    public void EnrightTransformCopiesAndPrecisionAreExplicit()
    {
        var field = new EnrightField<float>(); var transform = field.Transform; transform.PostTranslate(Vec3d.One);
        Assert.True(field.Transform.IsIdentity); Assert.True(field.IsSinglePrecision); Assert.False(new EnrightField<double>().IsSinglePrecision);
        Vec3d point = new(0.371828192879, 0.193912728912, 0.697983913987); double time = 0.181732837391;
        Vec3d value = field.Sample(point, time); Assert.Equal(value, ((Vec3f)value).AsVec3d());
        Assert.Equal(value, field.Sample(((Vec3f)point).AsVec3d(), (float)time)); Assert.Equal(value, field.Copy().Sample(point, time));
    }
    [Fact]
    public void EnrightAnalyticVelocityIsDivergenceFreeAndTimePeriodic()
    {
        var field = new EnrightField<double>(); var random = new Random(1539); const double h = 1e-5;
        for (int i = 0; i < 100; ++i)
        {
            Vec3d p = new(random.NextDouble(), random.NextDouble(), random.NextDouble());
            Vec3d v = field.Sample(p, 0.2); Assert.True((field.Sample(p, 6.2) - v).Length < 3e-15); Assert.True((field.Sample(p, 3.2) + v).Length < 3e-15);
            double divergence = (field.Sample(p + h * Vec3d.UnitX).X - field.Sample(p - h * Vec3d.UnitX).X + field.Sample(p + h * Vec3d.UnitY).Y - field.Sample(p - h * Vec3d.UnitY).Y + field.Sample(p + h * Vec3d.UnitZ).Z - field.Sample(p - h * Vec3d.UnitZ).Z) / (2 * h);
            Assert.True(System.Math.Abs(divergence) < 2e-9);
        }
        Assert.True(field.Sample(new Vec3d(0.31, 0.47, 0.62), 1.5).Length < 1e-15);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void ConstantVelocityIntegratesForwardBackwardAndInPlace(int order)
    {
        var velocity = new Vec3d(0.25, -0.125, 0.0625); var grid = new Grid<Vec3d>(velocity); var integrator = new VelocityIntegrator<Vec3d>(grid);
        Vec3d position = new(0.371, -3.5, 16777216.125), expected = order == 0 ? position : position + velocity * 0.125;
        Assert.Equal(expected, integrator.RungeKutta(0.125, position, order));
        Vec3d copy = position; integrator.RungeKutta(0.125, ref copy, order); Assert.Equal(expected, copy);
        integrator.RungeKutta(-0.125, ref copy, order); Assert.Equal(position, copy);
        Vec3d[] positions = [position, position]; integrator.RungeKutta(0.125, positions.AsSpan(), order); Assert.All(positions, x => Assert.Equal(expected, x));
        Vec3f[] floatPositions = [(Vec3f)position]; integrator.RungeKutta(0.125, floatPositions.AsSpan(), order); Assert.Equal(integrator.RungeKutta(0.125, (Vec3f)position, order), floatPositions[0]);
    }
    [Fact]
    public void FloatGridRoundsIntermediatePositionsAndDeltaWithoutDiscardingOriginalDoubleLocation()
    {
        var grid = new Grid<Vec3f>(new Vec3f(0.125f)); var integrator = new VelocityIntegrator<Vec3f>(grid, SamplingOrder.Point);
        Vec3d position = new(16777216.25, -16777216.75, 0.371928391923); double dt = 0.123981273198;
        Vec3d expected = position + new Vec3d((float)dt * 0.125f); Assert.Equal(expected, integrator.RungeKutta(dt, position, 1));
        var linear = new Grid<Vec3f>(); linear.Tree.SetValue(Coord.Zero, new(1)); linear.Tree.SetValue(new Coord(1, 0, 0), new(2));
        var pointIntegrator = new VelocityIntegrator<Vec3f>(linear, SamplingOrder.Point);
        Vec3d nearHalf = new(0.4999999999, 0, 0); Assert.Equal(nearHalf + new Vec3d(0.5), pointIntegrator.RungeKutta(0.25, nearHalf, 1));
    }
    [Fact]
    public void DoubleGridRoundsTheDeltaBeforeAddingToFloatLocations()
    {
        var grid = new Grid<Vec3d>(new Vec3d(1.0000000596046448)); var integrator = new VelocityIntegrator<Vec3d>(grid, SamplingOrder.Point);
        Vec3f p = new(0.1234567f, -10000, 3.75f); double dt = 1.000000031;
        Assert.Equal(p + new Vec3f((float)(dt * grid.Background.X)), integrator.RungeKutta(dt, p, 1));
    }
    [Fact]
    public void HigherRungeKuttaOrdersConvergeOnAnAnalyticLinearField()
    {
        var grid = new Grid<Vec3d>(); for (int x = -3; x <= 3; ++x) for (int y = -3; y <= 3; ++y) for (int z = -3; z <= 3; ++z) grid.Tree.SetValue(new(x, y, z), new(x, -y, 0.5 * z));
        var integrator = new VelocityIntegrator<Vec3d>(grid); Vec3d initial = new(0.3, 0.5, 0.7); const double dt = 0.2;
        Vec3d exact = new(initial.X * System.Math.Exp(dt), initial.Y * System.Math.Exp(-dt), initial.Z * System.Math.Exp(0.5 * dt)); double previous = double.PositiveInfinity;
        for (int order = 1; order <= 4; ++order) { double error = (integrator.RungeKutta(dt, initial, order) - exact).Length; Assert.True(error < previous / 8); previous = error; }
    }
    [Fact]
    public void InvalidArgumentsFailBeforeUpdatingLocations()
    {
        Assert.Throws<NotSupportedException>(() => new VelocitySampler<Vec3i>(new Grid<Vec3i>())); Assert.Throws<NotSupportedException>(() => new DiscreteField<int>(new Grid<int>())); Assert.Throws<NotSupportedException>(() => new EnrightField<Half>());
        Assert.Throws<ArgumentNullException>(() => new VelocitySampler<Vec3d>((Grid<Vec3d>)null!)); Assert.Throws<ArgumentNullException>(() => new DiscreteField<Vec3d>((Grid<Vec3d>)null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new VelocityIntegrator<Vec3d>(new(), (SamplingOrder)5));
        var integrator = new VelocityIntegrator<Vec3d>(new Grid<Vec3d>()); var position = Vec3d.One;
        Assert.Throws<ArgumentOutOfRangeException>(() => integrator.RungeKutta(1, ref position, -1)); Assert.Equal(Vec3d.One, position);
        Vec3d[] positions = [position]; Assert.Throws<ArgumentOutOfRangeException>(() => integrator.RungeKutta(1, positions.AsSpan(), 5)); Assert.Equal(Vec3d.One, positions[0]);
        Vec3d nan = new(double.NaN, double.PositiveInfinity, double.NegativeInfinity); var unchanged = integrator.RungeKutta(double.NaN, nan, 0); Assert.True(double.IsNaN(unchanged.X)); Assert.Equal(nan.Y, unchanged.Y); Assert.Equal(nan.Z, unchanged.Z);
    }
    [Fact]
    public void SamplingAndIntegrationReuseAccessorsWithoutPerCallAllocation()
    {
        var grid = new Grid<Vec3f>(new Vec3f(0.125f)); var sampler = new VelocitySampler<Vec3f>(grid); var field = new DiscreteField<Vec3f>(grid); var enright = new EnrightField<float>(); var integrator = new VelocityIntegrator<Vec3f>(grid);
        Vec3d p = new(0.31, 0.27, 0.73); Vec3d value = default;
        for (int i = 0; i < 3000; ++i) { value = sampler.Sample(p); value += field.Sample(p).AsVec3d(); value += enright.Sample(p); value += integrator.RungeKutta(0.01, p); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 2000; ++i) { value = sampler.Sample(p); value += field.Sample(p).AsVec3d(); value += enright.Sample(p); value += integrator.RungeKutta(0.01, p); }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated); Assert.True(value.IsFinite);
    }
}
