// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tests;

public class RayIntersectorTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void VolumeIncludesInactiveAllocatedLeavesButExcludesInactiveTiles(int level)
    {
        var grid = new Grid<int>(); grid.Tree.SetValueOn(new(0, 0, 0), 1); grid.Tree.TouchLeaf(new(16, 0, 0)); grid.Tree.SetValueOn(new(24, 0, 0), 1); grid.Tree.AddTile(1, new(8, 0, 0), 9, false);
        var intersector = new VolumeRayIntersector<int>(grid, nodeLevel: level);
        Assert.True(intersector.SetIndexRay(new(new(-1, 0.5, 0.5), Vec3d.UnitX, 0, 100)));
        List<RayTimeSpan> spans = []; intersector.Hits(spans);
        Assert.Equal(new RayTimeSpan[] { new(1, 9), new(17, 33) }, spans);
        Assert.Equal(2UL, grid.ActiveVoxelCount); Assert.Equal(3, grid.Tree.LeafCount);
    }
    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(1, 7, -8)]
    [InlineData(2, 25, -8)]
    public void DilationCopiesFaceNeighborsWithoutChangingSource(int iterations, int count, int minimum)
    {
        var grid = new Grid<byte>(); grid.Tree.SetValueOn(Coord.Zero, 0);
        var intersector = new VolumeRayIntersector<byte>(grid, iterations);
        var copy = intersector.CopyTopology(); Assert.Equal((ulong)count, copy.ActiveVoxelCount); Assert.Equal((double)minimum, intersector.BoundingBox.Min.X); Assert.Equal(1UL, grid.ActiveVoxelCount);
        foreach (var value in copy.EnumerateValues()) Assert.False(value.Value);
    }
    [Fact]
    public void CoarseTilesRemainCompactAndFalseActiveValuesStillIntersect()
    {
        var grid = new Grid<bool>(); grid.Tree.AddTile(3, Coord.Zero, false, true);
        var intersector = new VolumeRayIntersector<bool>(grid, 2);
        Assert.Equal(0, intersector.CopyTopology().LeafCount); Assert.True(intersector.SetIndexRay(new(new(-1, 0.5, 0.5), Vec3d.UnitX)));
        Assert.Equal(new RayTimeSpan(1, 4097), intersector.March()); Assert.False(intersector.March().IsValid());
    }
    [Fact]
    public void SnapshotAndCopiesHaveIndependentQueryState()
    {
        var grid = new Grid<int>(); grid[Coord.Zero] = 1; grid[new(16, 0, 0)] = 2;
        var first = new VolumeRayIntersector<int>(grid); grid.Tree.Clear();
        Assert.True(first.SetIndexRay(new(new(-1, 0.5, 0.5), Vec3d.UnitX)));
        var copy = new VolumeRayIntersector<int>(first); var mutableTopology = first.CopyTopology(); mutableTopology.Clear();
        Assert.Equal(new RayTimeSpan(1, 9), first.March()); Assert.Equal(new RayTimeSpan(1, 9), copy.March());
        Assert.Equal(new RayTimeSpan(17, 25), first.March()); Assert.False(first.March().IsValid()); Assert.Equal(new RayTimeSpan(17, 25), copy.March());
    }
    [Fact]
    public void ExplicitBoundsRestrictCoarseHitsAndMissClearsPreviousState()
    {
        var grid = new Grid<int>(); grid[Coord.Zero] = 1;
        var intersector = new VolumeRayIntersector<int>(grid, new BBoxd(new Vec3d(2, 2, 2), new Vec3d(6, 6, 6)));
        Assert.False(intersector.March().IsValid()); Assert.Throws<InvalidOperationException>(() => intersector.GetIndexPosition(0));
        Assert.True(intersector.SetIndexRay(new(new(-1, 3, 3), Vec3d.UnitX))); Assert.Equal(new RayTimeSpan(3, 7), intersector.March());
        Assert.False(intersector.SetIndexRay(new(new(-1, 20, 20), Vec3d.UnitX)));
        List<RayTimeSpan> values = [new(1, 2)]; intersector.Hits(values); Assert.Empty(values); Assert.False(intersector.March().IsValid());
    }
    [Fact]
    public void WorldRaysNormalizeAndConvertDistanceUsingVoxelScale()
    {
        var grid = new Grid<int>(); grid[Coord.Zero] = 1; grid.Transform = Transform.CreateLinearTransform(2); grid.Transform.PostTranslate(new(10, 20, 30));
        var intersector = new VolumeRayIntersector<int>(grid);
        Assert.True(intersector.SetWorldRay(new(new(8, 21, 31), Vec3d.UnitX, 0, 100)));
        var hit = intersector.March(); Assert.Equal(new RayTimeSpan(1, 9), hit);
        Assert.Equal(new Vec3d(0, 0.5, 0.5), intersector.GetIndexPosition(hit.Start)); Assert.Equal(new Vec3d(10, 21, 31), intersector.GetWorldPosition(hit.Start)); Assert.Equal(2, intersector.GetWorldTime(hit.Start));
    }
    [Fact]
    public void VolumeHandlesUpperCoordinateBoundaryWithoutOverflowingItsBoundingBox()
    {
        var grid = new Grid<int>(); grid[new(int.MaxValue, 0, 0)] = 1;
        var intersector = new VolumeRayIntersector<int>(grid);
        Assert.Equal((double)int.MaxValue + 1, intersector.BoundingBox.Max.X);
        var ray = new Rayd(new((double)int.MaxValue - 15, 0.5, 0.5), Vec3d.UnitX, 0, 100);
        Assert.True(intersector.SetIndexRay(ray)); Assert.Equal(new RayTimeSpan(8, 16), intersector.March()); Assert.False(intersector.March().IsValid());
    }
    [Fact]
    public void InactiveOnlyGridHasNoAutomaticBoundsButExplicitBoundsVisitItsLeaves()
    {
        var grid = new Grid<int>(); grid.Tree.TouchLeaf(Coord.Zero);
        Assert.False(grid.IsEmpty); Assert.False(new VolumeRayIntersector<int>(grid).SetIndexRay(new(new(-1, 0.5, 0.5), Vec3d.UnitX)));
        var explicitBounds = new VolumeRayIntersector<int>(grid, new BBoxd(Vec3d.Zero, new Vec3d(8)));
        Assert.True(explicitBounds.SetIndexRay(new(new(-1, 0.5, 0.5), Vec3d.UnitX))); Assert.Equal(new RayTimeSpan(1, 9), explicitBounds.March());
    }
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void PlaneLevelSetReturnsAnalyticCrossingAndWorldNormal(int level)
    {
        var grid = NativeRayTests.MakeLevel<double>(2); var intersector = new LevelSetRayIntersector<double>(grid, nodeLevel: level);
        var ray = new Rayd(new(-8, 0.5, 0.25), Vec3d.UnitX, 0, 20);
        Assert.True(intersector.IntersectsIndex(ray, out var position, out var time));
        double x = (0.375 - 0.5 * 0.125 + 0.25 * 0.0625) / 0.5;
        Assert.Equal(x + 8, time, 12); Assert.Equal(x, position.X, 12);
        Assert.True(intersector.IntersectsWorld(ray.IndexToWorld(grid.Transform), out var world, out var normal, out var worldTime));
        Assert.Equal(time * 0.5, worldTime, 12); Assert.True(world.ApproxEquals(position * 0.5, 1e-12)); Assert.True(normal.ApproxEquals(new Vec3d(0.5, 0.125, -0.0625).Unit(), 1e-12));
        var copy = new LevelSetRayIntersector<double>(intersector); Assert.True(copy.IntersectsIndex(ray, out double second)); Assert.Equal(time, second);
    }
    [Fact]
    public void SearchRequiresHitBeforeResultsAndResetsOnNewRay()
    {
        var search = new LinearSearch<double>(NativeRayTests.MakeLevel<double>(0));
        Assert.Throws<InvalidOperationException>(() => search.GetIndexTime()); Assert.Throws<InvalidOperationException>(() => search.GetWorldPosition());
        Assert.True(search.SetIndexRay(new(new(-20, 0.25, 0.375), Vec3d.UnitX))); Assert.True(LevelSetHdda.Test(search)); Assert.True(double.IsFinite(search.GetIndexTime()));
        Assert.False(search.SetIndexRay(new(new(-20, 100, 100), Vec3d.UnitX))); Assert.Throws<InvalidOperationException>(() => search.GetIndexTime());
    }
    [Fact]
    public void InactiveLevelSetValuesDoNotCreateAnIntersection()
    {
        var grid = NativeRayTests.MakeLevel<double>(2); foreach (var leaf in grid.Tree.EnumerateLeaves()) for (int i = 0; i < LeafNode<double>.Size; i++) leaf.SetValueOff(i);
        var intersector = new LevelSetRayIntersector<double>(grid); Assert.False(intersector.IntersectsIndex(new(new(-8, 0.5, 0.25), Vec3d.UnitX), out double time)); Assert.True(double.IsNaN(time));
    }
    [Fact]
    public void FlatZeroSamplesPreserveNativeIndeterminateLinearTime()
    {
        var grid = new Grid<double>(3) { GridClass = GridClass.LevelSet }; grid.Tree.Fill(new(0, 0, 0, 7, 7, 7), 0, true);
        var intersector = new LevelSetRayIntersector<double>(grid, nodeLevel: -1);
        Assert.True(intersector.IntersectsIndex(new(new(0.25, 0.25, 0.25), Vec3d.UnitX, 0, 2), out double time)); Assert.True(double.IsNaN(time));
    }
    [Fact]
    public void PerWorkerCopiesReadConcurrently()
    {
        var grid = NativeRayTests.MakeLevel<float>(0); var master = new LevelSetRayIntersector<float>(grid, iterations: 2);
        var ray = new Rayd(new(-20, 0.125, 0.375), Vec3d.UnitX); Assert.True(master.IntersectsIndex(ray, out double expected));
        Parallel.For(0, 64, _ => { var copy = new LevelSetRayIntersector<float>(master); for (int i = 0; i < 20; i++) { Assert.True(copy.IntersectsIndex(ray, out double actual)); Assert.Equal(expected, actual); } });
    }
    [Fact]
    public void WarmQueriesAllocateNoManagedMemory()
    {
        var grid = NativeRayTests.MakeLevel<float>(0); var surface = new LevelSetRayIntersector<float>(grid); var volume = new VolumeRayIntersector<float>(grid);
        var ray = new Rayd(new(-20, 0.125, 0.375), Vec3d.UnitX); List<RayTimeSpan> spans = new(128);
        for (int i = 0; i < 1000; i++) { surface.IntersectsIndex(ray); volume.SetIndexRay(ray); volume.Hits(spans); }
        long before = GC.GetAllocatedBytesForCurrentThread(); int hits = 0;
        for (int i = 0; i < 1000; i++) { if (surface.IntersectsIndex(ray)) hits++; volume.SetIndexRay(ray); volume.Hits(spans); }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before; Assert.Equal(1000, hits); Assert.Equal(0, allocated);
    }
    [Fact]
    public void CustomTesterReceivesInclusiveVoxelTraversalAndHierarchicalRanges()
    {
        var direct = new RecordingTester(); Assert.True(LevelSetHdda.Test(direct, -1)); Assert.Equal(new[] { new Coord(0, 0, 0), new Coord(1, 0, 0), new Coord(2, 0, 0) }, direct.Visited); Assert.Equal(0, direct.Initialized); Assert.Equal(2.75, direct.Ray.MaxTime);
        var hierarchical = new RecordingTester(); Assert.True(LevelSetHdda.Test(hierarchical, 2)); Assert.Equal(direct.Visited, hierarchical.Visited); Assert.Equal(new[] { 2, 1, 0 }, hierarchical.Levels);
    }
    [Fact]
    public void HitsRetainsShortSpansThatMarchIntentionallyRejects()
    {
        var grid = new Grid<int>(); grid[Coord.Zero] = 1; var volume = new VolumeRayIntersector<int>(grid);
        Assert.True(volume.SetIndexRay(new(new(0.5, 0.5, 0.5), Vec3d.UnitX, 0.2, 0.200001)));
        List<RayTimeSpan> hits = []; volume.Hits(hits); Assert.Equal(new RayTimeSpan[] { new(0.2, 0.200001) }, hits);
        Assert.False(volume.March().IsValid());
    }
    [Fact]
    public void ValueTypeTesterRetainsItsMutatedState()
    {
        var tester = new ValueTester { Ray = new(Vec3d.Zero, Vec3d.UnitX, 0, 3) };
        Assert.True(LevelSetHdda.Test(ref tester, -1)); Assert.Equal(3, tester.Visited); Assert.Equal(0, tester.Initialized);
    }
    private struct ValueTester : ILevelSetRayTester
    {
        public Rayd Ray { get; set; }
        public int Visited; public double Initialized;
        public bool HasNode(Coord c, int level) => true;
        public void SetRange(double start, double end) => Ray = new(Ray.Eye, Ray.Direction, start, end);
        public void Initialize(double time) => Initialized = time;
        public bool Test(Coord c, double time) => ++Visited == 3;
    }
    private sealed class RecordingTester : ILevelSetRayTester
    {
        public Rayd Ray { get; private set; } = new(new(0.25, 0.5, 0.5), Vec3d.UnitX, 0, 2.75);
        public List<Coord> Visited { get; } = []; public List<int> Levels { get; } = []; public double Initialized { get; private set; } = -1;
        public bool HasNode(Coord c, int level) { Levels.Add(level); return true; }
        public void SetRange(double start, double end) => Ray = new(Ray.Eye, Ray.Direction, start, end);
        public void Initialize(double time) => Initialized = time;
        public bool Test(Coord c, double time) { Visited.Add(c); return c.X == 2; }
    }
    [Fact]
    public void InvalidConfigurationIsRejected()
    {
        Assert.Throws<ArgumentException>(() => new VolumeRayIntersector<int>(new Grid<int>()));
        var grid = new Grid<int>(); grid[Coord.Zero] = 1; Assert.Throws<ArgumentOutOfRangeException>(() => new VolumeRayIntersector<int>(grid, nodeLevel: -1));
        Assert.Throws<ArgumentException>(() => new VolumeRayIntersector<int>(grid, BBoxd.Empty));
        grid.Transform = new Transform(Mat4d.CreateScale(new Vec3d(1, 2, 3))); Assert.Throws<ArgumentException>(() => new VolumeRayIntersector<int>(grid));
        var level = NativeRayTests.MakeLevel<float>(0); Assert.Throws<ArgumentOutOfRangeException>(() => new LevelSetRayIntersector<float>(level, level.Background));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LevelSetRayIntersector<float>(level, float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LevelSetRayIntersector<float>(level, iterations: -1)); Assert.Throws<ArgumentOutOfRangeException>(() => new LevelSetRayIntersector<float>(level, nodeLevel: 3));
        level.GridClass = GridClass.Unknown; Assert.Throws<ArgumentException>(() => new LevelSetRayIntersector<float>(level));
        var half = new Grid<Half>((Half)3) { GridClass = GridClass.LevelSet }; half[Coord.Zero] = (Half)0; Assert.Throws<NotSupportedException>(() => new LevelSetRayIntersector<Half>(half));
        var r = new Rayd(Vec3d.Zero, Vec3d.UnitX, 0, 1); Assert.Throws<ArgumentOutOfRangeException>(() => VolumeHdda.March(new VdbTree<int>(), ref r, 3));
    }
    [Theory]
    [InlineData(-1, -1, false)]
    [InlineData(0, 1, true)]
    [InlineData(0, 0.0000000001, false)]
    [InlineData(4, 2, false)]
    public void SpanValidityUsesNativeDoubleTolerance(double start, double end, bool valid)
    {
        var span = new RayTimeSpan(start, end); Assert.Equal(valid, span.IsValid()); Assert.Equal((start + end) * 0.5, span.Midpoint);
        if (valid) { Assert.True(span.Contains(start)); Assert.True(span.Contains(end)); Assert.False(span.Contains(start - 1)); }
    }
}
