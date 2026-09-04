using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
using Tedd.OpenVDB.Tree;

namespace Tedd.OpenVDB.Tests;

public sealed class PruneTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RootBackgroundErasureUsesNativeApproximateEquality(bool numeric)
    {
        var tree = new VdbTree<double>(1e-9);
        tree.AddTile(3, new Coord(0), 1e-9 + 1e-16, false);
        tree.AddTile(3, new Coord(4096, 0, 0), 1e-9 + 1e-16, true);
        tree.AddTile(3, new Coord(8192, 0, 0), 1e-9 + 1e-12, false);
        tree.AddTile(3, new Coord(-4096, 0, 0), double.NaN, false);
        if (numeric) Prune.Apply(tree, 0d); else Prune.Apply(tree);
        Assert.Equal(-1, tree.GetValueDepth(new Coord(0)));
        Assert.Equal(0, tree.GetValueDepth(new Coord(4096, 0, 0)));
        Assert.Equal(0, tree.GetValueDepth(new Coord(8192, 0, 0)));
        Assert.Equal(-1, tree.GetValueDepth(new Coord(-4096, 0, 0)));
        Assert.True(tree.IsValueOn(new Coord(4096, 0, 0)));
    }
    [Theory]
    [InlineData(254, 12)]
    [InlineData(256, 10)]
    [InlineData(258, 10)]
    public void TolerancePruningUsesLowerMedianNotFirstValue(int lowCount, int expected)
    {
        var tree = new VdbTree<int>();
        var leaf = tree.TouchLeaf(default);
        leaf.Fill(12, true);
        for (var index = 0; index < lowCount; ++index) leaf.SetValueOnly(index, 10);
        Prune.Apply(tree, 2);
        Assert.Equal(0, tree.LeafCount);
        Assert.Equal(expected, tree.GetValue(default));
        Assert.Equal(512UL, tree.ActiveVoxelCount);
    }

    [Fact]
    public void RangeToleranceDoesNotUseFirstValuePlusOrMinusTolerance()
    {
        var tree = new VdbTree<int>();
        var leaf = tree.TouchLeaf(default);
        leaf.Fill(10, true);
        leaf.SetValueOnly(1, 9); leaf.SetValueOnly(2, 11);
        Prune.Apply(tree, 1);
        Assert.Equal(1, tree.LeafCount);
        Prune.Apply(tree, 2);
        Assert.Equal(0, tree.LeafCount);
    }

    [Fact]
    public void InactivePruningUsesReplacementWhilePreservingExistingInactiveTiles()
    {
        var tree = new VdbTree<int>(3);
        tree.SetValueOff(new Coord(1), -1);
        tree.SetValueOn(new Coord(9, 1, 1), 4);
        tree.AddTile(1, new Coord(16, 0, 0), 99, false);
        Prune.PruneInactiveWithValue(tree, 7);
        Assert.Equal(7, tree.GetValue(new Coord(1)));
        Assert.Equal(99, tree.GetValue(new Coord(16, 0, 0)));
        Assert.Equal(4, tree.GetValue(new Coord(9, 1, 1)));
        Assert.Equal(1, tree.LeafCount);
        Assert.Equal(1UL, tree.ActiveVoxelCount);
    }

    [Fact]
    public void PruneTilesRetainsUniformLeafNodes()
    {
        var tree = new VdbTree<float>(3);
        tree.TouchLeaf(default).Fill(3, false);
        Prune.PruneTiles(tree, 0f);
        Assert.Equal(1, tree.LeafCount);
        Prune.Apply(tree, 0f);
        Assert.Equal(0, tree.RootEntryCount);
    }

    [Fact]
    public void NumericUniformityRejectsNaNAndOverflowWithoutDiscardingData()
    {
        var real = new VdbTree<float>();
        real.TouchLeaf(default).Fill(float.NaN, true);
        Prune.Apply(real, 1f);
        Assert.Equal(1, real.LeafCount);
        var integer = new VdbTree<long>();
        var leaf = integer.TouchLeaf(default); leaf.Fill(long.MinValue, true); leaf.SetValueOnly(1, long.MaxValue);
        Prune.Apply(integer, long.MaxValue);
        Assert.Equal(1, integer.LeafCount);
    }

    [Fact]
    public void LevelSetPruningUsesFirstVoxelSignAndRetainsActiveNodes()
    {
        var tree = new VdbTree<float>(3);
        tree.TouchLeaf(default).Fill(-1, false);
        tree.SetValueOnly(new Coord(7), 2);
        tree.SetValueOn(new Coord(9, 1, 1), 0.5f);
        Prune.PruneLevelSet(tree, 7f, -9f);
        Assert.Equal(-9f, tree.GetValue(default));
        Assert.Equal(-9f, tree.GetValue(new Coord(7)));
        Assert.Equal(0.5f, tree.GetValue(new Coord(9, 1, 1)));
        Assert.Equal(3f, tree.Background);
    }

    [Fact]
    public void StructuralPruningInvalidatesCachedLeafAccessors()
    {
        var tree = new VdbTree<int>(3);
        var oldLeaf = tree.TouchLeaf(default);
        oldLeaf.Fill(10, false);
        var accessor = tree.GetAccessor(); Assert.Equal(10, accessor.GetValue(default));
        Prune.PruneInactiveWithValue(tree, 7);
        oldLeaf.Fill(99, true);
        Assert.Equal(7, accessor.GetValue(default));
        accessor.SetValueOn(default, 12);
        Assert.Equal(12, tree.GetValue(default));
    }

    [Fact]
    public void ExactPruningSupportsBooleanAndUnmanagedBlockTypes()
    {
        var boolean = new VdbTree<bool>(); boolean.TouchLeaf(default).Fill(true, false);
        Prune.Apply(boolean); Assert.Equal(0, boolean.LeafCount); Assert.True(boolean.GetValue(default));
        var blocks = new VdbTree<Block>(); blocks.TouchLeaf(default).Fill(new Block(7, 11), true);
        Prune.Apply(blocks); Assert.Equal(0, blocks.LeafCount); Assert.Equal(new Block(7, 11), blocks.GetValue(default));
    }

    [Fact]
    public void InvalidWidthsAndToleranceAreRejectedBeforeMutation()
    {
        var tree = new VdbTree<float>(3); tree.SetValueOn(default, 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => Prune.Apply(tree, -1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => Prune.Apply(tree, float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => Prune.PruneLevelSet(tree, -1f, -2f));
        Assert.Throws<ArgumentOutOfRangeException>(() => Prune.PruneLevelSet(tree, 1f, 0f));
        Assert.Throws<ArgumentException>(() => Prune.PruneLevelSet(new VdbTree<float>(-1)));
        Assert.Equal(1, tree.GetValue(default));
    }

    private readonly record struct Block(ushort Material, ushort Flags);
}
