using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tests;

public class LevelSetAdvectionTests
{
    [Fact]
    public void DefaultsAndIndependentTrackerSettingsMatchNative()
    {
        var advection = new LevelSetAdvection<double>(Shape());
        Assert.Equal(BiasedGradientScheme.HJWeno5, advection.SpatialScheme); Assert.Equal(TemporalIntegrationScheme.TvdRungeKutta2, advection.TemporalScheme);
        Assert.Equal(BiasedGradientScheme.HJWeno5, advection.TrackerSpatialScheme); Assert.Equal(TemporalIntegrationScheme.TvdRungeKutta1, advection.TrackerTemporalScheme); Assert.Equal(3, advection.NormalizationCount); Assert.Equal(1, advection.GrainSize); Assert.Equal(0.75, advection.VoxelSize);
        advection.SpatialScheme = BiasedGradientScheme.First; advection.TemporalScheme = TemporalIntegrationScheme.TvdRungeKutta3;
        Assert.Equal(BiasedGradientScheme.HJWeno5, advection.TrackerSpatialScheme); Assert.Equal(TemporalIntegrationScheme.TvdRungeKutta1, advection.TrackerTemporalScheme);
    }
    public static IEnumerable<object[]> Schemes() { for (int scheme = 0; scheme < 5; ++scheme) for (int rk = 0; rk < 3; ++rk) yield return [scheme, rk]; }
    [Theory]
    [MemberData(nameof(Schemes))]
    public void LinearLevelSetTranslatesWithNativeDifferencesAndTemporalStages(int spatial, int temporal)
    {
        var grid = new Grid<double>(20) { GridClass = GridClass.LevelSet, Transform = Transform.CreateLinearTransform(0.5) };
        // Two transports with three radius-three stages require eighteen voxels of unperturbed X support.
        for (int x = -24; x <= 24; ++x) for (int y = -3; y <= 3; ++y) for (int z = -3; z <= 3; ++z) grid.Tree.SetValue(new(x, y, z), x * 0.5);
        var field = new CountingField(new Vec3d(0.125, 0, 0), grid.Transform);
        var solver = new LevelSetAdvection<double>(grid, field) { SpatialScheme = (BiasedGradientScheme)spatial, TemporalScheme = (TemporalIntegrationScheme)temporal, NormalizationCount = 0, GrainSize = 0 };
        Assert.Equal(1UL, solver.Advect(0, 0.25)); Assert.InRange(System.Math.Abs(grid.Tree.GetValue(Coord.Zero) + 0.03125), 0, 1e-14);
        Assert.Equal(1UL, solver.Advect(0.25, 0)); Assert.InRange(System.Math.Abs(grid.Tree.GetValue(Coord.Zero)), 0, 1e-13);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TransformAlignmentSelectsDirectCoordinateOrWorldQueries(bool aligned)
    {
        var grid = Shape(); var field = new CountingField(new Vec3d(0.5, 0, 0), aligned ? grid.Transform.Copy() : new Transform());
        var solver = new LevelSetAdvection<double>(grid, field) { NormalizationCount = 0, GrainSize = 0 };
        Assert.Equal(1, field.Counters.Copies); solver.Advect(0, 0.01);
        Assert.True(field.Counters.Copies > 1); Assert.True(aligned ? field.Counters.Coordinates > 0 && field.Counters.World == 0 : field.Counters.World > 0 && field.Counters.Coordinates == 0);
    }
    [Fact]
    public void VelocityIsResampledOncePerCflStepAtCurrentTime()
    {
        var grid = Shape(); var field = new CountingField(new Vec3d(4, 0, 0), grid.Transform);
        var solver = new LevelSetAdvection<double>(grid, field) { NormalizationCount = 0, TemporalScheme = TemporalIntegrationScheme.TvdRungeKutta3, GrainSize = 0 };
        ulong steps = solver.Advect(0.125, 0.4); Assert.True(steps > 1);
        Assert.Equal((int)steps, field.Counters.Times.Count); Assert.Equal(0.125, field.Counters.Times.Min()); Assert.True(field.Counters.Times.Max() < 0.4);
    }
    [Fact]
    public void NoOpAndZeroVelocityDoNotTrackOrChangeStoredTopology()
    {
        var grid = Shape(); var original = grid.Tree.DeepCopy(); var zero = new CountingField(Vec3d.Zero, grid.Transform); var solver = new LevelSetAdvection<double>(grid, zero);
        Assert.Equal(0UL, solver.Advect(3, 3)); Assert.Equal(0, zero.Counters.Coordinates);
        Assert.Equal(0UL, solver.Advect(0, 100)); EqualTree(original, grid.Tree);
        var empty = new Grid<double>(3) { GridClass = GridClass.LevelSet }; Assert.Equal(0UL, new LevelSetAdvection<double>(empty).Advect(0, 1));
    }
    [Fact]
    public void NativeVelocityDeltaCutoffDependsOnGridPrecision()
    {
        var floatGrid = LevelSetSphere.Create<float>(3.25f, new(-0.25, 0.5, 0.25), 0.75f, 3f); var doubleGrid = Shape();
        var field = new CountingField(new Vec3d(1e-4), new Transform(), true);
        var floatSolver = new LevelSetAdvection<float>(floatGrid, field) { NormalizationCount = 0, GrainSize = 0 };
        var doubleSolver = new LevelSetAdvection<double>(doubleGrid, field) { NormalizationCount = 0, GrainSize = 0 };
        Assert.Equal(0UL, floatSolver.Advect(0, 0.125f)); Assert.Equal(1UL, doubleSolver.Advect(0, 0.125));
    }
    [Fact]
    public void NativeNanAndInfiniteVelocitiesCauseZeroCflSteps()
    {
        foreach (double component in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            var grid = Shape(); var original = grid.Tree.DeepCopy(); var solver = new LevelSetAdvection<double>(grid, new CountingField(new Vec3d(component), grid.Transform));
            Assert.Equal(0UL, solver.Advect(0, 0.1)); EqualTree(original, grid.Tree);
        }
    }
    [Fact]
    public void BudgetsAndInvalidMapsRejectBeforeChangingTheGrid()
    {
        var grid = Shape(); var original = grid.Tree.DeepCopy(); var solver = new LevelSetAdvection<double>(grid) { MaximumActiveVoxels = 0 };
        Assert.Throws<InvalidOperationException>(() => solver.Advect(0, 0.1)); EqualTree(original, grid.Tree);
        solver.MaximumActiveVoxels = -1; Assert.Throws<ArgumentOutOfRangeException>(() => solver.Advect(0, 0.1));
        grid.Transform.PostRotate(0.3, Axis.Z); solver.MaximumActiveVoxels = int.MaxValue;
        Assert.Throws<ArgumentException>(() => solver.Advect(0, 0.1)); EqualTree(original, grid.Tree);
        grid.Transform = new(Mat4d.CreateRotation(0.3, Axis.Z)); grid.Transform.PostTranslate(Vec3d.One);
        Assert.Throws<ArgumentException>(() => solver.Advect(0, 0.1)); EqualTree(original, grid.Tree);
        grid.Transform = Transform.CreateFrustumTransform(new BBoxd(new Vec3d(-8), new Vec3d(8)), 0.5, 4);
        Assert.Throws<ArgumentException>(() => solver.Advect(0, 0.1));
    }
    [Fact]
    public void ActiveTilesAndInvalidSchemesAndTimesRejectBeforeMutation()
    {
        var grid = Shape(); var solver = new LevelSetAdvection<double>(grid);
        solver.SpatialScheme = BiasedGradientScheme.Unknown; Assert.Throws<ArgumentOutOfRangeException>(() => solver.Advect(0, 0)); solver.SpatialScheme = BiasedGradientScheme.First;
        solver.TemporalScheme = TemporalIntegrationScheme.Unknown; Assert.Throws<ArgumentOutOfRangeException>(() => solver.Advect(0, 0)); solver.TemporalScheme = TemporalIntegrationScheme.TvdRungeKutta1;
        solver.TrackerSpatialScheme = BiasedGradientScheme.Unknown; Assert.Throws<ArgumentOutOfRangeException>(() => solver.Advect(0, 0)); solver.TrackerSpatialScheme = BiasedGradientScheme.First;
        solver.TrackerTemporalScheme = TemporalIntegrationScheme.Unknown; Assert.Throws<ArgumentOutOfRangeException>(() => solver.Advect(0, 0)); solver.TrackerTemporalScheme = TemporalIntegrationScheme.TvdRungeKutta1;
        Assert.Throws<ArgumentOutOfRangeException>(() => solver.Advect(double.NaN, 0)); Assert.Throws<ArgumentOutOfRangeException>(() => solver.Advect(0, double.PositiveInfinity)); Assert.Throws<ArgumentOutOfRangeException>(() => solver.Advect(-double.MaxValue, double.MaxValue));
        grid.Tree.AddTile(1, new(32, 0, 0), 1, true); var original = grid.Tree.DeepCopy(); Assert.Throws<ArgumentException>(() => solver.Advect(0, 1)); EqualTree(original, grid.Tree);
    }
    [Fact]
    public void NonAdvancingFloatTimeThrowsBeforePublishingTheFirstStage()
    {
        var grid = LevelSetSphere.Create<float>(3.25f, new(-0.25, 0.5, 0.25), 0.75f, 3f); var original = grid.Tree.DeepCopy();
        var solver = new LevelSetAdvection<float>(grid, new CountingField(new(500, 0, 0), grid.Transform, true));
        Assert.Throws<ArithmeticException>(() => solver.Advect(100000000f, 100000008f)); EqualTree(original, grid.Tree);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void CancellationDuringFieldSamplingLeavesTheFirstStageUnpublished(int grainSize)
    {
        var grid = Shape(); var original = grid.Tree.DeepCopy(); using var cancellation = new CancellationTokenSource();
        var field = new CountingField(new Vec3d(1, 0, 0), grid.Transform) { CancelSource = cancellation };
        var solver = new LevelSetAdvection<double>(grid, field) { GrainSize = grainSize };
        Assert.ThrowsAny<OperationCanceledException>(() => solver.Advect(0, 0.1, cancellation.Token)); EqualTree(original, grid.Tree);
        Assert.Throws<OperationCanceledException>(() => solver.Advect(0, 0.1, new CancellationToken(true))); EqualTree(original, grid.Tree);
    }
    [Fact]
    public void ParallelStagesMatchSerialStagesAndRebindReplacedTrees()
    {
        var serial = Shape(); var parallel = serial.DeepCopy(); var field = new CountingField(new Vec3d(0.5, -0.25, 0.125), serial.Transform);
        var first = new LevelSetAdvection<double>(serial, field) { GrainSize = 0, NormalizationCount = 1 }; var second = new LevelSetAdvection<double>(parallel, field) { GrainSize = 1, NormalizationCount = 1 };
        var replacement = parallel.Tree.DeepCopy(); parallel.SetTree(replacement);
        Assert.Equal(first.Advect(0, 0.125), second.Advect(0, 0.125)); EqualTree(serial.Tree, parallel.Tree);
    }
    private static Grid<double> Shape() => LevelSetSphere.Create<double>(3.25, new(-0.25, 0.5, 0.25), 0.75, 3);
    private static void EqualTree<T>(VdbTree<T> expected, VdbTree<T> actual) where T : unmanaged
    {
        Assert.True(expected.HasSameTopology(actual)); Assert.Equal(expected.Background, actual.Background); Assert.Equal(expected.EnumerateValues(ValueIterationMode.All).ToArray(), actual.EnumerateValues(ValueIterationMode.All).ToArray());
    }
    private sealed class CountingField : IVelocityField
    {
        private readonly Vec3d _velocity;
        public CountingField(Vec3d velocity, Transform transform, bool single = false) { _velocity = velocity; Transform = transform; IsSinglePrecision = single; Counters = new(); }
        private CountingField(CountingField source) { _velocity = source._velocity; Transform = source.Transform; IsSinglePrecision = source.IsSinglePrecision; Counters = source.Counters; CancelSource = source.CancelSource; }
        public Transform Transform { get; }
        public bool IsSinglePrecision { get; }
        public Counts Counters { get; }
        public CancellationTokenSource? CancelSource { get; init; }
        public Vec3d Sample(Coord index, double time) { Interlocked.Increment(ref Counters.Coordinates); return Evaluate(time); }
        public Vec3d Sample(Vec3d world, double time) { Interlocked.Increment(ref Counters.World); return Evaluate(time); }
        private Vec3d Evaluate(double time) { lock (Counters.Times) Counters.Times.Add(time); CancelSource?.Cancel(); return IsSinglePrecision ? ((Vec3f)_velocity).AsVec3d() : _velocity; }
        public IVelocityField Copy() { Interlocked.Increment(ref Counters.Copies); return new CountingField(this); }
        public sealed class Counts { public int Copies, Coordinates, World; public HashSet<double> Times { get; } = []; }
    }
}
