using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
using Tedd.OpenVDB.Tree;

namespace Tedd.OpenVDB.Tests;

public sealed class MorphologyTests
{
    [Theory]
    [InlineData(NearestNeighbors.Face, 7)]
    [InlineData(NearestNeighbors.FaceEdge, 19)]
    [InlineData(NearestNeighbors.FaceEdgeVertex, 27)]
    public void SingleVoxelDilationUsesRequestedNeighborhood(NearestNeighbors neighbors, int count)
    {
        var tree = new VdbTree<int>(3); tree.SetValueOn(default, 9);
        tree.SetValueOff(new Coord(1, 0, 0), 17);
        Morphology.DilateActiveValues(tree, neighbors: neighbors);
        Assert.Equal((ulong)count, tree.ActiveVoxelCount);
        Assert.Equal(9, tree.GetValue(default));
        Assert.Equal(17, tree.GetValue(new Coord(1, 0, 0)));
        foreach (var point in new CoordBBox(new Coord(-1), new Coord(1)))
        {
            var axes = System.Math.Abs(point.X) + System.Math.Abs(point.Y) + System.Math.Abs(point.Z);
            var expected = axes <= (neighbors == NearestNeighbors.Face ? 1 : neighbors == NearestNeighbors.FaceEdge ? 2 : 3);
            Assert.Equal(expected, tree.IsValueOn(point));
        }
    }

    [Theory]
    [InlineData(NearestNeighbors.Face, false)]
    [InlineData(NearestNeighbors.FaceEdge, false)]
    [InlineData(NearestNeighbors.FaceEdgeVertex, false)]
    [InlineData(NearestNeighbors.Face, true)]
    [InlineData(NearestNeighbors.FaceEdge, true)]
    [InlineData(NearestNeighbors.FaceEdgeVertex, true)]
    public void RepeatedMorphologyMatchesIndependentDenseNeighborhoodModel(NearestNeighbors neighbors, bool erosion)
    {
        var random = new Random(771);
        var active = new HashSet<Coord>();
        var tree = new VdbTree<int>(7);
        foreach (var point in new CoordBBox(new Coord(-3), new Coord(3)))
            if (random.Next(5) != 0) { active.Add(point); tree.SetValueOn(point, 9); }
        var maximumAxes = neighbors == NearestNeighbors.Face ? 1 : neighbors == NearestNeighbors.FaceEdge ? 2 : 3;
        var kernel = new List<Coord>();
        foreach (var point in new CoordBBox(new Coord(-1), new Coord(1)))
        {
            var axes = System.Math.Abs(point.X) + System.Math.Abs(point.Y) + System.Math.Abs(point.Z);
            if (axes != 0 && axes <= maximumAxes) kernel.Add(point);
        }
        for (var iteration = 0; iteration < 2; ++iteration)
        {
            var result = new HashSet<Coord>();
            if (erosion)
            {
                foreach (var point in active) if (kernel.All(offset => active.Contains(point + offset))) result.Add(point);
            }
            else
            {
                result.UnionWith(active);
                foreach (var point in active) foreach (var offset in kernel) result.Add(point + offset);
            }
            active = result;
        }
        if (erosion) Morphology.ErodeActiveValues(tree, 2, neighbors);
        else Morphology.DilateActiveValues(tree, 2, neighbors);
        Assert.Equal((ulong)active.Count, tree.ActiveVoxelCount);
        foreach (var point in new CoordBBox(new Coord(-6), new Coord(6))) Assert.Equal(active.Contains(point), tree.IsValueOn(point));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IgnoreTilesDoesNotExpandOrModifyAnActiveRootTile(bool erosion)
    {
        var tree = new VdbTree<ushort>(); tree.AddTile(3, default, 9, true);
        if (erosion) Morphology.ErodeActiveValues(tree, 3, mode: TilePolicy.IgnoreTiles);
        else Morphology.DilateActiveValues(tree, 3, mode: TilePolicy.IgnoreTiles);
        Assert.Equal(1UL << 36, tree.ActiveVoxelCount);
        Assert.Equal(0, tree.LeafCount);
        Assert.Equal(1, tree.RootEntryCount);
        Assert.False(tree.IsValueOn(new Coord(-1, 0, 0)));
    }

    [Fact]
    public void PreserveTilesDilationRefinesOnlyTheBoundaryOfLargeUniformRegions()
    {
        var tree = new VdbTree<ushort>(3); tree.AddTile(2, default, 9, true);
        Morphology.DilateActiveValues(tree);
        Assert.Equal(128UL * 128 * 128 + 6UL * 128 * 128, tree.ActiveVoxelCount);
        Assert.Equal(1, tree.GetValueDepth(new Coord(64)));
        Assert.Null(tree.ProbeLeaf(new Coord(64)));
        Assert.InRange(tree.LeafCount, 1, 1536);
        Assert.True(tree.IsValueOn(new Coord(-1, 32, 32)));
        Assert.Equal((ushort)3, tree.GetValue(new Coord(-1, 32, 32)));
        Assert.False(tree.IsValueOn(new Coord(-1, -1, 32)));
    }

    [Fact]
    public void PreserveTilesErosionRetainsUnreachedInteriorTiles()
    {
        var tree = new VdbTree<ushort>(3); tree.AddTile(2, default, 9, true);
        Morphology.ErodeActiveValues(tree);
        Assert.Equal(126UL * 126 * 126, tree.ActiveVoxelCount);
        Assert.Null(tree.ProbeLeaf(new Coord(64)));
        Assert.InRange(tree.LeafCount, 1, 1600);
        Assert.False(tree.IsValueOn(new Coord(0, 32, 32)));
        Assert.True(tree.IsValueOn(new Coord(1, 32, 32)));
        Assert.Equal((ushort)9, tree.GetValue(new Coord(1, 32, 32)));
    }

    [Theory]
    [InlineData(TilePolicy.IgnoreTiles, 512, 0)]
    [InlineData(TilePolicy.ExpandTiles, 0, 1)]
    [InlineData(TilePolicy.PreserveTiles, 0, 0)]
    public void TilePoliciesDetermineWhetherEmptyLeavesAndUntouchedTilesRemain(TilePolicy policy, int expectedActive, int expectedLeaves)
    {
        var tree = new VdbTree<int>(3); tree.AddTile(1, default, 9, true);
        Morphology.ErodeActiveValues(tree, 4, mode: policy);
        Assert.Equal((ulong)expectedActive, tree.ActiveVoxelCount);
        Assert.Equal(expectedLeaves, tree.LeafCount);
        if (policy == TilePolicy.ExpandTiles) Assert.Equal(9, tree.GetValue(default));
    }

    [Fact]
    public void IgnoreTilesErosionStillUsesActiveTilesAsNeighborSupport()
    {
        var tree = new VdbTree<int>(); tree.AddTile(1, new Coord(0), 9, true);
        var point = new Coord(-1, 3, 3);
        foreach (var neighbor in new[] { point, new Coord(-2, 3, 3), new Coord(-1, 2, 3), new Coord(-1, 4, 3), new Coord(-1, 3, 2), new Coord(-1, 3, 4) }) tree.SetValueOn(neighbor, 9);
        Morphology.ErodeActiveValues(tree, mode: TilePolicy.IgnoreTiles);
        Assert.True(tree.IsValueOn(point));
        Assert.True(tree.IsValueOn(new Coord(0)));
        Assert.False(tree.IsValueOn(new Coord(-2, 3, 3)));
    }

    [Fact]
    public void SignedCoordinateLimitsDoNotWrapNeighborhoods()
    {
        var tree = new VdbTree<bool>(); tree.SetValueOn(new Coord(int.MaxValue, 0, 0), true);
        Morphology.DilateActiveValues(tree, mode: TilePolicy.IgnoreTiles);
        Assert.Equal(6UL, tree.ActiveVoxelCount);
        Assert.False(tree.IsValueOn(new Coord(int.MinValue, 0, 0)));
        Morphology.ErodeActiveValues(tree, mode: TilePolicy.IgnoreTiles);
        Assert.False(tree.IsValueOn(new Coord(int.MaxValue, 0, 0)));
    }

    [Fact]
    public void ZeroAndNegativeIterationsAreNoOpsAndInvalidModesAreRejected()
    {
        var tree = new VdbTree<int>(); tree.SetValueOn(default, 9);
        var leaf = tree.ProbeLeaf(default);
        Morphology.DilateActiveValues(tree, 0);
        Morphology.ErodeActiveValues(tree, -1);
        Assert.Same(leaf, tree.ProbeLeaf(default));
        Assert.Throws<ArgumentOutOfRangeException>(() => Morphology.DilateActiveValues(tree, neighbors: (NearestNeighbors)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => Morphology.ErodeActiveValues(tree, mode: (TilePolicy)99));
        Assert.Equal(1UL, tree.ActiveVoxelCount);
    }
}
