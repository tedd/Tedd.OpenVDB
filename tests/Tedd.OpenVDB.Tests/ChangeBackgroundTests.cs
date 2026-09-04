using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tests;

public sealed class ChangeBackgroundTests
{
    [Fact]
    public void LevelSetUpdatesActiveInternalTilesButPreservesActiveRootAndLeafValues()
    {
        var grid = LevelSetTrackingConformanceTests.Shape<double>(2);
        ChangeBackground.AsymmetricLevelSet(grid.Tree, 4, -5);
        Assert.Equal(-5, grid.Tree.GetValue(new(16, 0, 0)));
        Assert.Equal(4, grid.Tree.GetValue(new(128, 0, 0)));
        Assert.Equal(-0.5, grid.Tree.GetValue(new(4096, 0, 0)));
        Assert.Equal(-5, grid.Tree.GetValue(new(8192, 0, 0)));
        Assert.Equal(-3, grid.Tree.GetValue(new(0)));
        Assert.Equal(4, grid.Tree.GetValue(new(0, 1, 0)));
        Assert.True(grid.Tree.IsValueOn(new(16, 0, 0)));
    }
    [Fact]
    public void GeneralChangeMatchesApproximateInactiveValuesOfEitherSign()
    {
        var tree = new VdbTree<double>(3);
        tree.SetValueOff(new(0), 3 + 4e-16); tree.SetValueOff(new(1, 0, 0), -3); tree.SetValueOff(new(2, 0, 0), 7); tree.SetValueOn(new(3, 0, 0), 3);
        ChangeBackground.Apply(tree, 5);
        Assert.Equal(5, tree.GetValue(new(0))); Assert.Equal(-5, tree.GetValue(new(1, 0, 0)));
        Assert.Equal(7, tree.GetValue(new(2, 0, 0))); Assert.Equal(3, tree.GetValue(new(3, 0, 0)));
    }
    [Fact]
    public void GeneralSameBackgroundCallStillAppliesNativeNanEquality()
    {
        var tree = new VdbTree<float>(3); tree.SetValueOff(new(0), float.NaN);
        ChangeBackground.Apply(tree, 3);
        Assert.Equal(3, tree.GetValue(new(0))); Assert.False(tree.IsValueOn(new(0)));
    }
    [Fact]
    public void InvalidLevelSetBandIsRejectedBeforeChangingAnyValues()
    {
        var tree = new VdbTree<float>(3); tree.SetValueOff(new(0), -2);
        Assert.Throws<ArgumentOutOfRangeException>(() => ChangeBackground.LevelSet(tree, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ChangeBackground.AsymmetricLevelSet(tree, -1, -3));
        Assert.Equal(-2, tree.GetValue(new(0))); Assert.Equal(3, tree.Background);
    }
}
