using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tests;

public sealed class LevelSetTrackerTests
{
    [Fact]
    public void DefaultsAndStateRoundTripMatchNative()
    {
        var grid = LevelSetTrackingConformanceTests.Shape<float>(0); var tracker = new LevelSetTracker<float>(grid);
        Assert.Equal(new LevelSetTrackerState(BiasedGradientScheme.HJWeno5, TemporalIntegrationScheme.TvdRungeKutta1, 3, 1), tracker.State);
        Assert.Equal(TrimMode.All, tracker.Trimming); Assert.Equal(0.75f, tracker.VoxelSize); Assert.Equal(3, tracker.HalfWidth);
        var state = new LevelSetTrackerState(BiasedGradientScheme.Second, TemporalIntegrationScheme.TvdRungeKutta3, 5, 0); tracker.State = state;
        Assert.Equal(state, tracker.State);
    }
    [Theory, InlineData(TrimMode.None, 7), InlineData(TrimMode.Interior, 6), InlineData(TrimMode.Exterior, 6), InlineData(TrimMode.All, 5)]
    public void TrimmingUsesInclusiveBandBoundsAndPreservesInteriorValues(TrimMode mode, int count)
    {
        var grid = new Grid<double>(3) { GridClass = GridClass.LevelSet };
        for (int i = 0; i < 7; ++i) grid.Tree.SetValueOn(new(i, 0, 0), i - 3);
        var tracker = new LevelSetTracker<double>(grid) { Trimming = mode }; tracker.Prune();
        Assert.Equal((ulong)count, grid.ActiveVoxelCount); Assert.Equal(-3, grid.Tree.GetValue(new(0))); Assert.Equal(3, grid.Tree.GetValue(new(6, 0, 0)));
    }
    [Fact]
    public void NormalizationReducesDistanceGradientErrorWithoutChangingTopology()
    {
        var grid = LevelSetSphere.Create(6.0, Vec3d.Zero, 0.5);
        foreach (var value in grid.Tree.EnumerateValueHandles()) value.SetValueOnly(value.Value * 1.2);
        var topology = grid.Tree.DeepCopy(); double before = Error(grid);
        new LevelSetTracker<double>(grid) { SpatialScheme = BiasedGradientScheme.First, NormalizationCount = 12, GrainSize = 0 }.Normalize();
        Assert.True(Error(grid) < before * 0.7); Assert.True(grid.Tree.HasSameTopology(topology));
        Assert.All(grid.Tree.EnumerateValues(), value => Assert.True(double.IsFinite(value.Value)));
        static double Error(Grid<double> source)
        {
            double sum = 0; int count = 0; var accessor = source.GetAccessor();
            foreach (var value in source.Tree.EnumerateValues()) if (value.IsVoxel && System.Math.Abs(value.Value) < 0.5)
            { sum += System.Math.Abs(DifferentialOperators.Gradient(source.Transform, accessor, value.Coordinate).Length - 1); ++count; }
            return sum / count;
        }
    }
    [Fact]
    public void ActivityMaskCanNormalizeInactiveTargetVoxelsWithoutActivatingThem()
    {
        var grid = new Grid<double>(3) { GridClass = GridClass.LevelSet };
        grid.Tree.SetValueOff(new(0), 1); var mask = new VdbTree<bool>(); mask.SetValueOn(new(0), false);
        new LevelSetTracker<double>(grid) { SpatialScheme = BiasedGradientScheme.First, NormalizationCount = 1, GrainSize = 0 }.Normalize(mask);
        Assert.False(grid.Tree.IsValueOn(new(0))); Assert.NotEqual(1, grid.Tree.GetValue(new(0)));
    }
    [Fact]
    public void ZeroCountDilationOnlyActivatesLayersAndChangesInactiveBackgroundBySign()
    {
        var grid = LevelSetSphere.Create(4.0, Vec3d.Zero, 1.0); var source = grid.Tree.DeepCopy();
        var tracker = new LevelSetTracker<double>(grid) { NormalizationCount = 0 }; tracker.Dilate();
        Assert.Equal(4, tracker.HalfWidth); Assert.True(grid.ActiveVoxelCount > source.ActiveVoxelCount);
        foreach (var value in source.EnumerateValues()) if (value.IsVoxel) Assert.Equal(value.Value, grid.Tree.GetValue(value.Coordinate));
        tracker.Erode(); Assert.Equal(3, tracker.HalfWidth); Assert.True(tracker.Leaves.IsValid);
        Assert.False(tracker.Resize(3));
    }
    [Fact]
    public void InvalidErosionPreservesOriginalGridAndInvalidSchemesRejectEvenZeroIterations()
    {
        var grid = LevelSetTrackingConformanceTests.Shape<float>(0); var original = grid.Tree.DeepCopy(); var tracker = new LevelSetTracker<float>(grid);
        Assert.Throws<ArgumentOutOfRangeException>(() => tracker.Erode(3)); Assert.True(original.HasSameTopology(grid.Tree));
        Assert.Equal(original.Background, grid.Background);
        tracker.SpatialScheme = BiasedGradientScheme.Unknown; tracker.NormalizationCount = 0;
        Assert.Throws<ArgumentOutOfRangeException>(() => tracker.Normalize());
    }
    [Fact]
    public void PreCancellationLeavesGridAndCachedAccessorsIntact()
    {
        var grid = LevelSetTrackingConformanceTests.Shape<double>(0); var original = grid.Tree.DeepCopy(); var tracker = new LevelSetTracker<double>(grid); var token = new CancellationToken(true);
        Assert.Throws<OperationCanceledException>(() => tracker.Normalize(token)); Assert.Throws<OperationCanceledException>(() => tracker.Track(token));
        Assert.Throws<OperationCanceledException>(() => tracker.Prune(token)); Assert.Throws<OperationCanceledException>(() => tracker.Dilate(1, token)); Assert.Throws<OperationCanceledException>(() => tracker.Erode(1, token));
        Assert.True(original.HasSameTopology(grid.Tree)); Assert.Equal(original.EnumerateValues().ToArray(), grid.Tree.EnumerateValues().ToArray());
    }
    [Fact]
    public async Task CancellationDuringLongNormalizationReleasesAuxiliaryBuffersAndRetainsFiniteGrid()
    {
        var grid = LevelSetTrackingConformanceTests.Shape<float>(0); var tracker = new LevelSetTracker<float>(grid) { NormalizationCount = 100000, GrainSize = 0 };
        using var cancellation = new CancellationTokenSource(); cancellation.CancelAfter(30);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Task.Run(() => tracker.Normalize(cancellation.Token)));
        Assert.Equal(0, tracker.Leaves.AuxiliaryBufferCount); Assert.True(tracker.Leaves.IsValid);
        Assert.All(grid.Tree.EnumerateValues(), value => Assert.True(float.IsFinite(value.Value)));
    }
    [Fact]
    public void ExternalTreeReplacementRebindsManagedLeafSnapshot()
    {
        var grid = LevelSetTrackingConformanceTests.Shape<double>(0); var tracker = new LevelSetTracker<double>(grid) { NormalizationCount = 0 };
        var replacement = new VdbTree<double>(3); replacement.SetValueOn(new(0), 1); grid.SetTree(replacement);
        tracker.Normalize(); Assert.Same(replacement, tracker.Leaves.Tree); Assert.Equal(1, tracker.Leaves.LeafCount);
    }
}
