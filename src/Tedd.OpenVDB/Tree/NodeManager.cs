// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
namespace Tedd.OpenVDB.Tree;

/// <summary>Linear caches of allocated nodes, processed one hierarchy level at a time.</summary>
/// <remarks>Levels selects the top 0..3 levels below the root. Structural edits require Rebuild.
/// Parallel callbacks act on distinct nodes; they must not mutate topology or share mutable state.</remarks>
public sealed class NodeManager<T> where T : unmanaged
{
    private readonly VdbTree<T> _tree;
    private TreeNode<T>[][] _nodes = [[], [], []];
    private long _version;
    public NodeManager(VdbTree<T> tree, int levels = 3)
    {
        _tree = tree ?? throw new ArgumentNullException(nameof(tree));
        if ((uint)levels > 3) throw new ArgumentOutOfRangeException(nameof(levels));
        Levels = levels; Rebuild();
    }
    public NodeManager(LeafManager<T> leaves, int levels = 3) : this((leaves ?? throw new ArgumentNullException(nameof(leaves))).Tree, levels) { }
    public int Levels { get; }
    public TreeNode<T> Root => _tree.Root;
    public bool IsValid => _version == _tree.TopologyVersion;
    /// <summary>Total cached nodes, excluding the root.</summary>
    public long NodeCount { get { Validate(); return _nodes.Sum(nodes => (long)nodes.Length); } }
    public int GetNodeCount(int level) { ValidateLevel(level); Validate(); return _nodes[level].Length; }
    public TreeNode<T> GetNode(int level, int index) { ValidateLevel(level); Validate(); return _nodes[level][index]; }
    public void Clear() { _nodes = [[], [], []]; _version = _tree.TopologyVersion; }
    public void Rebuild()
    {
        long version = _tree.TopologyVersion;
        TreeNode<T>[][] nodes = [[], [], []];
        TreeNode<T>[] parents = [_tree.Root];
        for (int level = 2; level >= 3 - Levels; level--)
        { nodes[level] = parents.SelectMany(parent => parent.EnumerateChildren()).ToArray(); parents = nodes[level]; }
        if (version != _tree.TopologyVersion) throw new InvalidOperationException("Tree topology changed while rebuilding the node manager.");
        _nodes = nodes; _version = version;
    }
    public void ForEachTopDown(Action<TreeNode<T>> operation, bool parallel = false, int grainSize = 1)
    { ArgumentNullException.ThrowIfNull(operation); ForEachTopDown((node, _) => operation(node), parallel, grainSize); }
    public void ForEachBottomUp(Action<TreeNode<T>> operation, bool parallel = false, int grainSize = 1)
    { ArgumentNullException.ThrowIfNull(operation); ForEachBottomUp((node, _) => operation(node), parallel, grainSize); }
    public void ForEachTopDown(Action<TreeNode<T>, int> operation, bool parallel = false, int grainSize = 1) => ForEach(operation, true, parallel, grainSize);
    public void ForEachBottomUp(Action<TreeNode<T>, int> operation, bool parallel = false, int grainSize = 1) => ForEach(operation, false, parallel, grainSize);
    private void ForEach(Action<TreeNode<T>, int> operation, bool topDown, bool parallel, int grainSize)
    {
        ArgumentNullException.ThrowIfNull(operation); ArgumentOutOfRangeException.ThrowIfNegativeOrZero(grainSize); Validate();
        if (topDown) { operation(Root, 0); Validate(); }
        for (int step = 0; step < Levels; step++)
        {
            int level = topDown ? 2 - step : 3 - Levels + step;
            var nodes = _nodes[level];
            NodeExecution.ForEach(nodes.Length, parallel, grainSize, index => { Validate(); operation(nodes[index], index); Validate(); });
        }
        if (!topDown) { operation(Root, 0); Validate(); }
    }
    /// <summary>Reduces in native top-down level order. Factory must create an independent identity accumulator.</summary>
    /// <remarks>For parallel execution join must be associative; partition results are joined in index order.</remarks>
    public TResult ReduceTopDown<TResult>(Func<TResult> factory, Func<TResult, TreeNode<T>, int, TResult> operation,
        Func<TResult, TResult, TResult> join, bool parallel = false, int grainSize = 1) => Reduce(factory, operation, join, true, parallel, grainSize);
    public TResult ReduceBottomUp<TResult>(Func<TResult> factory, Func<TResult, TreeNode<T>, int, TResult> operation,
        Func<TResult, TResult, TResult> join, bool parallel = false, int grainSize = 1) => Reduce(factory, operation, join, false, parallel, grainSize);
    private TResult Reduce<TResult>(Func<TResult> factory, Func<TResult, TreeNode<T>, int, TResult> operation,
        Func<TResult, TResult, TResult> join, bool topDown, bool parallel, int grainSize)
    {
        ArgumentNullException.ThrowIfNull(factory); ArgumentNullException.ThrowIfNull(operation); ArgumentNullException.ThrowIfNull(join);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(grainSize); Validate(); TResult result = factory();
        if (topDown) { result = operation(result, Root, 0); Validate(); }
        for (int step = 0; step < Levels; step++)
        {
            int level = topDown ? 2 - step : 3 - Levels + step; var nodes = _nodes[level];
            if (!parallel)
            {
                for (int index = 0; index < nodes.Length; index++) { result = operation(result, nodes[index], index); Validate(); }
            }
            else
            {
                int partitions = (int)(((long)nodes.Length + grainSize - 1) / grainSize);
                var results = new TResult[partitions];
                Parallel.For(0, partitions, partition =>
                {
                    TResult local = factory(); int start = checked(partition * grainSize), end = (int)System.Math.Min((long)start + grainSize, nodes.Length);
                    for (int index = start; index < end; index++) { Validate(); local = operation(local, nodes[index], index); Validate(); }
                    results[partition] = local;
                });
                foreach (TResult local in results) result = join(result, local);
                Validate();
            }
        }
        if (!topDown) { result = operation(result, Root, 0); Validate(); }
        return result;
    }
    private static void ValidateLevel(int level) { if ((uint)level > 2) throw new ArgumentOutOfRangeException(nameof(level)); }
    private void Validate() { if (!IsValid) throw new InvalidOperationException("Tree topology changed. Rebuild the node manager."); }
}

internal static class NodeExecution
{
    internal static void ForEach(int count, bool parallel, int grainSize, Action<int> operation)
    {
        if (!parallel) { for (int index = 0; index < count; index++) operation(index); return; }
        int partitions = (int)(((long)count + grainSize - 1) / grainSize);
        Parallel.For(0, partitions, partition =>
        {
            int start = checked(partition * grainSize), end = (int)System.Math.Min((long)start + grainSize, count);
            for (int index = start; index < end; index++) operation(index);
        });
    }
}
