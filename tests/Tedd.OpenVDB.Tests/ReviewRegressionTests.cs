using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;
using Tedd.OpenVDB.Tools;

namespace Tedd.OpenVDB.Tests;

public class ReviewRegressionTests
{
    [Theory]
    [InlineData(0, 1, true)]
    [InlineData(0, 1, false)]
    [InlineData(1, 0, true)]
    [InlineData(0, 0, false)]
    [InlineData(0, -1, true)]
    public void DenseCopyFromAliasedLeafSpanPreservesSourceValues(int sourceOffset, int destinationZ, bool active)
    {
        var tree = new VdbTree<int>(-1);
        var leaf = tree.TouchLeaf(Coord.Zero);
        for (int i = 0; i < 8; ++i) leaf.SetValueOn(i, 10 + i);
        int[] expected = leaf.Values.Slice(sourceOffset, 7).ToArray();
        var box = new CoordBBox(new Coord(0, 0, destinationZ), new Coord(0, 0, destinationZ + 6));

        tree.CopyFromDense(box, leaf.Values.Slice(sourceOffset, 7), active);

        int[] actual = new int[7];
        tree.CopyToDense(box, actual);
        Assert.Equal(expected, actual);
        foreach (Coord coordinate in box) Assert.Equal(active, tree.IsValueOn(coordinate));
    }
    [Fact]
    public void CallbackThrowLateInTraversalLeavesBothTreesAndAccessorsIntact()
    {
        var a = new VdbTree<int>(-91);
        var b = new VdbTree<int>(-73);
        a.SetValueOn(new Coord(-1), 11);
        b.SetValueOn(new Coord(int.MaxValue), 17);
        var accessorA = a.GetAccessor();
        var accessorB = b.GetAccessor();
        accessorA.GetValue(new Coord(-1));
        accessorB.GetValue(new Coord(int.MaxValue));
        int calls = 0;

        Assert.Throws<InvalidOperationException>(() => a.Combine(b, (VoxelState<int> left, VoxelState<int> right) =>
        {
            ++calls;
            // Fail only after substantial result construction, including one complete root branch.
            if (left.Value == -91 && right.Value == -73 && calls > 70000) throw new InvalidOperationException();
            return new(left.Value + right.Value, left.IsActive || right.IsActive);
        }));

        Assert.True(calls > 70000);
        Assert.Equal(-91, a.Background);
        Assert.Equal(-73, b.Background);
        Assert.Equal(11, accessorA.GetValue(new Coord(-1)));
        Assert.Equal(17, accessorB.GetValue(new Coord(int.MaxValue)));
        Assert.Equal(1UL, a.ActiveVoxelCount);
        Assert.Equal(1UL, b.ActiveVoxelCount);
        accessorA.SetValueOn(new Coord(-1), 13);
        accessorB.SetValueOn(new Coord(int.MaxValue), 19);
        Assert.Equal(13, a.GetValue(new Coord(-1)));
        Assert.Equal(19, b.GetValue(new Coord(int.MaxValue)));
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void RemovedTopologyBranchesInvalidateAllCachedAliases(int operation)
    {
        var tree = new VdbTree<int>(-1);
        var coordinate = new Coord(int.MinValue, int.MaxValue, -1);
        tree.SetValueOn(coordinate, 13);
        var first = tree.GetAccessor();
        var second = tree.GetAccessor();
        first.GetValue(coordinate);
        second.GetValue(coordinate);
        var other = new VdbTree<int>(3);
        if (operation == 0) tree.TopologyIntersection(other);
        if (operation == 1)
        {
            other.AddTile(3, coordinate, 7, true);
            tree.TopologyDifference(other);
        }
        if (operation == 2) Composite.CompSum(tree, other);
        int expected = operation == 2 ? 16 : -1;
        Assert.Equal(expected, first.GetValue(coordinate));
        Assert.Equal(expected, second.GetValue(coordinate));
        second.SetValueOn(coordinate, 21);
        Assert.Equal(21, tree.GetValue(coordinate));
        Assert.Equal(21, first.GetValue(coordinate));
    }
}
