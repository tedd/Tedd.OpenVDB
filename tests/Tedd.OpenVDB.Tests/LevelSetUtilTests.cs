using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
using Tedd.OpenVDB.Tree;

namespace Tedd.OpenVDB.Tests;

public sealed class LevelSetUtilTests
{
    [Fact]
    public void InteriorMaskUsesInactiveValuesAndIndependentTruePayload()
    {
        var source = new Grid<float>(3) { Name = "source", Transform = Transform.CreateLinearTransform(0.5) };
        source.Tree.SetValueOff(new(-1, 2, 3), -2);
        source.Tree.SetValueOn(new(0, 2, 3), 0);
        var mask = LevelSetUtil.SdfInteriorMask(source);
        Assert.True(mask.Tree.GetValue(new(-1, 2, 3)));
        Assert.True(mask.Tree.IsValueOn(new(-1, 2, 3)));
        Assert.False(mask.Tree.IsValueOn(new(0, 2, 3)));
        Assert.Equal(string.Empty, mask.Name);
        Assert.NotSame(source.Transform, mask.Transform);
        Assert.Equal(source.Transform.VoxelSize, mask.Transform.VoxelSize);
        mask.Tree.SetValueOff(new(-1, 2, 3), false);
        Assert.Equal(-2, source.Tree.GetValue(new(-1, 2, 3)));
    }

    [Theory, InlineData(1), InlineData(2), InlineData(3)]
    public void InteriorMaskDoesNotSeedNativeTileOnlyPaths(int level)
    {
        var source = new VdbTree<double>(3);
        source.AddTile(level, new(0), -3, false);
        Assert.True(LevelSetUtil.SdfInteriorMask(source).IsEmpty);
    }

    [Fact]
    public void IsovalueAboveBackgroundIsClampedBeforeClassifyingStoredVoxels()
    {
        var source = new VdbTree<double>(3);
        source.SetValueOn(new(0), 2.5);
        var mask = LevelSetUtil.SdfInteriorMask(source, 99);
        Assert.Equal(1UL, mask.ActiveVoxelCount);
        Assert.False(mask.GetValue(new(1, 0, 0)));
    }

    [Fact]
    public void IsosurfaceCandidatesRequireActivityButNeighborValuesDoNot()
    {
        var tree = new VdbTree<float>(3);
        tree.SetValueOff(new(0), -1);
        tree.SetValueOn(new(1, 0, 0), 1);
        var result = LevelSetUtil.ExtractIsosurfaceMask(tree);
        Assert.Equal(1UL, result.ActiveVoxelCount);
        Assert.False(result.IsValueOn(new(0)));
        Assert.True(result.IsValueOn(new(1, 0, 0)));
    }

    [Fact]
    public void FogTransfersLeavesAndPreservesNativeSharedOriginalTreeBehavior()
    {
        var grid = new Grid<float>(3) { Name = "fog", GridClass = GridClass.LevelSet, Transform = Transform.CreateLinearTransform(0.25) };
        grid.SetMetadata("custom", 17);
        grid.Tree.SetValueOn(new(0), -3);
        grid.Tree.SetValueOff(new(1, 0, 0), -1.5f);
        var oldTree = grid.Tree;
        var alias = new Grid<float>(oldTree);
        var leaf = oldTree.ProbeLeaf(new(0));
        var accessor = oldTree.GetAccessor();
        Assert.Equal(-3, accessor.GetValue(new(0)));
        LevelSetUtil.SdfToFogVolume(grid);
        Assert.NotSame(oldTree, grid.Tree);
        Assert.Same(oldTree, alias.Tree);
        Assert.Null(oldTree.ProbeLeaf(new(0)));
        Assert.Same(leaf, grid.Tree.ProbeLeaf(new(0)));
        Assert.Equal(3, accessor.GetValue(new(0)));
        Assert.Equal(1, grid.Tree.GetValue(new(0)));
        Assert.Equal(0.5f, grid.Tree.GetValue(new(1, 0, 0)));
        Assert.True(grid.Tree.IsValueOn(new(1, 0, 0)));
        Assert.Equal(GridClass.FogVolume, grid.GridClass);
        Assert.Equal("fog", grid.Name);
        Assert.Equal(17, grid.GetMetadata<int>("custom"));
        Assert.Equal(new Vec3d(0.25), grid.Transform.VoxelSize);
    }

    [Fact]
    public void FogPreservesNativeShallowAndZeroCutoffBehavior()
    {
        var grid = new Grid<float>(3);
        grid.Tree.SetValueOn(new(0), -2);
        grid.Tree.SetValueOn(new(1, 0, 0), 0);
        var zero = grid.DeepCopy();
        LevelSetUtil.SdfToFogVolume(grid, 0.5f);
        Assert.Equal(4, grid.Tree.GetValue(new(0)));
        Assert.False(grid.Tree.IsValueOn(new(1, 0, 0)));
        LevelSetUtil.SdfToFogVolume(zero, 0);
        Assert.True(float.IsPositiveInfinity(zero.Tree.GetValue(new(0))));
        Assert.True(float.IsNaN(zero.Tree.GetValue(new(1, 0, 0))));
        Assert.False(zero.Tree.IsValueOn(new(1, 0, 0)));
    }

    [Fact]
    public void FogTileOnlyRootInteriorSurvivesWhileLowerInteriorDoesNotSeedOutput()
    {
        var root = new Grid<float>(3); root.Tree.AddTile(3, new(0), -3, false);
        var lower = new Grid<float>(3); lower.Tree.AddTile(1, new(0), -3, false);
        LevelSetUtil.SdfToFogVolume(root); LevelSetUtil.SdfToFogVolume(lower);
        Assert.Equal(1UL << 36, root.ActiveVoxelCount);
        Assert.Equal(0, root.Tree.LeafCount);
        Assert.Equal(1, root.Tree.GetValue(new(0)));
        Assert.True(lower.IsEmpty);
    }

    [Fact]
    public void HalfInteriorAndFogConversionsRetainScalarSupport()
    {
        var grid = new Grid<Half>((Half)3);
        grid.Tree.SetValueOff(new(0), (Half)(-2));
        Assert.True(LevelSetUtil.SdfInteriorMask(grid).Tree.GetValue(new(0)));
        LevelSetUtil.SdfToFogVolume(grid);
        Assert.Equal((Half)1, grid.Tree.GetValue(new(0)));
    }

    [Fact]
    public void NullInputsAreRejectedBeforeMutation()
    {
        Assert.Throws<ArgumentNullException>(() => LevelSetUtil.SdfInteriorMask<float>((VdbTree<float>)null!));
        Assert.Throws<ArgumentNullException>(() => LevelSetUtil.ExtractIsosurfaceMask<float>((Grid<float>)null!));
        Assert.Throws<ArgumentNullException>(() => LevelSetUtil.SdfToFogVolume<float>(null!));
    }
}
