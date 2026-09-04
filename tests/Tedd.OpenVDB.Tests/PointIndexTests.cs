using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
namespace Tedd.OpenVDB.Tests;

public sealed class PointIndexTests
{
    [Fact]
    public void PrefixOffsetsStableVoxelOrderAndNegativeHalfBoundariesAreExact()
    {
        var points = new PointArray([new(.5), new(-.5), new(1.4), new(-.51), new(.5)]); var grid = PointIndexGrid.Create(points);
        Assert.Equal(new uint[] { 1 }, grid.Tree.ProbeLeaf(Coord.Zero)!.GetIndices(Coord.Zero).ToArray()); Assert.Equal(new uint[] { 0, 2, 4 }, grid.Tree.ProbeLeaf(new(1))!.GetIndices(new Coord(1)).ToArray()); Assert.Equal(new uint[] { 3 }, grid.Tree.ProbeLeaf(new(-1))!.GetIndices(new Coord(-1)).ToArray()); Assert.Equal(5ul, grid.PointCount); Assert.Equal(3ul, grid.ActiveVoxelCount); Assert.True(grid.IsValidPartition(points));
        var positive = grid.Tree.ProbeLeaf(Coord.Zero)!; Assert.Equal(1u, positive.GetValue(0)); Assert.Equal(4u, positive.GetValue(73)); Assert.Equal(4u, positive.GetValue(511));
    }
    [Fact]
    public void PrefixLeafCopiesInputsAndRejectsInconsistentOffsets()
    {
        uint[] offsets = new uint[512]; Array.Fill(offsets, 2u); uint[] ids = [9, 4]; var leaf = new PointIndexLeafNode(new(7), offsets, ids); offsets[0] = 0; ids[0] = 0;
        Assert.Equal(Coord.Zero, leaf.Origin); Assert.Equal(new uint[] { 9, 4 }, leaf.GetIndices(0).ToArray()); Assert.Equal(2u, leaf.GetValue(0)); Assert.True(leaf.IsValueOn(0)); Assert.False(leaf.IsValueOn(1)); Assert.False(leaf.IsEmpty(new(Coord.Zero, Coord.Zero))); Assert.True(leaf.IsEmpty(new(new Coord(1), new Coord(1))));
        Assert.Throws<ArgumentException>(() => new PointIndexLeafNode(Coord.Zero, new uint[511], [])); Assert.Throws<ArgumentException>(() => new PointIndexLeafNode(Coord.Zero, new uint[512], [1])); uint[] bad = new uint[512]; bad[0] = 1; Assert.Throws<ArgumentException>(() => new PointIndexLeafNode(Coord.Zero, bad, [1])); Assert.Throws<ArgumentOutOfRangeException>(() => leaf.GetIndices(512));
    }
    [Fact]
    public void ReadOnlyViewsAndDiagnosticCopiesCannotInvalidateIndexStorage()
    {
        var grid = PointIndexGrid.Create(new PointArray([Vec3d.Zero, new Vec3d(8)])); grid.Name = "points"; grid.GridClass = GridClass.Unknown; grid.SetMetadata("count", 2); var clone = grid.DeepCopy(); var offsets = grid.CopyOffsetGrid(); offsets.Tree.Clear(); offsets.Transform.PostScale(2); var mask = grid.Tree.Leaves[0].ValueMask; mask.SetOff(0);
        Assert.Equal(2ul, grid.PointCount); Assert.Equal(2ul, grid.ActiveVoxelCount); Assert.True(grid.Tree.Leaves[0].IsValueOn(0)); Assert.Equal(1, grid.Transform.VoxelSize.X); Assert.NotSame(grid.Tree.Leaves[0], clone.Tree.Leaves[0]); Assert.Equal("points", clone.Name); Assert.Equal(2, clone.GetMetadata<int>("count")); Assert.True(clone.TryGetMetadata<int>("count", out int count) && count == 2); clone.RemoveMetadata("count"); Assert.Equal(2, grid.GetMetadata<int>("count")); clone.Transform.PostTranslate(Vec3d.One); Assert.Equal(Vec3d.Zero, grid.Transform.IndexToWorld(Vec3d.Zero)); Assert.Equal(new uint[] { 0, 1 }, new PointIndexIterator(CoordBBox.Infinite, grid).ToArray());
    }
    [Fact]
    public void ValidPartitionTracksVoxelCrossingCountAndTransformChanges()
    {
        Vec3d[] positions = [Vec3d.Zero, new(.2)]; var source = new PointArray(positions); var grid = PointIndexGrid.Create(source); positions[0] = new(.3); Assert.True(grid.IsValidPartition(source)); Assert.Same(grid, PointIndexGrid.GetValid(source, grid)); positions[0] = new(.6); Assert.False(grid.IsValidPartition(source)); var rebuilt = PointIndexGrid.GetValid(source, grid); Assert.NotSame(grid, rebuilt); Assert.True(rebuilt.IsValidPartition(source)); Assert.False(grid.IsValidPartition(new PointArray([Vec3d.Zero]))); rebuilt.Transform.PostTranslate(new Vec3d(4)); Assert.False(rebuilt.IsValidPartition(source));
    }
    [Fact]
    public void NonfinitePointsAreOmittedAndMakeTheFullSourcePartitionInvalid()
    { var source = new PointArray([Vec3d.Zero, new Vec3d(double.NaN), new Vec3d(double.PositiveInfinity)]); var grid = PointIndexGrid.Create(source); Assert.Equal(1ul, grid.PointCount); Assert.False(grid.IsValidPartition(source)); Assert.Equal(new uint[] { 0 }, new PointIndexIterator(CoordBBox.Infinite, grid).ToArray()); }
    [Fact]
    public void EmptySourcesAndSparseCoordinateExtremesDoNotScanEmptySpace()
    {
        var empty = PointIndexGrid.Create(new PointArray([])); Assert.True(empty.Tree.IsEmpty); Assert.Equal(new long[] { 0, 0, 0, 1 }, empty.Tree.NodeCount()); Assert.Empty(new PointIndexIterator(CoordBBox.Infinite, empty));
        var points = new PointArray([new Vec3d(int.MinValue), new Vec3d(int.MaxValue)]); var grid = PointIndexGrid.Create(points); var iterator = new PointIndexIterator(CoordBBox.Infinite, grid); Assert.Equal(new uint[] { 0, 1 }, iterator.ToArray()); Assert.Equal(2, grid.Tree.RootEntryCount); Assert.Equal(new Coord(int.MaxValue & ~7), grid.Tree.Leaves[1].Origin);
    }
    [Fact]
    public void RadialQueriesUseStrictPointDistanceAndBoxesIncludeDegenerateBounds()
    {
        var points = new PointArray([Vec3d.Zero, new Vec3d(1, 0, 0), new Vec3d(-1, 0, 0), new Vec3d(.999, 0, 0)]); var grid = PointIndexGrid.Create(points); var it = new PointIndexIterator(); it.SearchAndUpdate(Vec3d.Zero, 1, grid, points); Assert.Equal(new uint[] { 0, 3 }, it.Order().ToArray()); it.SearchAndUpdate(Vec3d.Zero, 0, grid, points); Assert.Empty(it); it.SearchAndUpdate(new BBoxd(Vec3d.Zero, Vec3d.Zero), grid, points); Assert.Equal(new uint[] { 0 }, it.ToArray()); it.SearchAndUpdate(new BBoxd(new Vec3d(-1), new Vec3d(1)), grid, points); Assert.Equal(new uint[] { 0, 1, 2, 3 }, it.Order().ToArray());
    }
    [Fact]
    public void SeededAccurateQueriesAgreeWithIndependentBruteForceAcrossLeaves()
    {
        var rng = new Random(829); Vec3d[] p = Enumerable.Range(0, 2000).Select(_ => new Vec3d(rng.NextDouble() * 64 - 32, rng.NextDouble() * 64 - 32, rng.NextDouble() * 64 - 32)).ToArray(); var source = new PointArray(p); var transform = Transform.CreateLinearTransform(.75); transform.PostTranslate(new(.1, -.2, .3)); var grid = PointIndexGrid.Create(source, transform); var it = new PointIndexIterator();
        for (int q = 0; q < 20; ++q) { Vec3d center = new(rng.NextDouble() * 40 - 20, rng.NextDouble() * 40 - 20, rng.NextDouble() * 40 - 20); double radius = 1 + q * .7; it.WorldSpaceSearchAndUpdate(center, radius, grid, source); uint[] expected = p.Select((x, i) => (x, i)).Where(x => (x.x - center).LengthSquared < radius * radius).Select(x => (uint)x.i).Order().ToArray(); Assert.Equal(expected, it.Order().ToArray()); var box = new BBoxd(center - new Vec3d(radius), center + new Vec3d(radius)); it.WorldSpaceSearchAndUpdate(box, grid, source); Assert.Equal(p.Select((x, i) => (x, i)).Where(x => box.Contains(x.x)).Select(x => (uint)x.i).Order(), it.Order()); }
    }
    [Fact]
    public void CoarseRadialQueryRetainsVoxelCandidatesBeyondTheExactSphere()
    { var points = new PointArray([new Vec3d(.49, .49, .49)]); var grid = PointIndexGrid.Create(points); var it = new PointIndexIterator(); it.SearchAndUpdate(Vec3d.Zero, .1, grid, points, true); Assert.Empty(it); it.SearchAndUpdate(Vec3d.Zero, .1, grid, points, false); Assert.Equal(new uint[] { 0 }, it.ToArray()); }
    [Fact]
    public void IteratorCopiesResetAndQueriesOwnTheirResultLifetime()
    {
        var points = new PointArray([Vec3d.Zero, new Vec3d(.1), new Vec3d(.2)]); var grid = PointIndexGrid.Create(points); var a = new PointIndexIterator(); a.SearchAndUpdate(new BBoxd(new Vec3d(-.25), new Vec3d(.25)), grid, points); Assert.True(a.Next()); var b = new PointIndexIterator(a); Assert.Equal(1u, b.Current); a.Clear(); GC.Collect(2, GCCollectionMode.Forced, true, true); Assert.Equal(1u, b.Current); Assert.True(b.Next()); Assert.Equal(2u, b.Current); Assert.False(b.Next()); Assert.Throws<InvalidOperationException>(() => b.Current); b.Reset(); Assert.Equal(0u, b.Current); Assert.Equal(3ul, b.Count); Assert.Equal(new uint[] { 0, 1, 2 }, b.ToArray());
    }
    [Fact]
    public void FiltersGiveExactSquaredDistancesAndCacheTheirConstructionTransform()
    {
        var points = new PointArray([Vec3d.Zero, new Vec3d(.5, 0, 0), new Vec3d(1, 0, 0), new Vec3d(10, 0, 0)]); var grid = PointIndexGrid.Create(points); var filter = new PointIndexFilter(points, grid); grid.Transform.PostTranslate(new Vec3d(100)); List<(double, uint)> hits = []; filter.SearchAndApply(Vec3d.Zero, 1, (d, i) => hits.Add((d, i))); Assert.Equal(new (double, uint)[] { (0, 0), (.25, 1) }, hits);
        hits.Clear(); new PointIndexFilter(filter).SearchAndApply(Vec3d.Zero, 11, (d, i) => hits.Add((d, i))); Assert.Equal(new (double, uint)[] { (0, 0), (.25, 1), (1, 2), (100, 3) }, hits.OrderBy(x => x.Item2));
    }
    [Fact]
    public void FilterCallbacksCancellationAndPositionFailuresAreObservable()
    {
        var points = new PointArray([Vec3d.Zero, new Vec3d(.1)]); var grid = PointIndexGrid.Create(points); var filter = new PointIndexFilter(points, grid); Assert.Throws<OperationCanceledException>(() => filter.SearchAndApply(Vec3d.Zero, 1, (_, _) => { }, new(true))); Assert.Throws<ArithmeticException>(() => filter.SearchAndApply(Vec3d.Zero, 1, (_, _) => throw new ArithmeticException())); Assert.Throws<ArithmeticException>(() => PointIndexGrid.Create(new ThrowingPoints()));
        using var cts = new CancellationTokenSource(); int calls = 0; Assert.Throws<OperationCanceledException>(() => filter.SearchAndApply(Vec3d.Zero, 1, (_, _) => { ++calls; cts.Cancel(); }, cts.Token)); Assert.Equal(1, calls);
    }
    [Fact]
    public async Task IndependentQueriesReadAFrozenPointTreeConcurrently()
    {
        var source = ParticleAtlasConformanceTests.Particles(2); var grid = PointIndexGrid.Create(source, .75); uint[] all = new PointIndexIterator(CoordBBox.Infinite, grid).ToArray(); await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => { for (int n = 0; n < 25; ++n) { var it = new PointIndexIterator(CoordBBox.Infinite, grid); Assert.Equal(all, it.ToArray()); it.WorldSpaceSearchAndUpdate(Vec3d.Zero, 10, grid, source); Assert.All(it, i => Assert.True(source.GetPosition((int)i).LengthSquared < 100)); } })));
    }
    private sealed class ThrowingPoints : IPointSource { public int Count => 2; public Vec3d GetPosition(int index) => index == 0 ? Vec3d.Zero : throw new ArithmeticException(); }
    private static ParticleArray P(params Particle[] particles) => new(particles);
}
public sealed class ParticleAtlasTests
{
    [Fact]
    public void RadiusLevelsUseNativeThresholdAndMaximumLevelsPlusOne()
    {
        var particles = P(new Particle(new Vec3d(0), 1), new Particle(new Vec3d(1), 2), new Particle(new Vec3d(2), 4), new Particle(new Vec3d(3), 8), new Particle(new Vec3d(4), 16)); var atlas = ParticleAtlas.Create(particles, 1, 2); Assert.Equal(3, atlas.Levels); Assert.Equal(1, atlas.MinimumRadius(0)); Assert.Equal(2, atlas.MinimumRadius(1)); Assert.Equal(4, atlas.MinimumRadius(2)); Assert.Equal(16, atlas.MaximumRadius(2)); Assert.Equal(3ul, atlas.PointIndexGrid(2).PointCount); Assert.Equal(1, ParticleAtlas.Create(particles, 1, 0).Levels); Assert.Equal(1, ParticleAtlas.Create(particles, 1, 1).Levels);
    }
    [Fact]
    public void ConstructionAppendsAndExistingIteratorsKeepOriginalAccessibleLevels()
    {
        var particles = P(new Particle(Vec3d.Zero, 1)); var atlas = ParticleAtlas.Create(particles, 1); var old = atlas.CreateIterator(); atlas.Construct(particles, 1); Assert.Equal(2, atlas.Levels); Assert.Equal(2, old.Levels); old.UpdateFromLevel(100); Assert.Equal(new uint[] { 0 }, old.ToArray()); var fresh = atlas.CreateIterator(); fresh.WorldSpaceSearchAndUpdate(Vec3d.Zero, 2, particles); old.WorldSpaceSearchAndUpdate(Vec3d.Zero, 2, particles); Assert.Equal(old.Count * 2, fresh.Count);
    }
    [Fact]
    public void EmptyConstructionRetainsNativeExtremeRadiusAndOneEmptyLevel()
    { var atlas = new ParticleAtlas(); Assert.True(atlas.IsEmpty); Assert.Empty(atlas.CreateIterator()); atlas.Construct(P(), 1); Assert.False(atlas.IsEmpty); Assert.Equal(1, atlas.Levels); Assert.Equal(double.MaxValue, atlas.MinimumRadius(0)); Assert.Equal(-double.MaxValue, atlas.MaximumRadius(0)); Assert.Equal(new Vec3d(double.MaxValue), atlas.PointIndexGrid(0).Transform.VoxelSize); var it = atlas.CreateIterator(); it.UpdateFromLevel(100); Assert.Empty(it); }
    [Fact]
    public void NativeExclusiveFaceRegionsRetainDuplicateIndicesAtZeroRadius()
    { var particles = P(new Particle(Vec3d.Zero, 1)); var atlas = ParticleAtlas.Create(particles, 1); var it = atlas.CreateIterator(); it.WorldSpaceSearchAndUpdate(Vec3d.Zero, 0, particles); Assert.Equal(new uint[] { 0, 0 }, it.ToArray()); }
    [Fact]
    public void NativeUniformLevelRadiusCacheReadsGlobalParticleZero()
    { var particles = P(new Particle(new Vec3d(100), 1), new Particle(Vec3d.Zero, 8)); var atlas = ParticleAtlas.Create(particles, 1); Assert.Equal(2, atlas.Levels); var it = atlas.CreateIterator(); it.UpdateFromLevel(1); Assert.Equal(new uint[] { 1 }, it.ToArray()); it.WorldSpaceSearchAndUpdate(new Vec3d(6, 0, 0), 0, particles); Assert.Empty(it); }
    [Fact]
    public void FrozenAtlasIteratorResetsAndKeepsPointStorageAliveThroughCompaction()
    { var particles = ParticleAtlasConformanceTests.Particles(3); var atlas = ParticleAtlas.Create(particles, .125); var it = atlas.CreateIterator(); it.UpdateFromLevel(2); uint[] all = it.ToArray(); Assert.NotEmpty(all); it.Next(); GC.Collect(2, GCCollectionMode.Forced, true, true); it.Reset(); Assert.Equal(all[0], it.Current); Assert.Equal(all, it.ToArray()); }
    [Fact]
    public void FailedOrCancelledConstructPublishesNoPartialLevels()
    { var particles = P(new Particle(Vec3d.Zero, 1)); var atlas = ParticleAtlas.Create(particles, 1); Assert.Throws<OperationCanceledException>(() => atlas.Construct(particles, 1, cancellationToken: new(true))); Assert.Throws<ArgumentOutOfRangeException>(() => atlas.Construct(P(new Particle(Vec3d.Zero, double.NaN)), 1)); Assert.Equal(1, atlas.Levels); Assert.Equal(1ul, atlas.PointIndexGrid(0).PointCount); Assert.Throws<ArgumentOutOfRangeException>(() => atlas.Construct(particles, 1, -1)); }
    [Fact]
    public void QueriesAndLevelBoundsValidateManagedArguments()
    { var atlas = ParticleAtlas.Create(P(new Particle(Vec3d.Zero, 1)), 1); var it = atlas.CreateIterator(); Assert.Throws<ArgumentOutOfRangeException>(() => it.UpdateFromLevel(-1)); Assert.Throws<ArgumentOutOfRangeException>(() => it.WorldSpaceSearchAndUpdate(Vec3d.Zero, -1, P())); Assert.Throws<OperationCanceledException>(() => it.WorldSpaceSearchAndUpdate(Vec3d.Zero, 1, P(), new(true))); }
    [Fact]
    public void ConstructedTopologyContainsOnlyLeavesAndNoActiveTiles()
    { var grid = PointIndexGrid.Create(new PointArray([Vec3d.Zero, new Vec3d(4096)])); Assert.Equal(2, grid.Tree.LeafCount); Assert.Equal(2ul, grid.ActiveVoxelCount); Assert.All(grid.Tree.EnumerateValues(), value => Assert.True(value.IsVoxel)); }
    [Fact]
    public async Task OnePointFilterSupportsConcurrentFrozenQueries()
    { var source = ParticleAtlasConformanceTests.Particles(2); var grid = PointIndexGrid.Create(source, .75); var filter = new PointIndexFilter(source, grid); await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => { for (int n = 0; n < 20; ++n) { List<uint> ids = []; filter.SearchAndApply(Vec3d.Zero, 10, (_, i) => ids.Add(i)); Assert.Equal(ids.Order(), ids.Order().Distinct()); } }))); }
    [Fact]
    public void EveryParticleIsUniquelyAssignedAcrossAtlasLevels()
    { var particles = ParticleAtlasConformanceTests.Particles(3); var atlas = ParticleAtlas.Create(particles, .125); var iterator = atlas.CreateIterator(); List<uint> indices = []; for (int level = 0; level < atlas.Levels; ++level) { iterator.UpdateFromLevel(level); indices.AddRange(iterator); } Assert.Equal(Enumerable.Range(0, particles.Count).Select(x => (uint)x), indices.Order()); }
    private static ParticleArray P(params Particle[] particles) => new(particles);
}
