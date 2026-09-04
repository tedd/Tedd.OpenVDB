using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
using Tedd.OpenVDB.Tree;

namespace Tedd.OpenVDB.Tests;

public sealed class SegmentationTests
{
    private readonly record struct Block(ushort Id, byte Light);

    [Fact]
    public void FaceConnectivityCrossesLeavesButEdgesAndCornersRemainSeparate()
    {
        var tree = new VdbTree<ushort>();
        Coord[] points = [new(7, 0, 0), new(8, 0, 0), new(9, 1, 0), new(10, 2, 1)];
        foreach (Coord point in points) tree.SetValueOn(point, 7);
        var masks = Segmentation.ExtractActiveVoxelSegmentMasks(tree);
        Assert.Equal(new ulong[] { 2, 1, 1 }, masks.Select(mask => mask.ActiveVoxelCount));
        Assert.True(masks[0].IsValueOn(points[0])); Assert.True(masks[0].IsValueOn(points[1]));
        Assert.All(masks.SelectMany(mask => mask.EnumerateValues()), value => Assert.False(value.Value));
        Assert.Equal(4UL, tree.ActiveVoxelCount);
    }

    [Fact]
    public void RootTileBudgetRejectsBeforeVoxelAllocationAndLeavesInputIntact()
    {
        var tree = new VdbTree<ushort>(); tree.AddTile(3, new(0), 42, true);
        Assert.Throws<InvalidOperationException>(() => Segmentation.ExtractActiveVoxelSegmentMasks(tree, maximumNewLeaves: 100));
        Assert.Equal(1UL << 36, tree.ActiveVoxelCount);
        Assert.Equal(0, tree.LeafCount);
    }

    [Fact]
    public void SegmentationRestoresOriginalTileDepthForSeparateActiveComponents()
    {
        var tree = new VdbTree<int>(-1);
        tree.AddTile(1, new(0), 7, true); tree.SetValueOn(new(16, 0, 0), 9);
        var masks = Segmentation.ExtractActiveVoxelSegmentMasks(tree, 1);
        Assert.Equal(1, masks[0].LeafCount);
        var segments = Segmentation.SegmentActiveVoxels(tree, 1);
        Assert.Equal(2, segments[0].GetValueDepth(new(0)));
        Assert.Equal(7, segments[0].GetValue(new(0)));
        Assert.Equal(0, segments[0].LeafCount);
        Assert.Equal(3, segments[1].GetValueDepth(new(16, 0, 0)));
    }

    [Fact]
    public void CustomBlocksCopyWithoutInterpretingTheirPayload()
    {
        var background = new Block(0, 3);
        var tree = new VdbTree<Block>(background);
        tree.SetValueOn(new(0), new(17, 8)); tree.SetValueOn(new(1, 0, 0), new(18, 9)); tree.SetValueOn(new(16, 0, 0), new(42, 15));
        var segments = Segmentation.SegmentActiveVoxels(tree);
        Assert.Equal(new Block(17, 8), segments[0].GetValue(new(0)));
        Assert.Equal(new Block(18, 9), segments[0].GetValue(new(1, 0, 0)));
        Assert.Equal(background, segments[0].GetValue(new(16, 0, 0)));
        Assert.Equal(new Block(42, 15), segments[1].GetValue(new(16, 0, 0)));
        segments[0].SetValueOn(new(0), default);
        Assert.Equal(new Block(17, 8), tree.GetValue(new(0)));
    }

    [Fact]
    public void SingleComponentPreservesInactiveDataButEmptyInputReturnsEmptyTree()
    {
        var tree = new VdbTree<int>(-1);
        tree.SetValueOn(new(0), 7); tree.SetValueOff(new(100, 0, 0), 99);
        var single = Assert.Single(Segmentation.SegmentActiveVoxels(tree));
        Assert.Equal(99, single.GetValue(new(100, 0, 0)));
        Assert.NotSame(tree.ProbeLeaf(new(100, 0, 0)), single.ProbeLeaf(new(100, 0, 0)));
        tree.SetValueOff(new(0));
        Assert.Empty(Segmentation.ExtractActiveVoxelSegmentMasks(tree));
        var empty = Assert.Single(Segmentation.SegmentActiveVoxels(tree));
        Assert.True(empty.IsEmpty); Assert.Equal(-1, empty.Background);
    }

    [Fact]
    public void SdfExpansionRequiresStrictlyIncreasingDistanceAndRestoresInactiveSign()
    {
        var tree = new VdbTree<float>(3);
        tree.SetValueOn(new(0), -0.1f); tree.SetValueOn(new(1, 0, 0), 0.1f);
        tree.SetValueOn(new(2, 0, 0), 0.2f); tree.SetValueOn(new(3, 0, 0), 0.2f);
        var result = Assert.Single(Segmentation.SegmentSdf(tree));
        Assert.True(result.IsValueOn(new(2, 0, 0)));
        Assert.False(result.IsValueOn(new(3, 0, 0)));
        Assert.Equal(3, result.GetValue(new(3, 0, 0)));
        Assert.True(tree.IsValueOn(new(3, 0, 0)));
    }

    [Fact]
    public void SeparateSpheresHaveIndependentSignedInteriors()
    {
        var grid = LevelSetAnalysisConformanceTests.Shape(9);
        var results = Segmentation.SegmentSdf(grid);
        Assert.Equal(2, results.Count);
        Assert.Contains(results, segment => segment.Tree.GetValue(new(-7, 0, 0)) < 0 && segment.Tree.GetValue(new(8, 0, 0)) > 0);
        Assert.Contains(results, segment => segment.Tree.GetValue(new(8, 0, 0)) < 0 && segment.Tree.GetValue(new(-7, 0, 0)) > 0);
        Assert.All(results, segment => { Assert.Equal(grid.Name, segment.Name); Assert.Equal("kept", segment.GetMetadata<string>("custom")); Assert.NotSame(grid.Transform, segment.Transform); });
    }

    [Fact]
    public void BooleanFalsePayloadsAreActiveAndCoordinateLimitsDoNotWrapConnectivity()
    {
        var tree = new VdbTree<bool>();
        tree.SetValueOn(new(int.MinValue, 0, 0), false); tree.SetValueOn(new(int.MaxValue, 0, 0), false);
        Assert.Equal(2, Segmentation.SegmentActiveVoxels(tree).Count);
    }

    [Theory, InlineData(17), InlineData(43), InlineData(811)]
    public void SeededSparseComponentsAgreeWithIndependentCoordinateFloodFill(int seed)
    {
        var random = new Random(seed); var points = new HashSet<Coord>();
        for (int i = 0; i < 180; ++i) points.Add(new(random.Next(-9, 10), random.Next(-3, 4), random.Next(-3, 4)));
        var tree = new VdbTree<int>(); foreach (Coord point in points) tree.SetValueOn(point, seed);
        var sizes = new List<ulong>(); var remaining = new HashSet<Coord>(points);
        while (remaining.Count > 0)
        {
            Coord first = remaining.First(); remaining.Remove(first); var pending = new Queue<Coord>(); pending.Enqueue(first); ulong size = 0;
            while (pending.TryDequeue(out Coord c))
            {
                ++size;
                foreach (Coord p in new Coord[] { new(c.X + 1, c.Y, c.Z), new(c.X - 1, c.Y, c.Z), new(c.X, c.Y + 1, c.Z), new(c.X, c.Y - 1, c.Z), new(c.X, c.Y, c.Z + 1), new(c.X, c.Y, c.Z - 1) }) if (remaining.Remove(p)) pending.Enqueue(p);
            }
            sizes.Add(size);
        }
        Assert.Equal(sizes.OrderDescending(), Segmentation.ExtractActiveVoxelSegmentMasks(tree).Select(mask => mask.ActiveVoxelCount));
    }
}
