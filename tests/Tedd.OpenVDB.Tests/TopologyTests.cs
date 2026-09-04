using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tests;

public class TopologyTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void BooleanTopologyMatchesDenseReferenceAndPreservesValues(int operation)
    {
        var a = new VdbTree<int>(-1);
        var b = new VdbTree<float>(100);
        var boxA = new CoordBBox(new(-8, -8, -8), new(7, 7, 7));
        var boxB = new CoordBBox(new(-4, -10, -2), new(9, 3, 10));
        a.Fill(boxA, 7); b.Fill(boxB, 99);
        a.SetValueOff(new(0, 0, 0), 123);
        if (operation == 0) a.TopologyUnion(b);
        if (operation == 1) a.TopologyIntersection(b);
        if (operation == 2) a.TopologyDifference(b);
        ulong count = 0;
        foreach (var c in new CoordBBox(new(-12, -12, -12), new(12, 12, 12)))
        {
            bool onA = boxA.Contains(c) && c != default;
            bool onB = boxB.Contains(c);
            bool expected = operation switch { 0 => onA || onB, 1 => onA && onB, _ => onA && !onB };
            Assert.Equal(expected, a.IsValueOn(c));
            if (operation == 0 || expected) Assert.Equal(c == default ? 123 : boxA.Contains(c) ? 7 : -1, a.GetValue(c));
            if (expected) count++;
        }
        Assert.Equal(count, a.ActiveVoxelCount);
    }
    [Fact]
    public void IntersectionRootTileWithOneVoxelDoesNotEnumerateBillionsOfVoxels()
    {
        var a = new VdbTree<int>(); a.AddTile(3, default, 8, true);
        var b = new VdbTree<byte>(); b.SetValueOn(new(11, 12, 13), 1);
        a.TopologyIntersection(b);
        Assert.Equal(1UL, a.ActiveVoxelCount);
        Assert.Equal(8, a.GetValue(new(11, 12, 13)));
        Assert.Equal(8, a.GetValue(new(100, 100, 100)));
        Assert.False(a.IsValueOn(new(100, 100, 100)));
        Assert.Equal(1, a.LeafCount);
    }
    [Fact]
    public void SelfDifferenceClearsActivityButKeepsValues()
    {
        var tree = new VdbTree<int>(); tree.AddTile(2, default, 7, true);
        tree.SetValueOn(new(3, 4, 5), 11);
        tree.TopologyDifference(tree);
        Assert.Equal(0UL, tree.ActiveVoxelCount);
        Assert.Equal(11, tree.GetValue(new(3, 4, 5)));
        Assert.Equal(7, tree.GetValue(default));
    }
    [Fact]
    public void TopologyComparisonIncludesRepresentationButIgnoresValues()
    {
        var a = new VdbTree<int>(); a.AddTile(1, default, 7, true);
        var b = new VdbTree<float>(); b.AddTile(1, default, 12, true); b.TouchLeaf(default);
        Assert.False(a.HasSameTopology(b));
        a.TouchLeaf(default);
        Assert.True(a.HasSameTopology(b));
        b.SetValueOff(new(1, 2, 3));
        Assert.False(a.HasSameTopology(b));
    }
}
