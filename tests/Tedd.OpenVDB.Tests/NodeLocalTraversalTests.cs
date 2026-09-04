// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tests;

public class NodeLocalTraversalTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ParallelSiblingTopologyEditsPreserveDirectTraversal(bool children, bool reduction)
    {
        var tree = new VdbTree<int>();
        tree.TouchLeaf(Coord.Zero); tree.TouchLeaf(new(4096, 0, 0));
        using var acquired = new ManualResetEventSlim();
        using var changed = new ManualResetEventSlim();
        bool Visit(TreeNode<int> node, int index)
        {
            if (node.IsRoot) return true;
            if (index == 0)
            {
                if (children)
                {
                    using var iterator = node.EnumerateChildren().GetEnumerator();
                    Assert.True(iterator.MoveNext()); TreeNode<int> child = iterator.Current;
                    acquired.Set(); Assert.True(changed.Wait(TimeSpan.FromSeconds(10)));
                    Assert.True(child.IsValid); Assert.Equal(Coord.Zero, child.Origin);
                    Assert.False(iterator.MoveNext()); // Includes the final version check.
                }
                else
                {
                    using var iterator = node.EnumerateValues(ValueIterationMode.All).GetEnumerator();
                    Assert.True(iterator.MoveNext()); TreeValue<int> value = iterator.Current;
                    acquired.Set(); Assert.True(changed.Wait(TimeSpan.FromSeconds(10)));
                    Assert.True(value.IsValid); Assert.Equal(0, value.Value);
                    value.SetValueOn(7); Assert.Equal(7, value.Value);
                    Assert.True(iterator.MoveNext());
                }
            }
            else
            {
                Assert.True(acquired.Wait(TimeSpan.FromSeconds(10)));
                node.TouchLeaf(node.Origin + new Coord(128, 0, 0));
                changed.Set();
            }
            return false;
        }
        var manager = new DynamicNodeManager<int>(tree, levels: 1);
        if (reduction)
            Assert.Equal(3, manager.ReduceTopDown(() => 0, (count, node, index) => (count + 1, Visit(node, index)), (a, b) => a + b, parallel: true));
        else manager.ForEachTopDown(Visit, parallel: true);
        Assert.Equal(3, tree.LeafCount);
    }

    [Fact]
    public void DescendantEditsPreserveTheParentsDirectTable()
    {
        var tree = new VdbTree<int>(); tree.TouchLeaf(Coord.Zero);
        var upper = tree.ProbeNode(Coord.Zero, 2)!.Value;
        using var children = upper.EnumerateChildren().GetEnumerator(); Assert.True(children.MoveNext());
        using var values = upper.EnumerateValues(ValueIterationMode.All).GetEnumerator(); Assert.True(values.MoveNext());
        TreeValue<int> value = values.Current;
        tree.ProbeNode(Coord.Zero, 1)!.Value.TouchLeaf(new(8, 0, 0));
        Assert.True(value.IsValid); value.SetValueOnly(19); Assert.Equal(19, value.Value);
        Assert.True(values.MoveNext()); Assert.False(children.MoveNext());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void SiblingEditsPreserveLocalHandlesAtEveryNonRootLevel(int level)
    {
        var tree = new VdbTree<int>(); tree.TouchLeaf(Coord.Zero);
        var node = tree.ProbeNode(Coord.Zero, level)!.Value;
        using var values = node.EnumerateValues(ValueIterationMode.All).GetEnumerator(); Assert.True(values.MoveNext());
        TreeValue<int> value = values.Current;
        Coord sibling = level switch { 0 => new(8, 0, 0), 1 => new(128, 0, 0), _ => new(4096, 0, 0) };
        tree.TouchLeaf(sibling);
        Assert.True(value.IsValid); value.SetValueOn(23); Assert.Equal(23, value.Value);
        Assert.True(values.MoveNext());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void TileChildTileCyclePermanentlyInvalidatesDirectIteration(int level)
    {
        var tree = new VdbTree<int>(); tree.TouchLeaf(Coord.Zero);
        var node = tree.ProbeNode(Coord.Zero, level)!.Value;
        using var values = node.EnumerateValues(ValueIterationMode.All).GetEnumerator(); Assert.True(values.MoveNext());
        TreeValue<int> value = values.Current;
        using var children = node.EnumerateChildren().GetEnumerator(); Assert.True(children.MoveNext());
        Coord coordinate = value.Coordinate; int before = node.ChildCount;
        node.TouchLeaf(coordinate); node.AddTile(level, coordinate, 0, false);
        Assert.Equal(before, node.ChildCount); Assert.True(node.IsValid); Assert.False(value.IsValid);
        Assert.Throws<InvalidOperationException>(() => value.SetValueOnly(99));
        Assert.Throws<InvalidOperationException>(() => values.MoveNext());
        Assert.Throws<InvalidOperationException>(() => children.MoveNext());
        Assert.Equal(0, tree.GetValue(coordinate));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void DetachmentInvalidatesAllLocalOperations(int level)
    {
        var tree = new VdbTree<int>(); tree.TouchLeaf(Coord.Zero);
        var node = tree.ProbeNode(Coord.Zero, level)!.Value;
        using var values = node.EnumerateValues(ValueIterationMode.All).GetEnumerator(); Assert.True(values.MoveNext());
        TreeValue<int> value = values.Current;
        using var children = node.EnumerateChildren().GetEnumerator();
        if (level > 0) Assert.True(children.MoveNext());
        tree.AddTile(level + 1, Coord.Zero, 31, false);
        Assert.False(node.IsValid); Assert.False(value.IsValid);
        Assert.Throws<InvalidOperationException>(() => value.Value);
        Assert.Throws<InvalidOperationException>(() => value.SetActiveState(true));
        Assert.Throws<InvalidOperationException>(() => values.MoveNext());
        Assert.Throws<InvalidOperationException>(() => children.MoveNext());
        Assert.Equal(31, tree.GetValue(Coord.Zero));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ValueAndActivityEditsPreserveLocalIteration(int level)
    {
        var tree = new VdbTree<int>(); tree.TouchLeaf(Coord.Zero);
        var node = tree.ProbeNode(Coord.Zero, level)!.Value;
        using var values = node.EnumerateValues(ValueIterationMode.All).GetEnumerator(); Assert.True(values.MoveNext());
        TreeValue<int> value = values.Current;
        value.SetValueOn(4); value.SetActiveState(false); value.SetValueOnly(8);
        if (level == 0) node.AddTile(0, value.Coordinate, 9, true);
        else node.AddTile(level, value.Coordinate, 9, true);
        Assert.True(value.IsValid); Assert.Equal(9, value.Value); Assert.True(value.IsActive);
        Assert.True(values.MoveNext());
    }

    [Theory]
    [InlineData("replace")]
    [InlineData("remove")]
    [InlineData("union")]
    [InlineData("intersection")]
    [InlineData("merge")]
    [InlineData("prune")]
    public void ExistingMutationEntryPointsInvalidateDirectTable(string operation)
    {
        var tree = new VdbTree<int>(); tree.TouchLeaf(Coord.Zero);
        // Retain a nonuniform leaf so pruning tests only remove the other child.
        tree.SetValueOn(new(7, 7, 7), 1);
        var node = tree.ProbeNode(Coord.Zero, 1)!.Value;
        if (operation == "prune") tree.TouchLeaf(new(8, 0, 0));
        TreeValue<int> value = node.EnumerateValues(ValueIterationMode.All).First();
        var other = new VdbTree<int>();
        switch (operation)
        {
            case "replace": tree.AddLeaf(new LeafNode<int>(Coord.Zero, 5)); break;
            case "remove": tree.StealLeaf(Coord.Zero); break;
            case "union": other.SetValueOn(new(8, 0, 0), 1); tree.TopologyUnion(other); break;
            case "intersection": other.SetValueOn(new(8, 0, 0), 1); tree.TopologyIntersection(other); break;
            case "merge": other.SetValueOn(new(8, 0, 0), 1); tree.Merge(other); break;
            case "prune": tree.Prune(); break;
        }
        Assert.True(node.IsValid); Assert.False(value.IsValid);
        Assert.Throws<InvalidOperationException>(() => value.Value);
    }

    [Fact]
    public void FinalLeafYieldDetectsDetachment()
    {
        var tree = new VdbTree<int>(); tree.SetValueOn(Coord.Zero, 1);
        using var values = tree.ProbeNode(Coord.Zero, 0)!.Value.EnumerateValues().GetEnumerator();
        Assert.True(values.MoveNext()); tree.StealLeaf(Coord.Zero);
        Assert.Throws<InvalidOperationException>(() => values.MoveNext());
    }

    [Fact]
    public void TreeWideAndRootIteratorsRetainGlobalInvalidation()
    {
        var tree = new VdbTree<int>(); tree.TouchLeaf(Coord.Zero); tree.AddTile(3, new(-4096, 0, 0), 1, true);
        using var all = tree.EnumerateValueHandles(ValueIterationMode.All).GetEnumerator(); Assert.True(all.MoveNext());
        TreeValue<int> global = all.Current;
        using var root = tree.Root.EnumerateValues().GetEnumerator(); Assert.True(root.MoveNext()); TreeValue<int> rootValue = root.Current;
        using var nodes = tree.EnumerateNodes().GetEnumerator(); Assert.True(nodes.MoveNext());
        tree.TouchLeaf(new(4096, 0, 0));
        Assert.False(global.IsValid); Assert.False(rootValue.IsValid);
        Assert.Throws<InvalidOperationException>(() => all.MoveNext());
        Assert.Throws<InvalidOperationException>(() => root.MoveNext());
        Assert.Throws<InvalidOperationException>(() => nodes.MoveNext());
    }
}
