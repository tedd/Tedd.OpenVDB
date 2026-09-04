// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
namespace Tedd.OpenVDB.Tree;

/// <summary>Top-down traversal which discovers children after their parent callback completes.</summary>
/// <remarks>Returning false omits that node's descendants. Callbacks may change their own descendants,
/// but must not detach the current node or mutate another visited node. Parallel callbacks require
/// independent state and subtree-scoped TreeNode operations; arbitrary concurrent tree writes are unsupported.</remarks>
public sealed class DynamicNodeManager<T> where T : unmanaged
{
    private readonly VdbTree<T> _tree;
    public DynamicNodeManager(VdbTree<T> tree, int levels = 3)
    {
        _tree = tree ?? throw new ArgumentNullException(nameof(tree));
        if ((uint)levels > 3) throw new ArgumentOutOfRangeException(nameof(levels));
        Levels = levels;
    }
    public DynamicNodeManager(LeafManager<T> leaves, int levels = 3) : this((leaves ?? throw new ArgumentNullException(nameof(leaves))).Tree, levels) { }
    public int Levels { get; }
    public TreeNode<T> Root => _tree.Root;
    public void ForEachTopDown(Func<TreeNode<T>, int, bool> operation, bool parallel = false, int leafGrainSize = 1, int nonLeafGrainSize = 1)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ValidateGrains(leafGrainSize, nonLeafGrainSize);
        TreeNode<T>[] parents = [Root]; bool[] accepted = [true];
        for (int depth = 0; depth <= Levels; depth++)
        {
            if (depth != 0) parents = Children(parents, accepted);
            if (parents.Length == 0) return;
            accepted = new bool[parents.Length];
            NodeExecution.ForEach(parents.Length, parallel && depth != 0, depth == 3 ? leafGrainSize : nonLeafGrainSize,
                index => { accepted[index] = operation(parents[index], index); ValidateAttached(parents[index]); });
        }
    }
    /// <summary>Reduces accepted subtrees. Each operation returns the updated accumulator and whether to descend.</summary>
    public TResult ReduceTopDown<TResult>(Func<TResult> factory,
        Func<TResult, TreeNode<T>, int, (TResult State, bool Descend)> operation,
        Func<TResult, TResult, TResult> join, bool parallel = false, int leafGrainSize = 1, int nonLeafGrainSize = 1)
    {
        ArgumentNullException.ThrowIfNull(factory); ArgumentNullException.ThrowIfNull(operation); ArgumentNullException.ThrowIfNull(join);
        ValidateGrains(leafGrainSize, nonLeafGrainSize); TResult result = factory();
        TreeNode<T>[] parents = [Root]; bool[] accepted = [true];
        for (int depth = 0; depth <= Levels; depth++)
        {
            if (depth != 0) parents = Children(parents, accepted);
            if (parents.Length == 0) return result;
            accepted = new bool[parents.Length];
            if (!parallel || depth == 0)
            {
                for (int index = 0; index < parents.Length; index++)
                { (result, accepted[index]) = operation(result, parents[index], index); ValidateAttached(parents[index]); }
            }
            else
            {
                int grain = depth == 3 ? leafGrainSize : nonLeafGrainSize;
                int count = (int)(((long)parents.Length + grain - 1) / grain); var results = new TResult[count];
                Parallel.For(0, count, partition =>
                {
                    TResult local = factory(); int start = checked(partition * grain), end = (int)System.Math.Min((long)start + grain, parents.Length);
                    for (int index = start; index < end; index++)
                    { (local, accepted[index]) = operation(local, parents[index], index); ValidateAttached(parents[index]); }
                    results[partition] = local;
                });
                foreach (TResult local in results) result = join(result, local);
            }
        }
        return result;
    }
    private static TreeNode<T>[] Children(TreeNode<T>[] parents, bool[] accepted)
    {
        var children = new List<TreeNode<T>>();
        for (int index = 0; index < parents.Length; index++) if (accepted[index]) children.AddRange(parents[index].EnumerateChildren());
        return children.ToArray();
    }
    private static void ValidateAttached(TreeNode<T> node) { if (!node.IsValid) throw new InvalidOperationException("A dynamic callback detached its current node."); }
    private static void ValidateGrains(int leaf, int nonLeaf)
    { ArgumentOutOfRangeException.ThrowIfNegativeOrZero(leaf); ArgumentOutOfRangeException.ThrowIfNegativeOrZero(nonLeaf); }
}
