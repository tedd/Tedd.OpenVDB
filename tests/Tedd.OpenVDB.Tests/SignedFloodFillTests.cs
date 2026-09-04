using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
using Tedd.OpenVDB.Tree;

namespace Tedd.OpenVDB.Tests;

public sealed class SignedFloodFillTests
{
    [Fact]
    public void LeafScanlinePropagatesSignsWithoutChangingActiveSamples()
    {
        var tree = new VdbTree<double>(3);
        tree.SetValueOn(new Coord(0, 0, 1), -0.5);
        tree.SetValueOn(new Coord(0, 0, 6), 0.5);
        SignedFloodFill.WithValues(tree, -7d, 9d);
        for (var z = 0; z < 8; ++z)
        {
            var point = new Coord(0, 0, z);
            Assert.Equal(z == 1 || z == 6, tree.IsValueOn(point));
            Assert.Equal(z == 1 ? -0.5 : z == 6 ? 0.5 : z < 6 ? -9 : 7, tree.GetValue(point));
        }
        Assert.Equal(7, tree.Background);
        Assert.Equal(2UL, tree.ActiveVoxelCount);
    }

    [Fact]
    public void ClosedSphereFloodFillAssignsNegativeInteriorAndPositiveExterior()
    {
        var tree = new VdbTree<double>(3);
        for (var x = -12; x <= 12; ++x)
            for (var y = -12; y <= 12; ++y)
                for (var z = -12; z <= 12; ++z)
                {
                    var distance = System.Math.Sqrt(x * x + y * y + z * z) - 10;
                    if (System.Math.Abs(distance) <= 1) tree.SetValueOn(new Coord(x, y, z), distance);
                }
        var active = tree.ActiveVoxelCount;
        SignedFloodFill.Apply(tree);
        Assert.False(tree.IsValueOn(default)); Assert.Equal(-3, tree.GetValue(default));
        Assert.Equal(-3, tree.GetValue(new Coord(5, 0, 0)));
        Assert.Equal(3, tree.GetValue(new Coord(15, 0, 0)));
        Assert.Equal(3, tree.GetValue(new Coord(-15, 0, 0)));
        Assert.True(tree.IsValueOn(new Coord(10, 0, 0))); Assert.Equal(0, tree.GetValue(new Coord(10, 0, 0)));
        Assert.Equal(active, tree.ActiveVoxelCount);
    }

    [Fact]
    public void RootScanlineFillsOnlyGapsBracketedByNegativeChildNodes()
    {
        var tree = new VdbTree<float>(3);
        tree.TouchLeaf(default).Fill(-1, false);
        tree.TouchLeaf(new Coord(0, 0, 8192)).Fill(-1, false);
        tree.AddTile(3, new Coord(0, 0, 4096), 123, true);
        SignedFloodFill.Apply(tree);
        Assert.Equal(-3, tree.GetValue(new Coord(0, 0, 4096)));
        Assert.False(tree.IsValueOn(new Coord(0, 0, 4096)));
        Assert.Equal(-3, tree.GetValue(new Coord(1, 2, 6000)));
        Assert.Equal(3, tree.GetValue(new Coord(4096, 0, 4096)));
        Assert.Equal(2, tree.LeafCount);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void MinimumLevelPreservesLowerLevels(int minLevel)
    {
        var tree = new VdbTree<float>(3);
        var leaf = tree.TouchLeaf(default);
        leaf.Fill(77, false); leaf.SetValueOn(new Coord(0, 0, 1), -0.5f);
        SignedFloodFill.WithValues(tree, 7f, -9f, minLevel);
        Assert.Equal(77, leaf.GetValue(0));
        Assert.Equal(-0.5f, leaf.GetValue(new Coord(0, 0, 1)));
        Assert.Equal(minLevel == 4 ? 3 : 7, tree.Background);
    }

    [Fact]
    public void EmptyAndRootTileOnlyTreesKeepNativeEarlyReturnBehavior()
    {
        var empty = new VdbTree<float>(-3);
        SignedFloodFill.WithValues(empty, 7f, -9f);
        Assert.Equal(-3, empty.Background);
        var tile = new VdbTree<float>(3); tile.AddTile(3, default, -1, false);
        SignedFloodFill.WithValues(tile, 7f, -9f);
        Assert.Equal(3, tile.Background); Assert.Equal(-1, tile.GetValue(default));
    }

    [Fact]
    public void InactiveLeafUsesFirstValueSignAndSignedIntegralGridsAreSupported()
    {
        var tree = new VdbTree<int>(3);
        var leaf = tree.TouchLeaf(default); leaf.Fill(99, false); leaf.SetValueOnly(0, -1);
        SignedFloodFill.WithValues(tree, 7, -9);
        Assert.Equal(-9, leaf.GetValue(511)); Assert.Equal(0UL, tree.ActiveVoxelCount);
    }
}
