// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tests;

public class NodeManagerTests
{
    [Fact]
    public void CacheCountsLevelsAndClearMatchAllocatedNodes()
    {
        var tree = Setup();
        for (int levels = 0; levels <= 3; levels++)
        {
            var manager = new NodeManager<int>(tree, levels); var expected = tree.NodeCount();
            for (int level = 0; level < 3; level++) Assert.Equal(level >= 3 - levels ? expected[level] : 0, manager.GetNodeCount(level));
            Assert.Equal(expected.Take(3).Skip(3 - levels).Sum(), manager.NodeCount);
            var order = new List<int>(); manager.ForEachTopDown(node => order.Add(node.Level)); Assert.Equal(3, order[0]); Assert.Equal(order.OrderDescending(), order);
            order.Clear(); manager.ForEachBottomUp(node => order.Add(node.Level)); Assert.Equal(3, order[^1]); Assert.Equal(order.Order(), order);
            manager.Clear(); Assert.Equal(0, manager.NodeCount); order.Clear(); manager.ForEachTopDown(node => order.Add(node.Level)); Assert.Equal([3], order);
            manager.Rebuild(); Assert.Equal(expected.Take(3).Skip(3 - levels).Sum(), manager.NodeCount);
        }
    }
    [Fact]
    public void DetachedHandlesFailButUnrelatedEditsPreserveNodeIdentity()
    {
        var tree = Setup(); var node = tree.ProbeNode(new(1, 2, 3), 0)!.Value; var leaf = node.Leaf;
        tree.SetValueOn(new(40960, 0, 0), 1); Assert.True(node.IsValid); Assert.Same(leaf, node.Leaf);
        tree.AddTile(1, new(0, 0, 0), 4, true); Assert.False(node.IsValid); Assert.Throws<InvalidOperationException>(() => node.GetValue(new(1, 2, 3)));
        Assert.False(default(TreeNode<int>).IsValid); Assert.Throws<InvalidOperationException>(() => default(TreeNode<int>).TouchLeaf(default));
    }
    [Fact]
    public void StaticManagersRejectTopologyChangesAndRebuildAfterGc()
    {
        var tree = Setup(); var manager = new NodeManager<int>(new LeafManager<int>(tree));
        var reference = manager.GetNode(0, 0); tree.TouchLeaf(new(40960, 0, 0));
        Assert.False(manager.IsValid); Assert.Throws<InvalidOperationException>(() => manager.ForEachTopDown(_ => { }));
        manager.Rebuild(); GC.Collect(2, GCCollectionMode.Forced, true, true); Assert.True(reference.IsValid); Assert.Equal(tree.LeafCount, manager.GetNodeCount(0));
        Assert.Throws<InvalidOperationException>(() => manager.ForEachTopDown(node => { if (node.IsRoot) tree.Clear(); }));
    }
    [Fact]
    public void ValuesAreOwnedExactlyOnceAndMutationsPreserveTopology()
    {
        var tree = Setup(); var manager = new NodeManager<int>(tree); var counts = tree.NodeCount(); ulong before = tree.ActiveVoxelCount;
        long visited = 0;
        manager.ForEachBottomUp(node => { foreach (var value in node.EnumerateValues(ValueIterationMode.All)) { value.SetValueOnly(value.Value + 3); visited++; } });
        Assert.Equal(tree.EnumerateValues(ValueIterationMode.All).LongCount(), visited); Assert.Equal(before, tree.ActiveVoxelCount); Assert.Equal(counts, tree.NodeCount());
        Assert.Equal(12, tree.GetValue(new(1, 2, 3))); Assert.Equal(10, tree.GetValue(new(-4096, 0, 0)));
        foreach (var node in tree.EnumerateNodes()) { Assert.Equal(node.ValueCount, node.EnumerateValues(ValueIterationMode.All).Count()); Assert.Equal(node.ActiveValueCount, node.EnumerateValues().Count()); }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ParallelNodeReductionPreservesAllNodesAndLevelBarriers(bool topDown)
    {
        var tree = Setup(); for (int i = 0; i < 60; i++) tree.SetValueOn(new(i * 128, 24, 8), i);
        var manager = new NodeManager<int>(tree); var seen = new long[4];
        void Visit(TreeNode<int> node, int index)
        {
            int previous = topDown ? node.Level + 1 : node.Level - 1;
            if (previous is >= 0 and <= 3) Assert.Equal(previous == 3 ? 1 : manager.GetNodeCount(previous), Volatile.Read(ref seen[previous]));
            Interlocked.Increment(ref seen[node.Level]);
        }
        if (topDown) manager.ForEachTopDown(Visit, true, 4); else manager.ForEachBottomUp(Visit, true, 4);
        Assert.Equal(tree.NodeCount(), seen);
        long[] Reduce(long[] acc, TreeNode<int> node, int index) { acc[node.Level]++; return acc; }
        long[] Join(long[] a, long[] b) { for (int i = 0; i < 4; i++) a[i] += b[i]; return a; }
        long[] result = topDown ? manager.ReduceTopDown(() => new long[4], Reduce, Join, true, 7) : manager.ReduceBottomUp(() => new long[4], Reduce, Join, true, 7);
        Assert.Equal(tree.NodeCount(), result);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DynamicTraversalDiscoversNewChildrenAndOmitsRejectedDescendants(bool parallel)
    {
        var tree = new VdbTree<int>(); tree.AddTile(2, new(0, 0, 0), 2, true); tree.AddTile(2, new(4096, 0, 0), 3, false);
        var visited = new System.Collections.Concurrent.ConcurrentBag<(int Level, Coord Origin)>();
        new DynamicNodeManager<int>(tree).ForEachTopDown((node, index) =>
        {
            visited.Add((node.Level, node.Origin));
            if (node.Level is 1 or 2) node.TouchLeaf(node.Origin + new Coord(8, 16, 24));
            return node.Level != 2 || index == 0;
        }, parallel);
        Assert.Equal(5, visited.Count); Assert.Equal(2, tree.LeafCount); Assert.Single(visited, n => n.Level == 0);
        Assert.Equal(new Coord(8, 16, 24), visited.Single(n => n.Level == 0).Origin);
    }
    [Fact]
    public void DynamicRootCanCreateEntireTopologyAndReductionCanFilter()
    {
        var tree = new VdbTree<int>(); var manager = new DynamicNodeManager<int>(tree);
        long count = manager.ReduceTopDown(() => 0L, (acc, node, _) =>
        { if (node.IsRoot) node.TouchLeaf(new(-1, -2, -3)); return (acc + 1, true); }, (a, b) => a + b, true);
        Assert.Equal(4, count); Assert.Equal(1, tree.LeafCount);
        count = manager.ReduceTopDown(() => 0L, (acc, node, _) => (acc + 1, node.Level != 2), (a, b) => a + b, true);
        Assert.Equal(2, count);
        Assert.Throws<InvalidOperationException>(() => manager.ForEachTopDown((node, _) => { if (!node.IsRoot) tree.Clear(); return true; }));
    }
    [Fact]
    public void ScopedNodeOperationsRejectCoordinatesAndAncestorTileLevels()
    {
        var tree = Setup(); var leaf = tree.ProbeNode(new(1, 2, 3), 0)!.Value;
        Assert.Throws<ArgumentOutOfRangeException>(() => leaf.TouchLeaf(new(8, 0, 0))); Assert.Throws<ArgumentOutOfRangeException>(() => leaf.AddTile(1, new(0, 0, 0), 0, false));
        leaf.AddTile(0, new(1, 2, 3), 19, false); Assert.Equal(19, tree.GetValue(new(1, 2, 3))); Assert.False(tree.IsValueOn(new(1, 2, 3)));
        Assert.Null(tree.ProbeNode(new(81920, 0, 0), 2)); Assert.Throws<ArgumentOutOfRangeException>(() => tree.ProbeNode(default, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => new NodeManager<int>(tree, 4)); Assert.Throws<ArgumentOutOfRangeException>(() => new DynamicNodeManager<int>(tree, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new NodeManager<int>(tree).ForEachTopDown(_ => { }, grainSize: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DynamicNodeManager<int>(tree).ForEachTopDown((_, _) => true, leafGrainSize: 0));
    }
    [Fact]
    public void NodeIteratorsDetectMutationAtFinalYieldAndSupportDepthLimits()
    {
        var tree = Setup(); var all = tree.EnumerateNodes().ToArray(); Assert.Equal(tree.NodeCount().Sum(), all.Length);
        Assert.All(tree.EnumerateNodes(2, 2), node => Assert.Equal(2, node.Depth));
        using var values = tree.Root.EnumerateValues().GetEnumerator(); Assert.True(values.MoveNext()); tree.Clear(); Assert.Throws<InvalidOperationException>(() => values.MoveNext());
        using var nodes = tree.EnumerateNodes().GetEnumerator(); Assert.True(nodes.MoveNext()); tree.TouchLeaf(default); Assert.Throws<InvalidOperationException>(() => nodes.MoveNext());
    }
    private static VdbTree<int> Setup()
    {
        var tree = new VdbTree<int>(5); tree.AddTile(3, new(-4096, 0, 0), 7, true);
        tree.SetValueOn(new(1, 2, 3), 9); tree.SetValueOff(new(128, 0, 0), -2); tree.TouchLeaf(new(4096, 0, 0)); return tree;
    }
}

public class NativeNodeManagerTests
{
    private static readonly Lazy<JsonDocument> Fixture = new(() => JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "openvdb-13.0.1-node-manager.json"))));
    public static IEnumerable<object[]> Cases() { for (int seed = 0; seed < 8; seed++) for (int levels = 0; levels <= 3; levels++) for (int kind = 0; kind < 3; kind++) for (int mode = 0; mode < (kind == 2 ? 5 : 2); mode++) yield return [seed, levels, kind, mode]; }
    [Theory]
    [MemberData(nameof(Cases))]
    public void OrderedNodesAndStoredValuesMatchNative(int seed, int levels, int kind, int mode)
    {
        var root = Fixture.Value.RootElement; Assert.Equal("f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf", root.GetProperty("commit").GetString());
        var expected = root.GetProperty("cases").EnumerateArray().Single(c => c.GetProperty("seed").GetInt32() == seed && c.GetProperty("levels").GetInt32() == levels && c.GetProperty("kind").GetInt32() == kind && c.GetProperty("mode").GetInt32() == mode);
        var tree = new VdbTree<int>(seed + 1);
        if (seed != 0)
        {
            tree.AddTile(3, new(-4096, 0, 0), seed + 3, true); tree.AddTile(3, new(4096, 0, 0), seed + 5, false); tree.AddTile(2, new(0, 0, 0), seed + 7, (seed & 1) != 0);
            for (int i = 0; i < seed + 2; i++) { var c = new Coord((i % 3) * 4096 + 128 * (i / 3), -128 * (i % 2), 8 * (i % 5)); tree.SetValueOn(c, seed * 20 + i); tree.SetValueOff(c + new Coord(1, 0, 0), -seed - i); }
        }
        int next = 0;
        void Record(TreeNode<int> node)
        {
            var e = expected.GetProperty("nodes")[next++]; ulong hash = 14695981039346656037UL, count = 0, on = 0;
            foreach (var value in node.EnumerateValues(ValueIterationMode.All))
            {
                if (mode == 1 && !value.IsActive) value.SetValueOnly(value.Value + node.Level + 1);
                var c = value.Coordinate; Mix(c.X); Mix(c.Y); Mix(c.Z); Mix(value.Value); Mix(value.IsActive ? 1 : 0); count++; if (value.IsActive) on++;
            }
            Assert.Equal(e[0].GetInt32(), node.Level); Assert.Equal(new Coord(e[1].GetInt32(), e[2].GetInt32(), e[3].GetInt32()), node.Origin);
            Assert.Equal(e[4].GetInt32(), node.ChildCount); Assert.Equal(e[5].GetUInt64(), count); Assert.Equal(e[6].GetUInt64(), on); Assert.Equal(e[7].GetUInt64(), hash);
            void Mix(long value) => hash = unchecked((hash ^ (ulong)value) * 1099511628211UL);
        }
        if (kind == 2)
        {
            new DynamicNodeManager<int>(tree, levels).ForEachTopDown((node, index) =>
            {
                if (mode == 4) { if (node.IsRoot) node.TouchLeaf(new(16384, 0, 0)); else if (!node.IsLeaf) node.TouchLeaf(node.Origin + new Coord(8, 16, 24)); }
                Record(node); return mode switch { 1 => node.Level != 3, 2 => index % 2 == 0, 3 => node.Level != 1, _ => true };
            });
        }
        else { var manager = new NodeManager<int>(tree, levels); if (kind == 0) manager.ForEachTopDown(Record); else manager.ForEachBottomUp(Record); }
        Assert.Equal(expected.GetProperty("nodes").GetArrayLength(), next); Assert.Equal(expected.GetProperty("active").GetUInt64(), tree.ActiveVoxelCount);
        Assert.Equal(expected.GetProperty("counts").EnumerateArray().Select(n => n.GetInt64()), tree.NodeCount());
    }
}
