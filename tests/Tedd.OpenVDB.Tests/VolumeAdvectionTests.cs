using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tests;

public class VolumeAdvectionTests
{
    public static IEnumerable<object[]> Settings() { for (int scheme = 0; scheme < 6; ++scheme) for (int limiter = 0; limiter < 3; ++limiter) yield return [scheme, limiter]; }
    [Theory]
    [MemberData(nameof(Settings))]
    public void NativeSettingsReportSpatialTemporalAndLimiterOrders(int scheme, int limiter)
    {
        var solver = new VolumeAdvection<Vec3f>(new Grid<Vec3f>(new Vec3f(1, 0, 0))) { Scheme = (VolumeAdvectionScheme)scheme, Limiter = (VolumeAdvectionLimiter)limiter };
        Assert.Equal(scheme >= 4 ? 2 : 1, solver.SpatialOrder); Assert.Equal(scheme < 4 ? scheme + 1 : 2, solver.TemporalOrder); Assert.Equal(scheme >= 4 && limiter != 0, solver.IsLimiterOn);
        Assert.Equal(128, solver.GrainSize); Assert.Equal(1, solver.SubSteps); Assert.Equal(1, solver.MaxVelocity); solver.SubSteps = -7; Assert.Equal(1, solver.SubSteps);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void ConstantVelocityTranslatesAnInteriorLinearPolynomialInEitherDirection(int scheme)
    {
        var grid = new Grid<double>(); for (int x = -6; x <= 6; ++x) for (int y = -3; y <= 3; ++y) for (int z = -3; z <= 3; ++z) grid.Tree.SetValue(new(x, y, z), x + 2 * y - z);
        var solver = new VolumeAdvection<Vec3d>(new Grid<Vec3d>(new Vec3d(0.25, 0, 0))) { Scheme = (VolumeAdvectionScheme)scheme, GrainSize = 0 };
        Assert.Equal(-0.0625, solver.Advect(grid, 0.25).Tree.GetValue(Coord.Zero)); Assert.Equal(0.0625, solver.Advect(grid, -0.25).Tree.GetValue(Coord.Zero)); Assert.Equal(0, grid.Tree.GetValue(Coord.Zero));
    }
    [Fact]
    public void InputsRemainIndependentAndOutputMetadataAndTransformAreDeepCopied()
    {
        var input = VolumeAdvectionTestSystems.Shape<double>(0, 1); input.SetMetadata("payload", "immutable"); var before = input.DeepCopy(); var mask = VolumeAdvectionTestSystems.Mask(1, 1); var maskBefore = mask.DeepCopy(); var velocity = VolumeAdvectionTestSystems.Velocity<Vec3d>(1); var velocityBefore = velocity.DeepCopy();
        var output = new VolumeAdvection<Vec3d>(velocity) { Scheme = VolumeAdvectionScheme.Bfecc, GrainSize = 0, SubSteps = 2 }.Advect(input, mask, 0.25);
        Equal(before.Tree, input.Tree); Equal(maskBefore.Tree, mask.Tree); Equal(velocityBefore.Tree, velocity.Tree);
        Assert.Equal("transport", output.Name); Assert.Equal(GridClass.FogVolume, output.GridClass); Assert.Equal("immutable", output.GetMetadata<string>("payload"));
        output.Name = "changed"; output.SetMetadata("payload", "other"); output.Transform.PostTranslate(Vec3d.One);
        Assert.Equal("transport", input.Name); Assert.Equal("immutable", input.GetMetadata<string>("payload")); Assert.True(input.Transform.IsEquivalentTo(before.Transform));
    }
    [Fact]
    public void MaskUsesActivityAndRestoresOriginalTopologyAfterPruningExcludedLeaves()
    {
        var input = new Grid<float>(); input.Tree.SetValue(Coord.Zero, 4); input.Tree.SetValue(new(16, 0, 0), 9); var mask = new Grid<bool>(); mask.Tree.SetValue(Coord.Zero, false);
        var output = new VolumeAdvection<Vec3f>(new Grid<Vec3f>(Vec3f.Zero)) { GrainSize = 0 }.Advect(input, mask, 0);
        Assert.Equal(4, output.Tree.GetValue(Coord.Zero)); Assert.True(output.Tree.IsValueOn(new(16, 0, 0))); Assert.Equal(0, output.Tree.GetValue(new(16, 0, 0))); Assert.Equal(9, input.Tree.GetValue(new(16, 0, 0)));
        var bitMask = new MaskGrid(); bitMask.SetValueOn(Coord.Zero); Equal(output.Tree, new VolumeAdvection<Vec3f>(new Grid<Vec3f>(Vec3f.Zero)).Advect(input, bitMask, 0).Tree);
    }
    [Fact]
    public void ZeroDistanceRetainsActiveTilesAndPositiveDistanceVoxelizesThem()
    {
        var grid = new Grid<float>(); grid.Tree.AddTile(1, new(16, 0, 0), 0, true);
        var solver = new VolumeAdvection<Vec3f>(new Grid<Vec3f>(new Vec3f(1, 0, 0))) { GrainSize = 0 };
        var zero = solver.Advect(grid, 0); Assert.Equal(1UL, zero.Tree.ActiveTileCount); Assert.Equal(512UL, zero.ActiveVoxelCount);
        var moved = solver.Advect(grid, 0.1); Assert.True(moved.IsEmpty); Assert.Equal(0UL, moved.Tree.ActiveTileCount); Assert.Equal(1UL, grid.Tree.ActiveTileCount);
    }
    [Fact]
    public void ScalarBackgroundDeltaAndNaNComparisonMatchNative()
    {
        var solver = new VolumeAdvection<Vec3f>(new Grid<Vec3f>(Vec3f.Zero)) { GrainSize = 0 }; var single = new Grid<float>(); single.Tree.SetValue(Coord.Zero, 0.5e-5f); single.Tree.SetValue(new(1, 0, 0), 2e-5f); single.Tree.SetValue(new(2, 0, 0), float.NaN);
        var result = solver.Advect(single, 0, SamplingOrder.Point); Assert.False(result.Tree.IsValueOn(Coord.Zero)); Assert.True(result.Tree.IsValueOn(new(1, 0, 0))); Assert.False(result.Tree.IsValueOn(new(2, 0, 0)));
        var d = new Grid<double>(); d.Tree.SetValue(Coord.Zero, 0.5e-9); d.Tree.SetValue(new(1, 0, 0), 2e-9); var rd = solver.Advect(d, 0, SamplingOrder.Point); Assert.False(rd.Tree.IsValueOn(Coord.Zero)); Assert.True(rd.Tree.IsValueOn(new(1, 0, 0)));
        var half = new Grid<Half>(); half.Tree.SetValue(Coord.Zero, (Half)0.002); half.Tree.SetValue(new(1, 0, 0), (Half)0.008); var rh = solver.Advect(half, 0, SamplingOrder.Point); Assert.False(rh.Tree.IsValueOn(Coord.Zero)); Assert.True(rh.Tree.IsValueOn(new(1, 0, 0)));
    }
    [Fact]
    public void VectorBackgroundDeltaIsZeroEvenForFloatComponents()
    {
        var grid = new Grid<Vec3f>(Vec3f.Zero); grid.Tree.SetValue(Coord.Zero, new Vec3f(1e-12f, 0, 0)); grid.Tree.SetValue(new(1, 0, 0), Vec3f.Zero);
        var result = new VolumeAdvection<Vec3f>(new Grid<Vec3f>(Vec3f.Zero)).Advect(grid, 0, SamplingOrder.Point); Assert.True(result.Tree.IsValueOn(Coord.Zero)); Assert.False(result.Tree.IsValueOn(new(1, 0, 0)));
    }
    [Fact]
    public void MaximumVelocityIncludesBackgroundInactiveValuesAndTilesAndIsCached()
    {
        var velocity = new Grid<Vec3f>(new Vec3f(3, 4, 0)); velocity.Tree.SetValueOff(Coord.Zero, new Vec3f(0, 0, 6)); velocity.Tree.AddTile(1, new(16, 0, 0), new Vec3f(0, 8, 0), false);
        var solver = new VolumeAdvection<Vec3f>(velocity); Assert.Equal(8, solver.MaxVelocity); var grid = new Grid<double> { Transform = new(Mat4d.CreateScale(0.5)) }; Assert.Equal(2, solver.GetMaxDistance(grid, -0.0626));
        velocity.Tree.SetValue(Coord.Zero, new Vec3f(100)); Assert.Equal(8, solver.MaxVelocity); Assert.True(new VolumeAdvection<Vec3f>(velocity).MaxVelocity > 100);
    }
    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    public void ParallelStagesMatchSerialIncludingAuxiliaryBuffersAndLimiters(int scheme)
    {
        var input = VolumeAdvectionTestSystems.Shape<Vec3f>(1, 1); var velocity = VolumeAdvectionTestSystems.Velocity<Vec3f>(1);
        var solver = new VolumeAdvection<Vec3f>(velocity, true) { Scheme = (VolumeAdvectionScheme)scheme, SubSteps = 2, GrainSize = 0 }; var serial = solver.Advect(input, 0.2); solver.GrainSize = 1; Equal(serial.Tree, solver.Advect(input, 0.2).Tree);
    }
    [Fact]
    public void InvalidArgumentsBudgetsAndCancellationLeaveInputsUnchanged()
    {
        var input = VolumeAdvectionTestSystems.Shape<float>(1, 0); var copy = input.DeepCopy(); var solver = new VolumeAdvection<Vec3f>(new Grid<Vec3f>(new Vec3f(1, 0, 0))) { MaximumDilation = 0 };
        Assert.Throws<InvalidOperationException>(() => solver.Advect(input, 1)); solver.MaximumDilation = int.MaxValue; solver.MaximumVoxelizedLeaves = 0; Assert.Throws<InvalidOperationException>(() => solver.Advect(input, 1));
        Assert.Throws<OperationCanceledException>(() => solver.Advect(input, 1, cancellationToken: new CancellationToken(true))); Equal(copy.Tree, input.Tree);
        solver.Scheme = (VolumeAdvectionScheme)99; Assert.Equal(0, solver.TemporalOrder); Assert.Throws<ArgumentOutOfRangeException>(() => solver.Advect(input, 0)); solver.Scheme = 0;
        solver.Limiter = (VolumeAdvectionLimiter)99; Assert.Throws<ArgumentOutOfRangeException>(() => solver.Advect(input, 0)); solver.Limiter = 0;
        solver.GrainSize = -1; Assert.Throws<ArgumentOutOfRangeException>(() => solver.Advect(input, 0)); solver.GrainSize = 0;
        Assert.Throws<ArgumentOutOfRangeException>(() => solver.GetMaxDistance(input, double.NaN)); Assert.Throws<ArithmeticException>(() => solver.GetMaxDistance(input, double.MaxValue));
        input.Transform = new(Mat4d.CreateScale(new Vec3d(1, 2, 1))); Assert.Throws<ArgumentException>(() => solver.GetMaxDistance(input, 0));
        Assert.Throws<NotSupportedException>(() => new VolumeAdvection<float>(new Grid<float>())); Assert.Throws<NotSupportedException>(() => solver.Advect(new Grid<decimal>(), 0));
        input.Transform = new(); var mask = new Grid<bool> { Transform = new(Mat4d.CreateTranslation(Vec3d.One)) }; Assert.Throws<ArgumentException>(() => solver.Advect(input, mask, 0));
    }
    [Fact]
    public void NonfiniteVelocityDistanceAndOutOfDomainTracingRejectBeforeReturningOutput()
    {
        var input = new Grid<double>(); input.Tree.SetValue(new(int.MaxValue, 0, 0), 7); var original = input.DeepCopy();
        var solver = new VolumeAdvection<Vec3d>(new Grid<Vec3d>(new Vec3d(-1, 0, 0))) { GrainSize = 0 };
        Assert.ThrowsAny<ArgumentException>(() => solver.Advect(input, 1, SamplingOrder.Point)); Equal(original.Tree, input.Tree);
        foreach (double bad in new[] { double.NaN, double.PositiveInfinity }) Assert.Throws<ArithmeticException>(() => new VolumeAdvection<Vec3d>(new Grid<Vec3d>(new Vec3d(bad))).GetMaxDistance(input, 1));
    }
    private static void Equal<T>(VdbTree<T> first, VdbTree<T> second) where T : unmanaged { Assert.True(first.HasSameTopology(second)); Assert.Equal(first.EnumerateValues(ValueIterationMode.All).ToArray(), second.EnumerateValues(ValueIterationMode.All).ToArray()); }
}

internal static class VolumeAdvectionTestSystems
{
    internal static Transform Transform(int kind)
    {
        var transform = new Transform(Mat4d.Identity); if (kind != 0) { transform.PreScale(0.75); transform.PostTranslate(new(0.375, -0.625, 0.125)); }
        if (kind == 2) transform.PreRotate(0.27, Axis.Z); return new Transform(transform.Matrix);
    }
    internal static T Value<T>(double a, double b = 0, double c = 0, double d = 0) where T : unmanaged
    {
        object value = typeof(T) == typeof(float) ? (object)(float)a : typeof(T) == typeof(double) ? a : typeof(T) == typeof(Half) ? (Half)(float)a : typeof(T) == typeof(int) ? (int)a : typeof(T) == typeof(Vec3f) ? new Vec3f((float)a, (float)b, (float)c) : typeof(T) == typeof(Vec3i) ? new Vec3i((int)a, (int)b, (int)c) : typeof(T) == typeof(Vec3d) ? new Vec3d(a, b, c) : typeof(T) == typeof(Vec2d) ? new Vec2d(a, b) : typeof(T) == typeof(Vec4d) ? new Vec4d(a, b, c, d) : throw new NotSupportedException(); return (T)value;
    }
    internal static double[] Components<T>(T value) where T : unmanaged => value switch { float x => [x], double x => [x], Half x => [(double)x], int x => [x], Vec2d x => [x.X, x.Y], Vec3d x => [x.X, x.Y, x.Z], Vec3f x => [x.X, x.Y, x.Z], Vec3i x => [x.X, x.Y, x.Z], Vec4d x => [x.X, x.Y, x.Z, x.W], _ => throw new NotSupportedException() };
    internal static Grid<T> Shape<T>(int shape, int map) where T : unmanaged
    {
        var grid = new Grid<T>(Value<T>(-0.25, 0.5, -0.125, 0.75)) { Transform = Transform(map), Name = "transport", GridClass = GridClass.FogVolume };
        if (shape != 2)
        {
            for (int x = -2; x <= 2; ++x) for (int y = -2; y <= 2; ++y) for (int z = -2; z <= 2; ++z)
            {
                T value = Value<T>(1.25 + 0.1875 * x - 0.125 * y + 0.0625 * z + 0.375 * ((x * y + z) % 3), -0.75 - 0.125 * x + 0.25 * y + 0.03125 * z, 0.5 + 0.0625 * x * y - 0.1875 * z, 1 + 0.125 * y * z); Coord coordinate = new(x, y, z);
                if ((x + 2 * y + 3 * z) % 5 == 0) grid.Tree.SetValueOff(coordinate, value); else grid.Tree.SetValue(coordinate, value);
            }
            grid.Tree.SetValueOff(new(33, 0, 0), Value<T>(-7, 2, 3, 4));
        }
        if (shape == 3) { grid.Tree.SetValue(Coord.Zero, Value<T>(double.NaN, 2, -3, 4)); grid.Tree.SetValue(new(1, 0, 0), Value<T>(-0.25, double.NaN, 2, 4)); grid.Tree.SetValue(new(2, 0, 0), Value<T>(1, double.NaN, 2, 4)); }
        if (shape == 1) grid.Tree.AddTile(1, new(16, 0, 0), Value<T>(2, 2, 2, 2), true); return grid;
    }
    internal static Grid<V> Velocity<V>(int kind) where V : unmanaged
    {
        var grid = new Grid<V>(kind == 2 ? Value<V>(0) : Value<V>(0.625, -0.375, 0.25)) { Transform = Transform(0) };
        if (kind == 1)
        {
            for (int x = -8; x <= 8; ++x) for (int y = -8; y <= 8; ++y) for (int z = -8; z <= 8; ++z) grid.Tree.SetValue(new(x, y, z), Value<V>(0.375 + 0.015625 * x + 0.0078125 * y, -0.25 + 0.015625 * y - 0.0078125 * z, 0.125 + 0.0078125 * x * z));
            grid.Tree.SetValueOff(new(17, 1, 1), Value<V>(1.25, -0.5, 0.75));
        }
        return grid;
    }
    internal static Grid<bool> Mask(int kind, int map)
    {
        var grid = new Grid<bool> { Transform = Transform(map) }; for (int x = kind == 1 ? -1 : 0; x <= 4; ++x) for (int y = -4; y <= 4; ++y) for (int z = -4; z <= 4; ++z) grid.Tree.SetValue(new(x, y, z), false); return grid;
    }
}
